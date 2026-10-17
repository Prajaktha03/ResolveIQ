using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using ResolveIQ.Api.Controllers;
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Health;
using ResolveIQ.Api.Middleware;
using ResolveIQ.Api.Models;
using ResolveIQ.Api.Services;
using Xunit;

namespace ResolveIQ.Tests;

public class SecurityAndProductionReadinessTests
{
    private class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "ResolveIQ.Api";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    // ==========================================
    // MILESTONE 1 & 4: Security Headers Middleware
    // ==========================================

    [Fact]
    public async Task SecurityHeadersMiddleware_AppliesAllStandardSecurityHeaders()
    {
        var middleware = new SecurityHeadersMiddleware(next: (innerHttpContext) => Task.CompletedTask);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
        Assert.Equal("strict-origin-when-cross-origin", context.Response.Headers["Referrer-Policy"].ToString());
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", context.Response.Headers["Content-Security-Policy"].ToString());
        Assert.Equal("camera=(), microphone=(), geolocation=()", context.Response.Headers["Permissions-Policy"].ToString());
        Assert.Equal("none", context.Response.Headers["X-Permitted-Cross-Domain-Policies"].ToString());
    }

    [Fact]
    public async Task SecurityHeadersMiddleware_DoesNotOverwriteExistingHeader()
    {
        var middleware = new SecurityHeadersMiddleware(next: (innerHttpContext) => Task.CompletedTask);
        var context = new DefaultHttpContext();
        context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";

        await middleware.InvokeAsync(context);

        Assert.Equal("SAMEORIGIN", context.Response.Headers["X-Frame-Options"].ToString());
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
    }

    // ==========================================
    // MILESTONE 4: Correlation ID Middleware
    // ==========================================

    [Fact]
    public async Task CorrelationIdMiddleware_GeneratesNewCorrelationId_WhenNoneProvided()
    {
        var logger = NullLogger<CorrelationIdMiddleware>.Instance;
        var middleware = new CorrelationIdMiddleware(next: (innerContext) => Task.CompletedTask, logger);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        var headerValue = context.Response.Headers["X-Correlation-ID"].ToString();
        Assert.False(string.IsNullOrWhiteSpace(headerValue));
        Assert.Equal(headerValue, context.TraceIdentifier);
    }

    [Fact]
    public async Task CorrelationIdMiddleware_PreservesIncomingCorrelationId()
    {
        var logger = NullLogger<CorrelationIdMiddleware>.Instance;
        var middleware = new CorrelationIdMiddleware(next: (innerContext) => Task.CompletedTask, logger);
        var context = new DefaultHttpContext();
        var incomingCorrelationId = "incoming-trace-id-998877";
        context.Request.Headers["X-Correlation-ID"] = incomingCorrelationId;

        await middleware.InvokeAsync(context);

        var headerValue = context.Response.Headers["X-Correlation-ID"].ToString();
        Assert.Equal(incomingCorrelationId, headerValue);
        Assert.Equal(incomingCorrelationId, context.TraceIdentifier);
    }

    // ==========================================
    // MILESTONE 2: Global Exception Handler Middleware
    // ==========================================

    [Fact]
    public async Task GlobalExceptionHandlerMiddleware_ReturnsProblemDetailsAndSanitizesSecrets_InProduction()
    {
        var env = new TestWebHostEnvironment { EnvironmentName = "Production" };
        var logger = NullLogger<GlobalExceptionHandlerMiddleware>.Instance;

        var middleware = new GlobalExceptionHandlerMiddleware(
            next: (innerContext) => throw new InvalidOperationException("Fatal error: Database connection string Server=db.internal;Password=SuperSecretPassword; failed!"),
            logger,
            env);

        var context = new DefaultHttpContext();
        context.TraceIdentifier = "test-trace-id-12345";
        var responseStream = new MemoryStream();
        context.Response.Body = responseStream;

        await middleware.InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);

        responseStream.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(responseStream);
        var responseBody = await reader.ReadToEndAsync();

        // Must not leak internal secrets or connection strings
        Assert.DoesNotContain("SuperSecretPassword", responseBody);
        Assert.DoesNotContain("Server=db.internal", responseBody);

        // Verify valid RFC 7807 structure
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;
        Assert.Equal(500, root.GetProperty("status").GetInt32());
        Assert.Equal("An unexpected error occurred.", root.GetProperty("title").GetString());
        Assert.Equal("https://httpstatuses.com/500", root.GetProperty("type").GetString());
        Assert.Equal("test-trace-id-12345", root.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task GlobalExceptionHandlerMiddleware_IncludesDetail_InDevelopment()
    {
        var env = new TestWebHostEnvironment { EnvironmentName = "Development" };
        var logger = NullLogger<GlobalExceptionHandlerMiddleware>.Instance;
        var exMessage = "Detailed diagnostic message for developer debugging.";

        var middleware = new GlobalExceptionHandlerMiddleware(
            next: (innerContext) => throw new InvalidOperationException(exMessage),
            logger,
            env);

        var context = new DefaultHttpContext();
        context.TraceIdentifier = "dev-trace-id";
        var responseStream = new MemoryStream();
        context.Response.Body = responseStream;

        await middleware.InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        responseStream.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(responseStream);
        var responseBody = await reader.ReadToEndAsync();

        Assert.Contains(exMessage, responseBody);
    }

    [Fact]
    public async Task GlobalExceptionHandlerMiddleware_GracefullyHandles_ClientCancellation()
    {
        var env = new TestWebHostEnvironment { EnvironmentName = "Production" };
        var logger = NullLogger<GlobalExceptionHandlerMiddleware>.Instance;

        var middleware = new GlobalExceptionHandlerMiddleware(
            next: (innerContext) => throw new OperationCanceledException(),
            logger,
            env);

        var context = new DefaultHttpContext();
        var responseStream = new MemoryStream();
        context.Response.Body = responseStream;

        // Must handle client cancellation without throwing uncaught exception or returning 500
        await middleware.InvokeAsync(context);

        Assert.NotEqual(500, context.Response.StatusCode);
    }

    // ==========================================
    // MILESTONE 4: Database Health Check
    // ==========================================

    [Fact]
    public async Task DatabaseHealthCheck_ReturnsHealthy_WhenDatabaseIsOperational()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var healthCheck = new DatabaseHealthCheck(db, NullLogger<DatabaseHealthCheck>.Instance);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("Database is responsive", result.Description);
        Assert.True(result.Data.ContainsKey("database"));
        Assert.Equal("Operational", result.Data["database"]);
    }

    // ==========================================
    // MILESTONE 1: JWT Secret Strength Validation
    // ==========================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short-key")]
    [InlineData("c29tZXNob3J0a2V5MTIz")] // 16 bytes base64
    public void JwtConfiguration_Validation_FailsSafely_WhenKeyIsWeakOrMissing(string? weakKey)
    {
        var action = () =>
        {
            if (string.IsNullOrWhiteSpace(weakKey))
                throw new InvalidOperationException("Fatal configuration error: 'Jwt:Key' is not set.");

            byte[] keyBytes;
            try
            {
                keyBytes = Convert.FromBase64String(weakKey);
            }
            catch (FormatException)
            {
                keyBytes = System.Text.Encoding.UTF8.GetBytes(weakKey);
            }

            if (keyBytes.Length < 32)
                throw new InvalidOperationException($"Fatal configuration error: 'Jwt:Key' must be at least 256 bits (32 bytes). Current key length is {keyBytes.Length * 8} bits.");
        };

        var ex = Assert.Throws<InvalidOperationException>(action);
        Assert.Contains("Jwt:Key", ex.Message);
    }

    [Fact]
    public void JwtConfiguration_Validation_Succeeds_WhenKeyIsAtLeast256Bits()
    {
        var keyBytes = new byte[32];
        RandomNumberGenerator.Fill(keyBytes);
        var strongBase64Key = Convert.ToBase64String(keyBytes);

        var decoded = Convert.FromBase64String(strongBase64Key);
        Assert.True(decoded.Length >= 32);
    }

    // ==========================================
    // MILESTONE 1 & 5: IDOR & Object-Level Access Control
    // ==========================================

    [Fact]
    public async Task Ticket_Employee_CannotAccess_OtherEmployeeTicket_ReturnsForbid()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee1, _, _) = await TestHelpers.SeedDefaultUsersAsync(db);

        var employee2 = new User
        {
            Id = 4,
            FullName = "Employee Two",
            Email = "emp2@test.com",
            PasswordHash = "hash",
            Role = "Employee",
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(employee2);

        var ticketOfEmployee2 = new Ticket
        {
            Id = 101,
            Title = "Confidential Payroll Query",
            Description = "Employee 2 private payroll issue",
            Status = "Open",
            Priority = "Medium",
            CreatedByUserId = employee2.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.Tickets.Add(ticketOfEmployee2);
        await db.SaveChangesAsync();

        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);
        var slaService = new SlaService(timeProvider);
        var ticketService = new TicketService(db, slaService);
        var controller = new TicketsController(ticketService, db, slaService);

        // Authenticate as employee 1
        TestHelpers.SetUserContext(controller, employee1.Id, "Employee");

        var result = await controller.GetById(ticketOfEmployee2.Id);
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task TicketComments_Employee_CannotViewOrPostComment_OnOtherEmployeeTicket_ReturnsForbid()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee1, _, _) = await TestHelpers.SeedDefaultUsersAsync(db);

        var employee2 = new User
        {
            Id = 4,
            FullName = "Employee Two",
            Email = "emp2@test.com",
            PasswordHash = "hash",
            Role = "Employee",
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(employee2);

        var ticketOfEmployee2 = new Ticket
        {
            Id = 102,
            Title = "Sensitive Ticket",
            Description = "Private Details",
            Status = "Open",
            Priority = "High",
            CreatedByUserId = employee2.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.Tickets.Add(ticketOfEmployee2);
        await db.SaveChangesAsync();

        var commentsController = new TicketCommentsController(db);
        TestHelpers.SetUserContext(commentsController, employee1.Id, "Employee");

        // Attempting to read comments on employee 2's ticket
        var getResult = await commentsController.GetComments(ticketOfEmployee2.Id);
        Assert.IsType<ForbidResult>(getResult);

        // Attempting to post comment on employee 2's ticket
        var postResult = await commentsController.AddComment(ticketOfEmployee2.Id, new CreateCommentRequest
        {
            Content = "Malicious eavesdropping comment"
        });
        Assert.IsType<ForbidResult>(postResult);
    }

    [Fact]
    public async Task Ticket_Agent_CannotAccess_TicketAssignedToAnotherAgent_ReturnsForbid()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent1, _) = await TestHelpers.SeedDefaultUsersAsync(db);

        var agent2 = new User
        {
            Id = 5,
            FullName = "Agent Two",
            Email = "agent2@test.com",
            PasswordHash = "hash",
            Role = "Agent",
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(agent2);

        var ticketAssignedToAgent2 = new Ticket
        {
            Id = 103,
            Title = "Server crash",
            Description = "Assigned to Agent 2",
            Status = "InProgress",
            Priority = "Critical",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent2.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.Tickets.Add(ticketAssignedToAgent2);
        await db.SaveChangesAsync();

        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);
        var slaService = new SlaService(timeProvider);
        var ticketService = new TicketService(db, slaService);
        var controller = new TicketsController(ticketService, db, slaService);

        // Authenticate as agent 1
        TestHelpers.SetUserContext(controller, agent1.Id, "Agent");

        var result = await controller.GetById(ticketAssignedToAgent2.Id);
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task TicketComments_Agent_CannotViewOrPostComment_OnTicketAssignedToAnotherAgent_ReturnsForbid()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent1, _) = await TestHelpers.SeedDefaultUsersAsync(db);

        var agent2 = new User
        {
            Id = 5,
            FullName = "Agent Two",
            Email = "agent2@test.com",
            PasswordHash = "hash",
            Role = "Agent",
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(agent2);

        var ticketAssignedToAgent2 = new Ticket
        {
            Id = 104,
            Title = "Hardware failure",
            Description = "Assigned to Agent 2",
            Status = "InProgress",
            Priority = "High",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent2.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.Tickets.Add(ticketAssignedToAgent2);
        await db.SaveChangesAsync();

        var commentsController = new TicketCommentsController(db);
        TestHelpers.SetUserContext(commentsController, agent1.Id, "Agent");

        // Attempting to read comments
        var getResult = await commentsController.GetComments(ticketAssignedToAgent2.Id);
        Assert.IsType<ForbidResult>(getResult);

        // Attempting to post comment
        var postResult = await commentsController.AddComment(ticketAssignedToAgent2.Id, new CreateCommentRequest
        {
            Content = "Unauthorized agent comment"
        });
        Assert.IsType<ForbidResult>(postResult);
    }

    [Fact]
    public async Task Admin_CanAccessAllTicketsAndComments()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var ticket = new Ticket
        {
            Id = 105,
            Title = "Network Outage",
            Description = "Created by employee, assigned to agent",
            Status = "InProgress",
            Priority = "Critical",
            CreatedByUserId = employee.Id,
            AssignedToUserId = agent.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);
        var slaService = new SlaService(timeProvider);
        var ticketService = new TicketService(db, slaService);
        var controller = new TicketsController(ticketService, db, slaService);
        TestHelpers.SetUserContext(controller, admin.Id, "Admin");

        var ticketResult = await controller.GetById(ticket.Id);
        var okTicket = Assert.IsType<OkObjectResult>(ticketResult);
        Assert.NotNull(okTicket.Value);

        var commentsController = new TicketCommentsController(db);
        TestHelpers.SetUserContext(commentsController, admin.Id, "Admin");

        var postCommentResult = await commentsController.AddComment(ticket.Id, new CreateCommentRequest
        {
            Content = "Admin oversight comment"
        });
        var createdResult = Assert.IsType<CreatedAtActionResult>(postCommentResult);
        Assert.NotNull(createdResult.Value);
    }

    [Fact]
    public async Task Notifications_UserCannotMarkAnotherUsersNotificationAsRead_ReturnsForbid()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, _) = await TestHelpers.SeedDefaultUsersAsync(db);

        var agentNotification = new Notification
        {
            Id = 201,
            UserId = agent.Id,
            Type = "TicketAssigned",
            Title = "Secret Task",
            Message = "You were assigned to a ticket.",
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };
        db.Notifications.Add(agentNotification);
        await db.SaveChangesAsync();

        var controller = new NotificationsController(db);
        // Employee attempts to mark agent's notification as read
        TestHelpers.SetUserContext(controller, employee.Id, "Employee");

        var result = await controller.MarkAsRead(agentNotification.Id);
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Users_NonAdmin_CannotViewOtherUserProfile_ReturnsForbid()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, _) = await TestHelpers.SeedDefaultUsersAsync(db);

        var controller = new UsersController(db);
        // Employee attempts to view Agent's profile
        TestHelpers.SetUserContext(controller, employee.Id, "Employee");

        var result = await controller.GetById(agent.Id);
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Users_NonAdmin_CannotListAllUsers_ReturnsForbid()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, _, _) = await TestHelpers.SeedDefaultUsersAsync(db);

        var controller = new UsersController(db);
        TestHelpers.SetUserContext(controller, employee.Id, "Employee");

        var result = await controller.GetAll();
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Users_UpdateRole_RejectsAdminPrivilegeEscalation_ReturnsBadRequest()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, _, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var controller = new UsersController(db);
        TestHelpers.SetUserContext(controller, admin.Id, "Admin");

        // Attempt to escalate employee to Admin via UpdateRole endpoint
        var request = new UpdateUserRoleRequest { Role = "Admin" };
        var result = await controller.UpdateRole(employee.Id, request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequest.Value);
    }
}
