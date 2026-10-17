using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ResolveIQ.Api.Controllers;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Models;
using ResolveIQ.Api.Services;
using Xunit;

namespace ResolveIQ.Tests;

public class TicketNotificationEventsTests
{
    private static async Task<(AppDbContext db, User employee, User agent, User admin, INotificationService notifService, SlaService slaService)> CreateSetupAsync()
    {
        var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);
        var notifService = new NotificationService(db);
        var slaService = new SlaService();

        return (db, employee, agent, admin, notifService, slaService);
    }

    [Fact]
    public async Task TicketCreation_DispatchesNotificationToAdmins_ExcludesCreator()
    {
        var (db, employee, _, admin, notifService, slaService) = await CreateSetupAsync();
        var ticketService = new TicketService(db, slaService, notifService);

        var request = new CreateTicketRequest
        {
            Title = "VPN connection failed",
            Description = "Unable to connect to internal subnet",
            Priority = "High"
        };

        var ticket = await ticketService.CreateAsync(request, employee.Id);

        // Admin received notification
        var adminNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == admin.Id && n.TicketId == ticket.Id);
        Assert.NotNull(adminNotif);
        Assert.Equal("TicketCreated", adminNotif.Type);
        Assert.Contains(ticket.Title, adminNotif.Title);

        // Creator (Employee) did NOT receive a self-notification
        var employeeNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == employee.Id && n.TicketId == ticket.Id);
        Assert.Null(employeeNotif);
    }

    [Fact]
    public async Task TicketAssignment_NotifiesAssignedAgentAndCreator_ExcludesAdminActor()
    {
        var (db, employee, agent, admin, notifService, slaService) = await CreateSetupAsync();
        var ticketService = new TicketService(db, slaService, notifService);
        var controller = new TicketsController(ticketService, db, slaService, notifService);

        var ticket = new Ticket
        {
            Id = 50,
            Title = "Workstation Upgrade",
            CreatedByUserId = employee.Id,
            Status = "Open",
            Priority = "Medium"
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        TestHelpers.SetUserContext(controller, admin.Id, admin.Role);

        var assignResult = await controller.Assign(ticket.Id, agent.Id);
        Assert.IsType<OkObjectResult>(assignResult);

        // 1. Agent received assignment notification
        var agentNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == agent.Id && n.TicketId == ticket.Id && n.Type == "TicketAssigned");
        Assert.NotNull(agentNotif);
        Assert.Contains("Assigned to Ticket", agentNotif.Title);

        // 2. Creator (employee) received notification
        var creatorNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == employee.Id && n.TicketId == ticket.Id && n.Type == "TicketAssigned");
        Assert.NotNull(creatorNotif);

        // 3. Admin (actor) was not notified
        var adminNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == admin.Id && n.TicketId == ticket.Id && n.Type == "TicketAssigned");
        Assert.Null(adminNotif);
    }

    [Fact]
    public async Task TicketReassignment_NotifiesOldAgentAndNewAgent()
    {
        var (db, employee, agent1, admin, notifService, slaService) = await CreateSetupAsync();

        var agent2 = new User { Id = 20, FullName = "Agent Two", Email = "agent2@test.com", Role = "Agent" };
        db.Users.Add(agent2);

        var ticket = new Ticket
        {
            Id = 51,
            Title = "Printer Jam",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent1.Id,
            Status = "Open"
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var ticketService = new TicketService(db, slaService, notifService);
        var controller = new TicketsController(ticketService, db, slaService, notifService);
        TestHelpers.SetUserContext(controller, admin.Id, admin.Role);

        // Reassign from Agent 1 to Agent 2
        await controller.Assign(ticket.Id, agent2.Id);

        // Old agent notified of unassignment
        var oldAgentNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == agent1.Id && n.TicketId == ticket.Id);
        Assert.NotNull(oldAgentNotif);
        Assert.Contains("Unassigned", oldAgentNotif.Title);

        // New agent notified of assignment
        var newAgentNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == agent2.Id && n.TicketId == ticket.Id);
        Assert.NotNull(newAgentNotif);
        Assert.Contains("Assigned to Ticket", newAgentNotif.Title);
    }

    [Fact]
    public async Task TicketComment_AgentComments_NotifiesCreator_ExcludesAgent()
    {
        var (db, employee, agent, _, notifService, _) = await CreateSetupAsync();

        var ticket = new Ticket
        {
            Id = 52,
            Title = "Email Client Crashing",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            Status = "InProgress"
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var controller = new TicketCommentsController(db, notifService);
        TestHelpers.SetUserContext(controller, agent.Id, agent.Role, agent.FullName, agent.Email);

        var result = await controller.AddComment(ticket.Id, new CreateCommentRequest { Content = "Looking into crash logs now." });
        Assert.IsType<CreatedAtActionResult>(result);

        // Creator received notification
        var creatorNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == employee.Id && n.TicketId == ticket.Id);
        Assert.NotNull(creatorNotif);
        Assert.Equal("NewComment", creatorNotif.Type);
        Assert.Contains(agent.FullName, creatorNotif.Message);

        // Agent (actor) did not receive self-notification
        var agentNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == agent.Id && n.TicketId == ticket.Id);
        Assert.Null(agentNotif);
    }

    [Fact]
    public async Task TicketComment_CreatorComments_NotifiesAssignedAgent()
    {
        var (db, employee, agent, _, notifService, _) = await CreateSetupAsync();

        var ticket = new Ticket
        {
            Id = 53,
            Title = "Access Card Issue",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            Status = "InProgress"
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var controller = new TicketCommentsController(db, notifService);
        TestHelpers.SetUserContext(controller, employee.Id, employee.Role, employee.FullName, employee.Email);

        var result = await controller.AddComment(ticket.Id, new CreateCommentRequest { Content = "The badge reader is blinking red." });
        Assert.IsType<CreatedAtActionResult>(result);

        // Assigned agent received notification
        var agentNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == agent.Id && n.TicketId == ticket.Id);
        Assert.NotNull(agentNotif);
        Assert.Equal("NewComment", agentNotif.Type);

        // Creator did not receive notification
        var creatorNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == employee.Id && n.TicketId == ticket.Id);
        Assert.Null(creatorNotif);
    }

    [Fact]
    public async Task TicketStatusAndPriorityUpdate_NotifiesCreatorAndAssignedAgent()
    {
        var (db, employee, agent, admin, notifService, slaService) = await CreateSetupAsync();

        var ticket = new Ticket
        {
            Id = 54,
            Title = "Database Read Replica Slow",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            Status = "Open",
            Priority = "Medium"
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var ticketService = new TicketService(db, slaService, notifService);

        // Agent updates ticket status to Resolved and priority to High
        var updateRequest = new UpdateTicketRequest
        {
            Status = "Resolved",
            Priority = "High"
        };

        var updatedTicket = await ticketService.UpdateAsync(ticket.Id, updateRequest, agent.Id);
        Assert.NotNull(updatedTicket);

        // Creator received StatusChanged and PriorityChanged notifications
        var statusNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == employee.Id && n.TicketId == ticket.Id && n.Type == "StatusChanged");
        Assert.NotNull(statusNotif);
        Assert.Contains("Resolved", statusNotif.Message);

        var priorityNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == employee.Id && n.TicketId == ticket.Id && n.Type == "PriorityChanged");
        Assert.NotNull(priorityNotif);
        Assert.Contains("High", priorityNotif.Message);

        // Actor (Agent) excluded
        var agentStatusNotif = await db.Notifications.FirstOrDefaultAsync(n => n.UserId == agent.Id && n.TicketId == ticket.Id && n.Type == "StatusChanged");
        Assert.Null(agentStatusNotif);
    }
}
