using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ResolveIQ.Api.Controllers;
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Models;
using ResolveIQ.Api.Services;
using Xunit;

namespace ResolveIQ.Tests;

public class AuditHistoryTests
{
    [Fact]
    public async Task TicketLifecycle_GeneratesAuditEvents_WithoutDuplicates()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var slaService = new SlaService();
        var ticketService = new TicketService(db, slaService);

        // 1. Ticket Creation
        var createRequest = new CreateTicketRequest
        {
            Title = "Database slow",
            Description = "Queries taking 10s",
            Priority = "High"
        };
        var ticket = await ticketService.CreateAsync(createRequest, employee.Id);

        var createHistory = await db.TicketHistories
            .Where(h => h.TicketId == ticket.Id)
            .ToListAsync();
        Assert.Single(createHistory);
        Assert.Equal("Created", createHistory[0].Action);
        Assert.Equal(employee.Id, createHistory[0].UserId);
        Assert.Null(createHistory[0].OldValue);
        Assert.Contains("Status: Open; Priority: High", createHistory[0].NewValue);

        // 2. Assignment by Admin
        var ticketsController = new TicketsController(ticketService, db, slaService);
        TestHelpers.SetUserContext(ticketsController, admin.Id, "Admin", admin.FullName, admin.Email);

        var assignResult = await ticketsController.Assign(ticket.Id, agent.Id);
        Assert.IsType<OkObjectResult>(assignResult);

        var assignHistory = await db.TicketHistories
            .Where(h => h.TicketId == ticket.Id && h.Action == "Assigned")
            .ToListAsync();
        Assert.Single(assignHistory);
        Assert.Equal(admin.Id, assignHistory[0].UserId);
        Assert.Null(assignHistory[0].OldValue);
        Assert.Equal(agent.Id.ToString(), assignHistory[0].NewValue);

        // 3. Assign again to SAME agent -> Must NOT duplicate event
        var reassignResult = await ticketsController.Assign(ticket.Id, agent.Id);
        Assert.IsType<OkObjectResult>(reassignResult);

        var assignHistoryCount = await db.TicketHistories
            .CountAsync(h => h.TicketId == ticket.Id && h.Action == "Assigned");
        Assert.Equal(1, assignHistoryCount); // Still 1

        // 4. Update status & priority
        var updateRequest = new UpdateTicketRequest
        {
            Status = "InProgress",
            Priority = "Critical"
        };
        await ticketService.UpdateAsync(ticket.Id, updateRequest, agent.Id);

        var statusHistory = await db.TicketHistories
            .Where(h => h.TicketId == ticket.Id && h.Action == "StatusChanged")
            .ToListAsync();
        Assert.Single(statusHistory);
        Assert.Equal("Open", statusHistory[0].OldValue);
        Assert.Equal("InProgress", statusHistory[0].NewValue);

        var priorityHistory = await db.TicketHistories
            .Where(h => h.TicketId == ticket.Id && h.Action == "PriorityChanged")
            .ToListAsync();
        Assert.Single(priorityHistory);
        Assert.Equal("High", priorityHistory[0].OldValue);
        Assert.Equal("Critical", priorityHistory[0].NewValue);

        // 5. Update with NO changes -> Must NOT add redundant history
        var noChangeRequest = new UpdateTicketRequest
        {
            Status = "InProgress",
            Priority = "Critical"
        };
        await ticketService.UpdateAsync(ticket.Id, noChangeRequest, agent.Id);

        var totalEventsAfterNoChange = await db.TicketHistories
            .CountAsync(h => h.TicketId == ticket.Id);
        Assert.Equal(4, totalEventsAfterNoChange); // Created, Assigned, StatusChanged, PriorityChanged
    }

    [Fact]
    public async Task GetHistory_ReturnsChronologicalOrderAndSafeActorDetails()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var ticket = new Ticket
        {
            Id = 20,
            Title = "VPN not connecting",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id
        };
        db.Tickets.Add(ticket);

        var t0 = DateTime.UtcNow.AddHours(-3);
        var t1 = DateTime.UtcNow.AddHours(-2);
        var t2 = DateTime.UtcNow.AddHours(-1);

        db.TicketHistories.AddRange(
            new TicketHistory
            {
                TicketId = ticket.Id,
                UserId = employee.Id,
                Action = "Created",
                NewValue = "Status: Open",
                CreatedAt = t0
            },
            new TicketHistory
            {
                TicketId = ticket.Id,
                UserId = admin.Id,
                Action = "Assigned",
                OldValue = null,
                NewValue = agent.Id.ToString(),
                CreatedAt = t1
            },
            new TicketHistory
            {
                TicketId = ticket.Id,
                UserId = null, // System event
                Action = "FirstResponseBreached",
                CreatedAt = t2
            }
        );
        await db.SaveChangesAsync();

        var slaService = new SlaService();
        var ticketService = new TicketService(db, slaService);
        var controller = new TicketsController(ticketService, db, slaService);
        TestHelpers.SetUserContext(controller, employee.Id, "Employee", employee.FullName, employee.Email);

        var result = await controller.GetHistory(ticket.Id);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var items = Assert.IsAssignableFrom<List<TicketHistoryResponse>>(okResult.Value);

        Assert.Equal(3, items.Count);

        // Chronological order verification
        Assert.True(items[0].CreatedAt <= items[1].CreatedAt);
        Assert.True(items[1].CreatedAt <= items[2].CreatedAt);

        // Safe actor details verification
        Assert.Equal(employee.FullName, items[0].ActorName);
        Assert.Equal("Employee", items[0].ActorRole);

        Assert.Equal(admin.FullName, items[1].ActorName);
        Assert.Equal("Admin", items[1].ActorRole);

        Assert.Equal("System", items[2].ActorName);
        Assert.Equal("System", items[2].ActorRole);
    }

    [Fact]
    public async Task GetHistory_Authorization_EnforcesAccessPolicy()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var otherEmployee = new User
        {
            Id = 50,
            FullName = "Other Employee",
            Email = "other@test.com",
            Role = "Employee"
        };
        db.Users.Add(otherEmployee);

        var ticket = new Ticket
        {
            Id = 21,
            Title = "Confidential Ticket",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var slaService = new SlaService();
        var ticketService = new TicketService(db, slaService);
        var controller = new TicketsController(ticketService, db, slaService);

        // 1. Employee owner can view
        TestHelpers.SetUserContext(controller, employee.Id, "Employee");
        var employeeResult = await controller.GetHistory(ticket.Id);
        Assert.IsType<OkObjectResult>(employeeResult);

        // 2. Other employee cannot view (Forbid)
        TestHelpers.SetUserContext(controller, otherEmployee.Id, "Employee");
        var otherResult = await controller.GetHistory(ticket.Id);
        Assert.IsType<ForbidResult>(otherResult);

        // 3. Assigned agent can view
        TestHelpers.SetUserContext(controller, agent.Id, "Agent");
        var agentResult = await controller.GetHistory(ticket.Id);
        Assert.IsType<OkObjectResult>(agentResult);

        // 4. Admin can view
        TestHelpers.SetUserContext(controller, admin.Id, "Admin");
        var adminResult = await controller.GetHistory(ticket.Id);
        Assert.IsType<OkObjectResult>(adminResult);

        // 5. Nonexistent ticket returns 404
        var notFoundResult = await controller.GetHistory(9999);
        Assert.IsType<NotFoundObjectResult>(notFoundResult);
    }
}
