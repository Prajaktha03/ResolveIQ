using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using ResolveIQ.Api.Controllers;
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Models;
using ResolveIQ.Api.Services;
using Xunit;

namespace ResolveIQ.Tests;

public class AuthAndAccessWorkflowRegressionTests
{
    private static IConfiguration CreateTestConfiguration()
    {
        // 256-bit symmetric key for testing
        var keyBytes = new byte[32];
        RandomNumberGenerator.Fill(keyBytes);
        var base64Key = Convert.ToBase64String(keyBytes);

        var myConfiguration = new Dictionary<string, string?>
        {
            {"Jwt:Key", base64Key},
            {"Jwt:Issuer", "ResolveIQ.Tests"},
            {"Jwt:Audience", "ResolveIQ.Tests.Clients"}
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(myConfiguration)
            .Build();
    }

    [Fact]
    public async Task Register_ValidEmployee_SucceedsAndReturnsAuthResponse()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var config = CreateTestConfiguration();
        var controller = new AuthController(db, config);

        var request = new RegisterRequest
        {
            FullName = "New User",
            Email = "newuser@test.com",
            Password = "SecurePassword123!"
        };

        var result = await controller.Register(request);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var authResponse = Assert.IsType<AuthResponse>(okResult.Value);

        Assert.NotNull(authResponse.Token);
        Assert.Equal("Employee", authResponse.Role);
        Assert.Equal("newuser@test.com", authResponse.Email);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsConflict()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var config = CreateTestConfiguration();
        var controller = new AuthController(db, config);

        var request = new RegisterRequest
        {
            FullName = "First User",
            Email = "duplicate@test.com",
            Password = "Password123!"
        };

        await controller.Register(request);

        // Try registering again with same email
        var duplicateResult = await controller.Register(request);
        Assert.IsType<ConflictObjectResult>(duplicateResult);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var config = CreateTestConfiguration();
        var controller = new AuthController(db, config);

        await controller.Register(new RegisterRequest
        {
            FullName = "Login User",
            Email = "login@test.com",
            Password = "MyPassword123!"
        });

        var loginResult = controller.Login(new LoginRequest
        {
            Email = "login@test.com",
            Password = "MyPassword123!"
        });

        var okResult = Assert.IsType<OkObjectResult>(loginResult);
        var authResponse = Assert.IsType<AuthResponse>(okResult.Value);
        Assert.NotNull(authResponse.Token);
        Assert.Equal("login@test.com", authResponse.Email);
    }

    [Fact]
    public async Task Login_InvalidPassword_ReturnsUnauthorized()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var config = CreateTestConfiguration();
        var controller = new AuthController(db, config);

        await controller.Register(new RegisterRequest
        {
            FullName = "Login User",
            Email = "login2@test.com",
            Password = "CorrectPassword123!"
        });

        var loginResult = controller.Login(new LoginRequest
        {
            Email = "login2@test.com",
            Password = "WrongPassword999!"
        });

        Assert.IsType<UnauthorizedObjectResult>(loginResult);
    }

    [Fact]
    public async Task UsersController_UpdateRole_PromotesAndDemotesProperly()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var controller = new UsersController(db);
        TestHelpers.SetUserContext(controller, admin.Id, "Admin");

        // 1. Promote Employee to Agent
        var promoteResult = await controller.UpdateRole(employee.Id, new UpdateUserRoleRequest { Role = "Agent" });
        Assert.IsType<OkObjectResult>(promoteResult);
        var promotedUser = await db.Users.FindAsync(employee.Id);
        Assert.Equal("Agent", promotedUser!.Role);

        // 2. Demote Agent to Employee
        var demoteResult = await controller.UpdateRole(agent.Id, new UpdateUserRoleRequest { Role = "Employee" });
        Assert.IsType<OkObjectResult>(demoteResult);
        var demotedUser = await db.Users.FindAsync(agent.Id);
        Assert.Equal("Employee", demotedUser!.Role);

        // 3. Attempt to promote to Admin -> Must fail
        var adminAttemptResult = await controller.UpdateRole(employee.Id, new UpdateUserRoleRequest { Role = "Admin" });
        Assert.IsType<BadRequestObjectResult>(adminAttemptResult);

        // 4. Attempt to change existing Admin -> Must fail
        var changeAdminResult = await controller.UpdateRole(admin.Id, new UpdateUserRoleRequest { Role = "Agent" });
        Assert.IsType<BadRequestObjectResult>(changeAdminResult);
    }

    [Fact]
    public async Task TicketsController_GetAll_ConsistentAccessPolicy()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var employee2 = new User { Id = 101, FullName = "Emp 2", Email = "emp2@test.com", Role = "Employee" };
        var agent2 = new User { Id = 102, FullName = "Agent 2", Email = "agent2@test.com", Role = "Agent" };
        db.Users.AddRange(employee2, agent2);

        // Seed 3 tickets
        db.Tickets.AddRange(
            new Ticket { Id = 70, Title = "T1", CreatedByUserId = employee.Id, AssignedToUserId = agent.Id },
            new Ticket { Id = 71, Title = "T2", CreatedByUserId = employee.Id, AssignedToUserId = agent2.Id },
            new Ticket { Id = 72, Title = "T3", CreatedByUserId = employee2.Id, AssignedToUserId = agent.Id }
        );
        await db.SaveChangesAsync();

        var slaService = new SlaService();
        var ticketService = new TicketService(db, slaService);
        var controller = new TicketsController(ticketService, db, slaService);

        // 1. Employee sees only own tickets (T1, T2) -> 2 tickets
        TestHelpers.SetUserContext(controller, employee.Id, "Employee");
        var empResult = Assert.IsType<OkObjectResult>(await controller.GetAll());
        var empPaged = Assert.IsType<PagedResult<Ticket>>(empResult.Value);
        Assert.Equal(2, empPaged.TotalCount);
        Assert.Equal(2, empPaged.Items.Count);
        Assert.All(empPaged.Items, t => Assert.Equal(employee.Id, t.CreatedByUserId));

        // 2. Agent sees only assigned tickets (T1, T3) -> 2 tickets
        TestHelpers.SetUserContext(controller, agent.Id, "Agent");
        var agentResult = Assert.IsType<OkObjectResult>(await controller.GetAll());
        var agentPaged = Assert.IsType<PagedResult<Ticket>>(agentResult.Value);
        Assert.Equal(2, agentPaged.TotalCount);
        Assert.Equal(2, agentPaged.Items.Count);
        Assert.All(agentPaged.Items, t => Assert.Equal(agent.Id, t.AssignedToUserId));

        // 3. Admin sees all tickets (T1, T2, T3) -> 3 tickets
        TestHelpers.SetUserContext(controller, admin.Id, "Admin");
        var adminResult = Assert.IsType<OkObjectResult>(await controller.GetAll());
        var adminPaged = Assert.IsType<PagedResult<Ticket>>(adminResult.Value);
        Assert.Equal(3, adminPaged.TotalCount);
        Assert.Equal(3, adminPaged.Items.Count);
    }

    [Fact]
    public async Task TicketsController_GetByIdAndSla_EnforcesConsistentAccessPolicy()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var otherEmployee = new User { Id = 201, FullName = "Other Emp", Email = "otheremp@test.com", Role = "Employee" };
        var otherAgent = new User { Id = 202, FullName = "Other Agent", Email = "otheragent@test.com", Role = "Agent" };
        db.Users.AddRange(otherEmployee, otherAgent);

        var ticket = new Ticket
        {
            Id = 80,
            Title = "Secured Ticket",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            FirstResponseDueAt = DateTime.UtcNow.AddHours(1),
            ResolutionDueAt = DateTime.UtcNow.AddHours(8)
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var slaService = new SlaService();
        var ticketService = new TicketService(db, slaService);
        var controller = new TicketsController(ticketService, db, slaService);

        // Employee owner can access details and SLA
        TestHelpers.SetUserContext(controller, employee.Id, "Employee");
        Assert.IsType<OkObjectResult>(await controller.GetById(ticket.Id));
        Assert.IsType<OkObjectResult>(await controller.GetSlaStatus(ticket.Id));

        // Other employee is forbidden
        TestHelpers.SetUserContext(controller, otherEmployee.Id, "Employee");
        Assert.IsType<ForbidResult>(await controller.GetById(ticket.Id));
        Assert.IsType<ForbidResult>(await controller.GetSlaStatus(ticket.Id));

        // Assigned agent can access
        TestHelpers.SetUserContext(controller, agent.Id, "Agent");
        Assert.IsType<OkObjectResult>(await controller.GetById(ticket.Id));
        Assert.IsType<OkObjectResult>(await controller.GetSlaStatus(ticket.Id));

        // Other agent is forbidden
        TestHelpers.SetUserContext(controller, otherAgent.Id, "Agent");
        Assert.IsType<ForbidResult>(await controller.GetById(ticket.Id));
        Assert.IsType<ForbidResult>(await controller.GetSlaStatus(ticket.Id));

        // Admin can access
        TestHelpers.SetUserContext(controller, admin.Id, "Admin");
        Assert.IsType<OkObjectResult>(await controller.GetById(ticket.Id));
        Assert.IsType<OkObjectResult>(await controller.GetSlaStatus(ticket.Id));

        // Nonexistent ticket returns NotFound
        Assert.IsType<NotFoundResult>(await controller.GetById(99999));
        Assert.IsType<NotFoundResult>(await controller.GetSlaStatus(99999));
    }
}
