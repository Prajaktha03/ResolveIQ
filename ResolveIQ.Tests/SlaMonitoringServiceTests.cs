using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ResolveIQ.Api.Controllers;
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Models;
using ResolveIQ.Api.Services;
using Xunit;

namespace ResolveIQ.Tests;

public class SlaMonitoringServiceTests
{
    private readonly DateTime _baseTime = new(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task EvaluateDeadlines_RecordsBreachEvents_ForOverdueActiveTickets()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        // Ticket 1: FirstResponse overdue, Resolution not overdue
        var ticket1 = new Ticket
        {
            Id = 30,
            Title = "Ticket 1",
            Status = "Open",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            FirstResponseDueAt = _baseTime.AddHours(-1), // Passed 1 hour ago
            FirstRespondedAt = null,
            ResolutionDueAt = _baseTime.AddHours(5),     // Still 5 hours remaining
            ResolvedAt = null
        };

        // Ticket 2: Both FirstResponse and Resolution overdue
        var ticket2 = new Ticket
        {
            Id = 31,
            Title = "Ticket 2",
            Status = "InProgress",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            FirstResponseDueAt = _baseTime.AddHours(-3), // Passed
            FirstRespondedAt = null,
            ResolutionDueAt = _baseTime.AddHours(-1),    // Passed
            ResolvedAt = null
        };

        // Ticket 3: Already resolved -> Should NOT be flagged by monitoring
        var ticket3 = new Ticket
        {
            Id = 32,
            Title = "Ticket 3",
            Status = "Resolved",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            FirstResponseDueAt = _baseTime.AddHours(-2),
            FirstRespondedAt = null,
            ResolutionDueAt = _baseTime.AddHours(-1),
            ResolvedAt = _baseTime.AddHours(-1)
        };

        db.Tickets.AddRange(ticket1, ticket2, ticket3);
        await db.SaveChangesAsync();

        var timeProvider = new TestTimeProvider(_baseTime);
        var monitoringService = new SlaMonitoringService(db, timeProvider);

        var recorded = await monitoringService.EvaluateDeadlinesAsync();

        // Ticket 1: 1 breach (FirstResponse)
        // Ticket 2: 2 breaches (FirstResponse, Resolution)
        // Ticket 3: 0 (Resolved)
        // Total = 3
        Assert.Equal(3, recorded);

        // Verify ticket1 status was NOT changed to Resolved
        var updatedTicket1 = await db.Tickets.FindAsync(ticket1.Id);
        Assert.Equal("Open", updatedTicket1!.Status);

        // Verify ticket2 status was NOT changed to Resolved
        var updatedTicket2 = await db.Tickets.FindAsync(ticket2.Id);
        Assert.Equal("InProgress", updatedTicket2!.Status);

        // Check history actions
        var histories = await db.TicketHistories.ToListAsync();
        Assert.Contains(histories, h => h.TicketId == ticket1.Id && h.Action == "FirstResponseBreached");
        Assert.Contains(histories, h => h.TicketId == ticket2.Id && h.Action == "FirstResponseBreached");
        Assert.Contains(histories, h => h.TicketId == ticket2.Id && h.Action == "ResolutionBreached");
        Assert.DoesNotContain(histories, h => h.TicketId == ticket3.Id);
    }

    [Fact]
    public async Task EvaluateDeadlines_IsIdempotent_DoesNotDuplicateEventsOnSubsequentPolls()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var ticket = new Ticket
        {
            Id = 33,
            Title = "Overdue Ticket",
            Status = "Open",
            CreatedByUserId = employee.Id,
            FirstResponseDueAt = _baseTime.AddHours(-2),
            FirstRespondedAt = null,
            ResolutionDueAt = _baseTime.AddHours(-1),
            ResolvedAt = null
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var timeProvider = new TestTimeProvider(_baseTime);
        var monitoringService = new SlaMonitoringService(db, timeProvider);

        // First run records 2 events
        var firstRunCount = await monitoringService.EvaluateDeadlinesAsync();
        Assert.Equal(2, firstRunCount);

        // Second run (subsequent poll) must record 0 new events
        var secondRunCount = await monitoringService.EvaluateDeadlinesAsync();
        Assert.Equal(0, secondRunCount);

        // Third run with time advanced must still record 0 new events
        timeProvider.Advance(TimeSpan.FromMinutes(30));
        var thirdRunCount = await monitoringService.EvaluateDeadlinesAsync();
        Assert.Equal(0, thirdRunCount);

        var totalHistories = await db.TicketHistories.CountAsync(h => h.TicketId == ticket.Id);
        Assert.Equal(2, totalHistories);
    }

    [Fact]
    public async Task PriorityChange_RecalculatesDeadlinesFromOriginalCreatedAt_PreservesClock()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var slaService = new SlaService();
        var ticketService = new TicketService(db, slaService);

        var createdAt = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
        var initialDeadlines = slaService.CalculateDeadlines("Low", createdAt);

        var ticket = new Ticket
        {
            Id = 34,
            Title = "Low priority issue",
            Description = "Something minor",
            Priority = "Low",
            Status = "Open",
            CreatedAt = createdAt,
            CreatedByUserId = employee.Id,
            FirstResponseDueAt = initialDeadlines.FirstResponseDueAt,
            ResolutionDueAt = initialDeadlines.ResolutionDueAt
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        // Escalate priority to Critical
        var updateRequest = new UpdateTicketRequest
        {
            Status = "Open",
            Priority = "Critical"
        };
        var updated = await ticketService.UpdateAsync(ticket.Id, updateRequest, agent.Id);

        Assert.NotNull(updated);
        // Targets for Critical are 15m and 4h
        // Must be calculated from original createdAt (9:00:00), NOT from current time!
        Assert.Equal(createdAt.AddMinutes(15), updated.FirstResponseDueAt);
        Assert.Equal(createdAt.AddHours(4), updated.ResolutionDueAt);

        // Verify audit history
        var history = await db.TicketHistories
            .FirstOrDefaultAsync(h => h.TicketId == ticket.Id && h.Action == "PriorityChanged");
        Assert.NotNull(history);
        Assert.Equal("Low", history.OldValue);
        Assert.Equal("Critical", history.NewValue);
    }

    [Fact]
    public async Task StatusChange_ResolvingSetsResolvedAt_ReopeningClearsResolvedAt()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var slaService = new SlaService();
        var ticketService = new TicketService(db, slaService);

        var ticket = new Ticket
        {
            Id = 35,
            Title = "Laptop battery",
            Priority = "Medium",
            Status = "InProgress",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            ResolvedAt = null
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        // 1. Resolve ticket
        var resolveRequest = new UpdateTicketRequest { Status = "Resolved", Priority = "Medium" };
        var resolvedTicket = await ticketService.UpdateAsync(ticket.Id, resolveRequest, agent.Id);
        Assert.NotNull(resolvedTicket!.ResolvedAt);

        var firstResolvedAt = resolvedTicket.ResolvedAt;

        // 2. Reopen ticket
        var reopenRequest = new UpdateTicketRequest { Status = "InProgress", Priority = "Medium" };
        var reopenedTicket = await ticketService.UpdateAsync(ticket.Id, reopenRequest, agent.Id);
        Assert.Null(reopenedTicket!.ResolvedAt);

        // 3. Re-resolve ticket
        var reResolveRequest = new UpdateTicketRequest { Status = "Resolved", Priority = "Medium" };
        var reResolvedTicket = await ticketService.UpdateAsync(ticket.Id, reResolveRequest, agent.Id);
        Assert.NotNull(reResolvedTicket!.ResolvedAt);
        Assert.True(reResolvedTicket.ResolvedAt >= firstResolvedAt);
    }

    [Fact]
    public async Task Reassignment_DoesNotResetOrChangeSlaDeadlines()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var agent2 = new User { Id = 88, FullName = "Second Agent", Email = "agent2@test.com", Role = "Agent" };
        db.Users.Add(agent2);

        var firstResponseDue = DateTime.UtcNow.AddHours(2);
        var resolutionDue = DateTime.UtcNow.AddHours(10);

        var ticket = new Ticket
        {
            Id = 36,
            Title = "Network config",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            FirstResponseDueAt = firstResponseDue,
            ResolutionDueAt = resolutionDue
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var slaService = new SlaService();
        var ticketService = new TicketService(db, slaService);
        var controller = new TicketsController(ticketService, db, slaService);
        TestHelpers.SetUserContext(controller, admin.Id, "Admin");

        // Reassign from agent 1 to agent 2
        var result = await controller.Assign(ticket.Id, agent2.Id);
        Assert.IsType<OkObjectResult>(result);

        var reassignedTicket = await db.Tickets.FindAsync(ticket.Id);
        Assert.NotNull(reassignedTicket);
        Assert.Equal(agent2.Id, reassignedTicket.AssignedToUserId);

        // Milestone 3 requirement: Reassignment must not reset deadlines merely because agent changes
        Assert.Equal(firstResponseDue, reassignedTicket.FirstResponseDueAt);
        Assert.Equal(resolutionDue, reassignedTicket.ResolutionDueAt);
    }
}
