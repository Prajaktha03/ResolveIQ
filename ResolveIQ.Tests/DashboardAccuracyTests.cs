using Microsoft.AspNetCore.Mvc;
using ResolveIQ.Api.Controllers;
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Models;
using ResolveIQ.Api.Services;
using Xunit;

namespace ResolveIQ.Tests;

public class DashboardAccuracyTests
{
    private readonly DateTime _baseTime = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetDashboard_ScopesTotalsByRole()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var employee2 = new User { Id = 4, FullName = "Emp 2", Email = "emp2@test.com", Role = "Employee" };
        var agent2 = new User { Id = 5, FullName = "Agent 2", Email = "agent2@test.com", Role = "Agent" };
        db.Users.AddRange(employee2, agent2);

        // Ticket 1: created by emp1, assigned to agent1
        // Ticket 2: created by emp1, assigned to agent2
        // Ticket 3: created by emp2, assigned to agent1
        // Ticket 4: created by emp2, assigned to agent2
        db.Tickets.AddRange(
            new Ticket { Id = 40, Title = "T1", CreatedByUserId = employee.Id, AssignedToUserId = agent.Id, Status = "Open" },
            new Ticket { Id = 41, Title = "T2", CreatedByUserId = employee.Id, AssignedToUserId = agent2.Id, Status = "InProgress" },
            new Ticket { Id = 42, Title = "T3", CreatedByUserId = employee2.Id, AssignedToUserId = agent.Id, Status = "Resolved" },
            new Ticket { Id = 43, Title = "T4", CreatedByUserId = employee2.Id, AssignedToUserId = agent2.Id, Status = "Open" }
        );
        await db.SaveChangesAsync();

        var slaService = new SlaService(new TestTimeProvider(_baseTime));
        var ticketService = new TicketService(db, slaService);
        var controller = new TicketsController(ticketService, db, slaService);

        // 1. Employee sees only tickets created by them (T1, T2) -> 2 tickets
        TestHelpers.SetUserContext(controller, employee.Id, "Employee");
        var empResult = Assert.IsType<OkObjectResult>(await controller.GetDashboard());
        var empDashboard = Assert.IsType<TicketDashboardResponse>(empResult.Value);
        Assert.Equal(2, empDashboard.TotalTickets);
        Assert.Equal(1, empDashboard.OpenTickets);
        Assert.Equal(1, empDashboard.InProgressTickets);
        Assert.Equal(0, empDashboard.ResolvedTickets);

        // 2. Agent sees only tickets assigned to them (T1, T3) -> 2 tickets
        TestHelpers.SetUserContext(controller, agent.Id, "Agent");
        var agentResult = Assert.IsType<OkObjectResult>(await controller.GetDashboard());
        var agentDashboard = Assert.IsType<TicketDashboardResponse>(agentResult.Value);
        Assert.Equal(2, agentDashboard.TotalTickets);
        Assert.Equal(1, agentDashboard.OpenTickets);
        Assert.Equal(0, agentDashboard.InProgressTickets);
        Assert.Equal(1, agentDashboard.ResolvedTickets);

        // 3. Admin sees all tickets (T1, T2, T3, T4) -> 4 tickets
        TestHelpers.SetUserContext(controller, admin.Id, "Admin");
        var adminResult = Assert.IsType<OkObjectResult>(await controller.GetDashboard());
        var adminDashboard = Assert.IsType<TicketDashboardResponse>(adminResult.Value);
        Assert.Equal(4, adminDashboard.TotalTickets);
        Assert.Equal(2, adminDashboard.OpenTickets);
        Assert.Equal(1, adminDashboard.InProgressTickets);
        Assert.Equal(1, adminDashboard.ResolvedTickets);
    }

    [Fact]
    public async Task GetDashboard_CalculatesMetricsAccurately()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var timeProvider = new TestTimeProvider(_baseTime);
        var slaService = new SlaService(timeProvider);
        var ticketService = new TicketService(db, slaService);
        var controller = new TicketsController(ticketService, db, slaService);

        // Ticket A: Open, FirstResponse At Risk (30m remaining), Resolution On Track (10h remaining)
        // -> AtRiskTickets: 1, BreachedTickets: 0, SlaChecksMet: 0, SlaChecksBreached: 0
        var ticketA = new Ticket
        {
            Id = 50,
            Title = "Ticket A",
            Status = "Open",
            CreatedByUserId = employee.Id,
            FirstResponseDueAt = _baseTime.AddMinutes(30),
            FirstRespondedAt = null,
            ResolutionDueAt = _baseTime.AddHours(10),
            ResolvedAt = null
        };

        // Ticket B: Open, BOTH FirstResponse and Resolution are At Risk (both within 1 hr)
        // -> Must be counted UNIQUELY once in AtRiskTickets!
        var ticketB = new Ticket
        {
            Id = 51,
            Title = "Ticket B",
            Status = "Open",
            CreatedByUserId = employee.Id,
            FirstResponseDueAt = _baseTime.AddMinutes(15),
            FirstRespondedAt = null,
            ResolutionDueAt = _baseTime.AddMinutes(45),
            ResolvedAt = null
        };

        // Ticket C: InProgress, FirstResponse Met, Resolution Breached (overdue by 1 hour)
        // -> AtRisk: 0, BreachedTickets: 1, SlaChecksMet: 1, SlaChecksBreached: 1
        var ticketC = new Ticket
        {
            Id = 52,
            Title = "Ticket C",
            Status = "InProgress",
            CreatedByUserId = employee.Id,
            FirstResponseDueAt = _baseTime.AddHours(-2),
            FirstRespondedAt = _baseTime.AddHours(-2).AddMinutes(-10), // Met
            ResolutionDueAt = _baseTime.AddHours(-1),                  // Overdue
            ResolvedAt = null
        };

        // Ticket D: Resolved without first response!
        // FirstResponse is Breached (never met), Resolution Met
        // -> MUST NOT be in AtRiskTickets!
        // -> BreachedTickets: 1 (unique ticket breached)
        // -> SlaChecksMet: 1, SlaChecksBreached: 1
        var ticketD = new Ticket
        {
            Id = 53,
            Title = "Ticket D",
            Status = "Resolved",
            CreatedByUserId = employee.Id,
            FirstResponseDueAt = _baseTime.AddHours(-3),
            FirstRespondedAt = null,                                  // Breached
            ResolutionDueAt = _baseTime.AddHours(2),
            ResolvedAt = _baseTime.AddHours(1)                        // Met
        };

        db.Tickets.AddRange(ticketA, ticketB, ticketC, ticketD);
        await db.SaveChangesAsync();

        TestHelpers.SetUserContext(controller, admin.Id, "Admin");
        var result = Assert.IsType<OkObjectResult>(await controller.GetDashboard());
        var dashboard = Assert.IsType<TicketDashboardResponse>(result.Value);

        Assert.Equal(4, dashboard.TotalTickets);
        Assert.Equal(2, dashboard.OpenTickets);
        Assert.Equal(1, dashboard.InProgressTickets);
        Assert.Equal(1, dashboard.ResolvedTickets);

        // AtRisk: Ticket A (1) + Ticket B (1) = 2.
        // Ticket D is Resolved and MUST NOT be counted in AtRisk!
        Assert.Equal(2, dashboard.AtRiskTickets);

        // BreachedTickets: Ticket C (1) + Ticket D (1) = 2 unique tickets
        Assert.Equal(2, dashboard.BreachedTickets);

        // SlaChecksMet: Ticket C (1) + Ticket D (1) = 2
        Assert.Equal(2, dashboard.SlaChecksMet);

        // SlaChecksBreached: Ticket C (1) + Ticket D (1) = 2
        Assert.Equal(2, dashboard.SlaChecksBreached);

        // Compliance percentage: 2 met / (2 met + 2 breached) = 50.0%
        // Pending milestones on Ticket A & B are excluded from the denominator!
        Assert.Equal(50.0, dashboard.SlaCompliancePercentage);
    }

    [Fact]
    public async Task GetDashboard_WithZeroCompletedChecks_ReturnsZeroComplianceSafely()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var timeProvider = new TestTimeProvider(_baseTime);
        var slaService = new SlaService(timeProvider);
        var ticketService = new TicketService(db, slaService);
        var controller = new TicketsController(ticketService, db, slaService);

        // Ticket with only pending deadlines
        var ticket = new Ticket
        {
            Id = 60,
            Title = "Pending Ticket",
            Status = "Open",
            CreatedByUserId = employee.Id,
            FirstResponseDueAt = _baseTime.AddHours(5),
            FirstRespondedAt = null,
            ResolutionDueAt = _baseTime.AddHours(24),
            ResolvedAt = null
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        TestHelpers.SetUserContext(controller, admin.Id, "Admin");
        var result = Assert.IsType<OkObjectResult>(await controller.GetDashboard());
        var dashboard = Assert.IsType<TicketDashboardResponse>(result.Value);

        Assert.Equal(0, dashboard.SlaChecksMet);
        Assert.Equal(0, dashboard.SlaChecksBreached);
        // Safely handles 0 completed checks without NaN or division by zero
        Assert.Equal(0.0, dashboard.SlaCompliancePercentage);
    }
}
