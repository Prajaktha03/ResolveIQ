using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ResolveIQ.Api.Controllers;
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Models;
using Xunit;

namespace ResolveIQ.Tests;

public class FirstResponseTrackingTests
{
    [Fact]
    public async Task AddComment_WhenAgentPostsFirstComment_RecordsFirstRespondedAtAndHistory()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var ticket = new Ticket
        {
            Id = 10,
            Title = "Printer Issue",
            Description = "Printer not working",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            FirstRespondedAt = null,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30)
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var controller = new TicketCommentsController(db);
        TestHelpers.SetUserContext(controller, agent.Id, "Agent", agent.FullName, agent.Email);

        var request = new CreateCommentRequest { Content = "I am looking into this right now." };
        var result = await controller.AddComment(ticket.Id, request);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.NotNull(createdResult.Value);

        // Verify ticket FirstRespondedAt was set
        var updatedTicket = await db.Tickets.FindAsync(ticket.Id);
        Assert.NotNull(updatedTicket);
        Assert.NotNull(updatedTicket.FirstRespondedAt);

        // Verify TicketHistory was recorded
        var history = await db.TicketHistories
            .FirstOrDefaultAsync(h => h.TicketId == ticket.Id && h.Action == "FirstResponse");
        Assert.NotNull(history);
        Assert.Equal(agent.Id, history.UserId);
        Assert.Equal("FirstResponse", history.Action);
    }

    [Fact]
    public async Task AddComment_WhenAdminPostsFirstComment_RecordsFirstRespondedAtAndHistory()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var ticket = new Ticket
        {
            Id = 11,
            Title = "Server Down",
            Description = "Production server unreachable",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            FirstRespondedAt = null,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10)
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var controller = new TicketCommentsController(db);
        TestHelpers.SetUserContext(controller, admin.Id, "Admin", admin.FullName, admin.Email);

        var request = new CreateCommentRequest { Content = "Admin stepping in to help." };
        var result = await controller.AddComment(ticket.Id, request);

        Assert.IsType<CreatedAtActionResult>(result);

        var updatedTicket = await db.Tickets.FindAsync(ticket.Id);
        Assert.NotNull(updatedTicket);
        Assert.NotNull(updatedTicket.FirstRespondedAt);

        var history = await db.TicketHistories
            .FirstOrDefaultAsync(h => h.TicketId == ticket.Id && h.Action == "FirstResponse");
        Assert.NotNull(history);
        Assert.Equal(admin.Id, history.UserId);
    }

    [Fact]
    public async Task AddComment_WhenAgentPostsSubsequentComment_DoesNotOverwriteOriginalTimestamp()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var originalTimestamp = DateTime.UtcNow.AddHours(-1);
        var ticket = new Ticket
        {
            Id = 12,
            Title = "Network Issue",
            Description = "Slow wifi",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            FirstRespondedAt = originalTimestamp,
            CreatedAt = DateTime.UtcNow.AddHours(-2)
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var controller = new TicketCommentsController(db);
        TestHelpers.SetUserContext(controller, agent.Id, "Agent", agent.FullName, agent.Email);

        var request = new CreateCommentRequest { Content = "Second comment follow up." };
        var result = await controller.AddComment(ticket.Id, request);

        Assert.IsType<CreatedAtActionResult>(result);

        var updatedTicket = await db.Tickets.FindAsync(ticket.Id);
        Assert.NotNull(updatedTicket);
        Assert.Equal(originalTimestamp, updatedTicket.FirstRespondedAt);

        // Verify no second FirstResponse history event
        var historyCount = await db.TicketHistories
            .CountAsync(h => h.TicketId == ticket.Id && h.Action == "FirstResponse");
        Assert.Equal(0, historyCount);
    }

    [Fact]
    public async Task AddComment_WhenEmployeePostsComment_DoesNotCountAsFirstResponse()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var ticket = new Ticket
        {
            Id = 13,
            Title = "Keyboard broken",
            Description = "Keys stuck",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            FirstRespondedAt = null,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var controller = new TicketCommentsController(db);
        TestHelpers.SetUserContext(controller, employee.Id, "Employee", employee.FullName, employee.Email);

        var request = new CreateCommentRequest { Content = "Adding more info: it is the spacebar." };
        var result = await controller.AddComment(ticket.Id, request);

        Assert.IsType<CreatedAtActionResult>(result);

        var updatedTicket = await db.Tickets.FindAsync(ticket.Id);
        Assert.NotNull(updatedTicket);
        // Employee comments must not set FirstRespondedAt
        Assert.Null(updatedTicket.FirstRespondedAt);

        var historyCount = await db.TicketHistories
            .CountAsync(h => h.TicketId == ticket.Id && h.Action == "FirstResponse");
        Assert.Equal(0, historyCount);
    }

    [Fact]
    public async Task AddComment_UnauthorizedAgent_ReturnsForbid()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        // Another agent not assigned to this ticket
        var anotherAgent = new User
        {
            Id = 99,
            FullName = "Agent Other",
            Email = "otheragent@test.com",
            Role = "Agent"
        };
        db.Users.Add(anotherAgent);

        var ticket = new Ticket
        {
            Id = 14,
            Title = "Ticket assigned to agent 2",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var controller = new TicketCommentsController(db);
        TestHelpers.SetUserContext(controller, anotherAgent.Id, "Agent", anotherAgent.FullName, anotherAgent.Email);

        var request = new CreateCommentRequest { Content = "Attempting to comment." };
        var result = await controller.AddComment(ticket.Id, request);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task AddComment_NonexistentTicket_ReturnsNotFound()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var controller = new TicketCommentsController(db);
        TestHelpers.SetUserContext(controller, agent.Id, "Agent", agent.FullName, agent.Email);

        var request = new CreateCommentRequest { Content = "Commenting on nonexistent ticket." };
        var result = await controller.AddComment(999, request);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }

    [Fact]
    public async Task AddComment_EmptyContent_ReturnsBadRequest()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var ticket = new Ticket
        {
            Id = 15,
            Title = "Valid Ticket",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var controller = new TicketCommentsController(db);
        TestHelpers.SetUserContext(controller, agent.Id, "Agent", agent.FullName, agent.Email);

        var request = new CreateCommentRequest { Content = "   " };
        var result = await controller.AddComment(ticket.Id, request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    [Fact]
    public async Task AddComment_SequenceEmployeeThenAgentThenAdmin_CorrectlyRecordsFirstResponseOnce()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var ticket = new Ticket
        {
            Id = 16,
            Title = "Sequential Comments",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            FirstRespondedAt = null
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var controller = new TicketCommentsController(db);

        // Step 1: Employee comments -> FirstRespondedAt remains null
        TestHelpers.SetUserContext(controller, employee.Id, "Employee", employee.FullName, employee.Email);
        await controller.AddComment(ticket.Id, new CreateCommentRequest { Content = "Employee comment 1" });
        var ticketAfterEmp = await db.Tickets.FindAsync(ticket.Id);
        Assert.Null(ticketAfterEmp!.FirstRespondedAt);

        // Step 2: Agent comments -> FirstRespondedAt is set to UTC now
        TestHelpers.SetUserContext(controller, agent.Id, "Agent", agent.FullName, agent.Email);
        await controller.AddComment(ticket.Id, new CreateCommentRequest { Content = "Agent support response" });
        var ticketAfterAgent = await db.Tickets.FindAsync(ticket.Id);
        Assert.NotNull(ticketAfterAgent!.FirstRespondedAt);
        var originalFirstResponseTime = ticketAfterAgent.FirstRespondedAt;

        // Step 3: Admin comments later -> FirstRespondedAt must NOT be overwritten
        TestHelpers.SetUserContext(controller, admin.Id, "Admin", admin.FullName, admin.Email);
        await controller.AddComment(ticket.Id, new CreateCommentRequest { Content = "Admin follow-up" });
        var ticketAfterAdmin = await db.Tickets.FindAsync(ticket.Id);
        Assert.Equal(originalFirstResponseTime, ticketAfterAdmin!.FirstRespondedAt);

        // Step 4: Employee comments again -> FirstRespondedAt preserved
        TestHelpers.SetUserContext(controller, employee.Id, "Employee", employee.FullName, employee.Email);
        await controller.AddComment(ticket.Id, new CreateCommentRequest { Content = "Employee thank you" });
        var ticketAfterEmp2 = await db.Tickets.FindAsync(ticket.Id);
        Assert.Equal(originalFirstResponseTime, ticketAfterEmp2!.FirstRespondedAt);

        // Verify only exactly 1 FirstResponse history event was recorded
        var firstResponseEvents = await db.TicketHistories
            .Where(h => h.TicketId == ticket.Id && h.Action == "FirstResponse")
            .ToListAsync();
        Assert.Single(firstResponseEvents);
        Assert.Equal(agent.Id, firstResponseEvents[0].UserId);
    }
}
