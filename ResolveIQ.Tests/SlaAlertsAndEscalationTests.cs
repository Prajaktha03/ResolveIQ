using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ResolveIQ.Api.Controllers;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Models;
using ResolveIQ.Api.Services;
using Xunit;

namespace ResolveIQ.Tests;

public class SlaAlertsAndEscalationTests
{
    private readonly DateTime _baseTime = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    private static IConfiguration CreateConfiguration(int warningMinutes = 15, int escalationDelayMinutes = 30)
    {
        var settings = new Dictionary<string, string?>
        {
            { "SlaMonitoring:WarningWindowMinutes", warningMinutes.ToString() },
            { "SlaMonitoring:EscalationDelayMinutes", escalationDelayMinutes.ToString() }
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    [Fact]
    public async Task SlaMonitoring_GeneratesAtRiskWarning_WhenEnteringWarningWindow()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        // Ticket whose first response is due in 10 minutes (within 15m warning window)
        var ticket = new Ticket
        {
            Id = 101,
            Title = "Network Switch Warning",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            Priority = "High",
            Status = "Open",
            FirstResponseDueAt = _baseTime.AddMinutes(10), // 10 mins from now -> AT RISK!
            FirstRespondedAt = null,
            ResolutionDueAt = _baseTime.AddHours(4),
            ResolvedAt = null
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var timeProvider = new TestTimeProvider(_baseTime);
        var notifService = new NotificationService(db);
        var config = CreateConfiguration(warningMinutes: 15);
        var monitoring = new SlaMonitoringService(db, timeProvider, null, notifService, config);

        var breaches = await monitoring.EvaluateDeadlinesAsync();
        Assert.Equal(0, breaches); // Not breached yet, so 0 breach events

        // Agent and Admin should have received SlaAtRisk notifications
        var agentNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == agent.Id && n.TicketId == ticket.Id && n.Type == "SlaAtRisk");
        Assert.NotNull(agentNotif);
        Assert.Contains("First Response Due Soon", agentNotif.Title);

        var adminNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == admin.Id && n.TicketId == ticket.Id && n.Type == "SlaAtRisk");
        Assert.NotNull(adminNotif);
    }

    [Fact]
    public async Task SlaMonitoring_GeneratesBreachAlert_WhenDeadlinePassed()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        // Ticket whose first response deadline passed 5 minutes ago
        var ticket = new Ticket
        {
            Id = 102,
            Title = "Broken Fiber Cable",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            Priority = "Critical",
            Status = "Open",
            FirstResponseDueAt = _baseTime.AddMinutes(-5), // 5 mins in the past -> BREACHED!
            FirstRespondedAt = null,
            ResolutionDueAt = _baseTime.AddHours(2),
            ResolvedAt = null
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var timeProvider = new TestTimeProvider(_baseTime);
        var notifService = new NotificationService(db);
        var config = CreateConfiguration();
        var monitoring = new SlaMonitoringService(db, timeProvider, null, notifService, config);

        var breaches = await monitoring.EvaluateDeadlinesAsync();
        Assert.Equal(1, breaches);

        // 1. History logged
        var history = await db.TicketHistories.FirstOrDefaultAsync(h => h.TicketId == ticket.Id && h.Action == "FirstResponseBreached");
        Assert.NotNull(history);
        Assert.Null(history.UserId); // System actor

        // 2. Notifications dispatched
        var agentNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == agent.Id && n.TicketId == ticket.Id && n.Type == "SlaBreached");
        Assert.NotNull(agentNotif);
        Assert.Contains("First Response Missed", agentNotif.Title);

        var adminNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == admin.Id && n.TicketId == ticket.Id && n.Type == "SlaBreached");
        Assert.NotNull(adminNotif);
    }

    [Fact]
    public async Task SlaMonitoring_Idempotent_RepeatedPollsDoNotCreateDuplicateAlertsOrHistories()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var ticket = new Ticket
        {
            Id = 103,
            Title = "Duplicate Polling Test",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            Priority = "Medium",
            Status = "Open",
            FirstResponseDueAt = _baseTime.AddMinutes(-10),
            FirstRespondedAt = null,
            ResolutionDueAt = _baseTime.AddHours(2),
            ResolvedAt = null
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var timeProvider = new TestTimeProvider(_baseTime);
        var notifService = new NotificationService(db);
        var config = CreateConfiguration();
        var monitoring = new SlaMonitoringService(db, timeProvider, null, notifService, config);

        // First poll
        var poll1 = await monitoring.EvaluateDeadlinesAsync();
        Assert.Equal(1, poll1);
        var notifCount1 = await db.Notifications.CountAsync();
        var historyCount1 = await db.TicketHistories.CountAsync();

        // Second poll at same time
        var poll2 = await monitoring.EvaluateDeadlinesAsync();
        Assert.Equal(0, poll2); // 0 new breach events

        // Third poll after 5 minutes
        timeProvider.Advance(TimeSpan.FromMinutes(5));
        var poll3 = await monitoring.EvaluateDeadlinesAsync();
        Assert.Equal(0, poll3); // 0 new breach events

        var notifCountFinal = await db.Notifications.CountAsync();
        var historyCountFinal = await db.TicketHistories.CountAsync();

        // Count must remain identical (idempotent, no duplicates)
        Assert.Equal(notifCount1, notifCountFinal);
        Assert.Equal(historyCount1, historyCountFinal);
    }

    [Fact]
    public async Task SlaMonitoring_EscalatesCriticalAndHighTickets_PastEscalationDelay()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        // Critical ticket that breached first response 40 minutes ago (escalation delay is 30 mins)
        var ticket = new Ticket
        {
            Id = 104,
            Title = "Critical Data Center Outage",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            Priority = "Critical",
            Status = "Open",
            FirstResponseDueAt = _baseTime.AddMinutes(-40),
            FirstRespondedAt = null,
            ResolutionDueAt = _baseTime.AddHours(2),
            ResolvedAt = null
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var timeProvider = new TestTimeProvider(_baseTime);
        var notifService = new NotificationService(db);
        var config = CreateConfiguration(escalationDelayMinutes: 30);
        var monitoring = new SlaMonitoringService(db, timeProvider, null, notifService, config);

        await monitoring.EvaluateDeadlinesAsync();

        // Escalation history entry logged
        var escalationHistory = await db.TicketHistories.FirstOrDefaultAsync(h => h.TicketId == ticket.Id && h.Action == "Escalated");
        Assert.NotNull(escalationHistory);
        Assert.Null(escalationHistory.UserId); // System actor
        Assert.Contains("FirstResponse", escalationHistory.NewValue);

        // Admin received escalation notification
        var adminEscalationNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == admin.Id && n.TicketId == ticket.Id && n.Type == "Escalated");
        Assert.NotNull(adminEscalationNotif);
        Assert.Contains("[ESCALATION]", adminEscalationNotif.Title);

        // Consecutive polling must NOT create duplicate escalation
        await monitoring.EvaluateDeadlinesAsync();
        var escalationHistoryCount = await db.TicketHistories.CountAsync(h => h.TicketId == ticket.Id && h.Action == "Escalated");
        Assert.Equal(1, escalationHistoryCount);
    }

    [Fact]
    public async Task SlaMonitoring_NoFirstResponseAlerts_AfterFirstResponseRecorded()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, _) = await TestHelpers.SeedDefaultUsersAsync(db);

        // Ticket with due date in past, but valid FirstRespondedAt was recorded
        var ticket = new Ticket
        {
            Id = 105,
            Title = "Already Responded",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            Priority = "High",
            Status = "InProgress",
            FirstResponseDueAt = _baseTime.AddMinutes(-20),
            FirstRespondedAt = _baseTime.AddMinutes(-25), // Met before deadline!
            ResolutionDueAt = _baseTime.AddHours(5),
            ResolvedAt = null
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var timeProvider = new TestTimeProvider(_baseTime);
        var notifService = new NotificationService(db);
        var config = CreateConfiguration();
        var monitoring = new SlaMonitoringService(db, timeProvider, null, notifService, config);

        var recorded = await monitoring.EvaluateDeadlinesAsync();
        Assert.Equal(0, recorded);

        // No FirstResponse breach history or notification
        var history = await db.TicketHistories.FirstOrDefaultAsync(h => h.TicketId == ticket.Id && h.Action == "FirstResponseBreached");
        Assert.Null(history);

        var notif = await db.Notifications.FirstOrDefaultAsync(n => n.TicketId == ticket.Id);
        Assert.Null(notif);
    }

    [Fact]
    public async Task SlaMonitoring_NoResolutionAlerts_AfterTicketResolved()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, _) = await TestHelpers.SeedDefaultUsersAsync(db);

        // Ticket with resolution deadline in past, but Status is Resolved
        var ticket = new Ticket
        {
            Id = 106,
            Title = "Resolved Ticket",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            Priority = "Medium",
            Status = "Resolved",
            FirstResponseDueAt = _baseTime.AddMinutes(-60),
            FirstRespondedAt = _baseTime.AddMinutes(-50),
            ResolutionDueAt = _baseTime.AddMinutes(-10),
            ResolvedAt = _baseTime.AddMinutes(-5)
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var timeProvider = new TestTimeProvider(_baseTime);
        var notifService = new NotificationService(db);
        var config = CreateConfiguration();
        var monitoring = new SlaMonitoringService(db, timeProvider, null, notifService, config);

        var recorded = await monitoring.EvaluateDeadlinesAsync();
        Assert.Equal(0, recorded);
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task GetEscalations_AdminCanViewEscalationHistory_NonAdminForbidden()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var ticket = new Ticket
        {
            Id = 107,
            Title = "Escalated Core Switch Failure",
            Priority = "Critical",
            Status = "Open",
            AssignedToUserId = agent.Id
        };
        db.Tickets.Add(ticket);

        db.TicketHistories.Add(new TicketHistory
        {
            TicketId = ticket.Id,
            UserId = null,
            Action = "Escalated",
            OldValue = "Critical",
            NewValue = "Escalated to Admins due to unaddressed FirstResponse SLA breach on Critical ticket.",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var slaService = new SlaService();
        var ticketService = new TicketService(db, slaService);
        var controller = new TicketsController(ticketService, db, slaService);

        // 1. Admin accessing -> 200 OK with escalation list
        TestHelpers.SetUserContext(controller, admin.Id, "Admin");
        var adminResult = await controller.GetEscalations();
        var okResult = Assert.IsType<OkObjectResult>(adminResult);
        var list = Assert.IsAssignableFrom<List<EscalationEventResponse>>(okResult.Value);
        Assert.Single(list);
        Assert.Equal(ticket.Id, list[0].TicketId);
        Assert.Equal("Critical", list[0].Priority);

        // 2. Employee accessing -> 403 Forbidden
        TestHelpers.SetUserContext(controller, employee.Id, "Employee");
        var empResult = await controller.GetEscalations();
        Assert.IsType<ForbidResult>(empResult);

        // 3. Agent accessing -> 403 Forbidden
        TestHelpers.SetUserContext(controller, agent.Id, "Agent");
        var agentResult = await controller.GetEscalations();
        Assert.IsType<ForbidResult>(agentResult);
    }
}
