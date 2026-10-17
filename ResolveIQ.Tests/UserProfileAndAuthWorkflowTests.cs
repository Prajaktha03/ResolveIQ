using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ResolveIQ.Api.Controllers;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Models;
using Xunit;

namespace ResolveIQ.Tests;

public class UserProfileAndAuthWorkflowTests
{
    private static IConfiguration CreateTestConfiguration()
    {
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
    public async Task GetMe_AuthenticatedUser_ReturnsOwnProfileWithoutSensitiveData()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, _, _) = await TestHelpers.SeedDefaultUsersAsync(db);

        var controller = new UsersController(db);
        TestHelpers.SetUserContext(controller, employee.Id, employee.Role, employee.FullName, employee.Email);

        var result = await controller.GetCurrentUserProfile();
        var okResult = Assert.IsType<OkObjectResult>(result);
        var profile = Assert.IsType<UserProfileResponse>(okResult.Value);

        Assert.Equal(employee.Id, profile.Id);
        Assert.Equal(employee.FullName, profile.FullName);
        Assert.Equal(employee.Email, profile.Email);
        Assert.Equal(employee.Role, profile.Role);
        Assert.Equal(employee.CreatedAt, profile.CreatedAt);
    }

    [Fact]
    public async Task UpdateMe_ValidRequest_UpdatesFullNameAndEmail()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, _, _) = await TestHelpers.SeedDefaultUsersAsync(db);

        var controller = new UsersController(db);
        TestHelpers.SetUserContext(controller, employee.Id, employee.Role, employee.FullName, employee.Email);

        var updateRequest = new UpdateProfileRequest
        {
            FullName = "Updated Name",
            Email = "updated.email@test.com"
        };

        var result = await controller.UpdateCurrentUserProfile(updateRequest);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var profile = Assert.IsType<UserProfileResponse>(okResult.Value);

        Assert.Equal("Updated Name", profile.FullName);
        Assert.Equal("updated.email@test.com", profile.Email);

        var userInDb = await db.Users.FindAsync(employee.Id);
        Assert.Equal("Updated Name", userInDb!.FullName);
        Assert.Equal("updated.email@test.com", userInDb.Email);
        Assert.Equal("Employee", userInDb.Role); // Role immutable via profile update
    }

    [Fact]
    public async Task UpdateMe_DuplicateEmail_ReturnsConflict()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, _) = await TestHelpers.SeedDefaultUsersAsync(db);

        var controller = new UsersController(db);
        TestHelpers.SetUserContext(controller, employee.Id, employee.Role, employee.FullName, employee.Email);

        // Attempt to take agent's email
        var updateRequest = new UpdateProfileRequest
        {
            FullName = "New Name",
            Email = agent.Email
        };

        var result = await controller.UpdateCurrentUserProfile(updateRequest);
        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("already exists", conflictResult.Value?.ToString());
    }

    [Fact]
    public async Task GetUserById_SelfAndAdminAllowed_OtherUserForbidden()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var controller = new UsersController(db);

        // 1. Employee accessing self -> OK
        TestHelpers.SetUserContext(controller, employee.Id, "Employee");
        var selfResult = await controller.GetById(employee.Id);
        Assert.IsType<OkObjectResult>(selfResult);

        // 2. Admin accessing employee -> OK
        TestHelpers.SetUserContext(controller, admin.Id, "Admin");
        var adminResult = await controller.GetById(employee.Id);
        Assert.IsType<OkObjectResult>(adminResult);

        // 3. Employee accessing agent -> Forbidden
        TestHelpers.SetUserContext(controller, employee.Id, "Employee");
        var forbiddenResult = await controller.GetById(agent.Id);
        Assert.IsType<ForbidResult>(forbiddenResult);
    }

    [Fact]
    public async Task GetAllUsers_OnlyAdminAllowed_OthersForbidden()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var controller = new UsersController(db);

        // 1. Admin -> OK
        TestHelpers.SetUserContext(controller, admin.Id, "Admin");
        var adminResult = await controller.GetAll();
        Assert.IsType<OkObjectResult>(adminResult);

        // 2. Agent -> Forbidden
        TestHelpers.SetUserContext(controller, agent.Id, "Agent");
        var agentResult = await controller.GetAll();
        Assert.IsType<ForbidResult>(agentResult);

        // 3. Employee -> Forbidden
        TestHelpers.SetUserContext(controller, employee.Id, "Employee");
        var empResult = await controller.GetAll();
        Assert.IsType<ForbidResult>(empResult);
    }

    [Fact]
    public async Task ChangePassword_ValidCurrentPassword_UpdatesPasswordSuccessfully()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var config = CreateTestConfiguration();
        var emailService = new TestEmailService();
        var hasher = new PasswordHasher<User>();

        var user = new User
        {
            Id = 50,
            FullName = "Password Tester",
            Email = "tester@test.com",
            Role = "Employee",
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = hasher.HashPassword(user, "OldPassword123!");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var controller = new AuthController(db, config, emailService);
        TestHelpers.SetUserContext(controller, user.Id, user.Role);

        var changeRequest = new ChangePasswordRequest
        {
            CurrentPassword = "OldPassword123!",
            NewPassword = "NewSecurePassword456!",
            ConfirmNewPassword = "NewSecurePassword456!"
        };

        var result = await controller.ChangePassword(changeRequest);
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("Password changed successfully", okResult.Value?.ToString());

        // Verify user can now log in with new password
        var loginSuccess = controller.Login(new LoginRequest
        {
            Email = "tester@test.com",
            Password = "NewSecurePassword456!"
        });
        Assert.IsType<OkObjectResult>(loginSuccess);

        // Old password must fail
        var oldLoginFail = controller.Login(new LoginRequest
        {
            Email = "tester@test.com",
            Password = "OldPassword123!"
        });
        Assert.IsType<UnauthorizedObjectResult>(oldLoginFail);
    }

    [Fact]
    public async Task ChangePassword_IncorrectCurrentPassword_FailsWithBadRequest()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var config = CreateTestConfiguration();
        var emailService = new TestEmailService();
        var hasher = new PasswordHasher<User>();

        var user = new User
        {
            Id = 51,
            FullName = "Password Tester 2",
            Email = "tester2@test.com",
            Role = "Employee",
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = hasher.HashPassword(user, "RealPassword123!");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var controller = new AuthController(db, config, emailService);
        TestHelpers.SetUserContext(controller, user.Id, user.Role);

        var changeRequest = new ChangePasswordRequest
        {
            CurrentPassword = "WrongPassword999!",
            NewPassword = "NewSecurePassword456!",
            ConfirmNewPassword = "NewSecurePassword456!"
        };

        var result = await controller.ChangePassword(changeRequest);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Current password is incorrect", badRequest.Value?.ToString());

        // Original password remains active
        var loginCheck = controller.Login(new LoginRequest
        {
            Email = "tester2@test.com",
            Password = "RealPassword123!"
        });
        Assert.IsType<OkObjectResult>(loginCheck);
    }

    [Fact]
    public async Task ChangePassword_PasswordMismatch_FailsWithBadRequest()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var config = CreateTestConfiguration();
        var emailService = new TestEmailService();

        var hasher = new PasswordHasher<User>();
        var user = new User
        {
            Id = 52,
            FullName = "Password Tester 3",
            Email = "tester3@test.com",
            Role = "Employee",
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = hasher.HashPassword(user, "RealPassword123!");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var controller = new AuthController(db, config, emailService);
        TestHelpers.SetUserContext(controller, user.Id, user.Role);

        var changeRequest = new ChangePasswordRequest
        {
            CurrentPassword = "RealPassword123!",
            NewPassword = "NewSecurePassword456!",
            ConfirmNewPassword = "DifferentPassword789!"
        };

        var result = await controller.ChangePassword(changeRequest);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("do not match", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task ForgotPassword_KnownUser_DispatchesTokenAndNeverExposesTokenInResponse()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var config = CreateTestConfiguration();
        var emailService = new TestEmailService();

        var user = new User
        {
            Id = 60,
            FullName = "Reset User",
            Email = "reset.me@test.com",
            Role = "Employee",
            PasswordHash = "hashed_pw",
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var controller = new AuthController(db, config, emailService);

        var result = await controller.ForgotPassword(new ForgotPasswordRequest { Email = "reset.me@test.com" });
        var okResult = Assert.IsType<OkObjectResult>(result);

        // Response must NOT contain token or sensitive details
        var jsonResponse = okResult.Value?.ToString() ?? "";
        Assert.DoesNotContain("Token", jsonResponse, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("instructions have been sent", jsonResponse);

        // Email service must receive exactly 1 email with raw token
        Assert.Single(emailService.DispatchedEmails);
        var (dispatchedEmail, dispatchedToken) = emailService.DispatchedEmails[0];
        Assert.Equal("reset.me@test.com", dispatchedEmail);
        Assert.False(string.IsNullOrWhiteSpace(dispatchedToken));

        // In database, token must be stored as SHA-256 hash (64 hex characters), NOT plaintext
        var tokenInDb = await db.PasswordResetTokens.FirstOrDefaultAsync(t => t.UserId == user.Id);
        Assert.NotNull(tokenInDb);
        Assert.False(tokenInDb.IsUsed);
        Assert.NotEqual(dispatchedToken, tokenInDb.TokenHash);
        Assert.Equal(64, tokenInDb.TokenHash.Length);
        Assert.True(tokenInDb.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task ForgotPassword_UnknownUser_ReturnsSameGenericMessageAndDispatchesNothing()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var config = CreateTestConfiguration();
        var emailService = new TestEmailService();

        var controller = new AuthController(db, config, emailService);

        var result = await controller.ForgotPassword(new ForgotPasswordRequest { Email = "does.not.exist@test.com" });
        var okResult = Assert.IsType<OkObjectResult>(result);

        // Generic message prevents email enumeration
        Assert.Contains("instructions have been sent", okResult.Value?.ToString());
        Assert.Empty(emailService.DispatchedEmails);
        Assert.Empty(db.PasswordResetTokens);
    }

    [Fact]
    public async Task ResetPassword_Workflow_Success_And_SingleUseEnforcement()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var config = CreateTestConfiguration();
        var emailService = new TestEmailService();
        var hasher = new PasswordHasher<User>();

        var user = new User
        {
            Id = 61,
            FullName = "Reset User 2",
            Email = "reset2@test.com",
            Role = "Employee",
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = hasher.HashPassword(user, "InitialPassword123!");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var controller = new AuthController(db, config, emailService);

        // 1. Request reset
        await controller.ForgotPassword(new ForgotPasswordRequest { Email = "reset2@test.com" });
        var rawToken = emailService.DispatchedEmails[0].Token;

        // 2. Perform reset with valid token
        var resetRequest = new ResetPasswordRequest
        {
            Email = "reset2@test.com",
            Token = rawToken,
            NewPassword = "NewlyResetPassword789!",
            ConfirmNewPassword = "NewlyResetPassword789!"
        };

        var resetResult = await controller.ResetPassword(resetRequest);
        var okResult = Assert.IsType<OkObjectResult>(resetResult);
        Assert.Contains("Password has been reset successfully", okResult.Value?.ToString());

        // 3. User can login with new password
        var loginResult = controller.Login(new LoginRequest
        {
            Email = "reset2@test.com",
            Password = "NewlyResetPassword789!"
        });
        Assert.IsType<OkObjectResult>(loginResult);

        // 4. Token single-use enforcement: Attempting to reset again with same token must FAIL
        var reuseResult = await controller.ResetPassword(resetRequest);
        var badRequest = Assert.IsType<BadRequestObjectResult>(reuseResult);
        Assert.Contains("already been used", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task ResetPassword_ExpiredToken_FailsWithBadRequest()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var config = CreateTestConfiguration();
        var emailService = new TestEmailService();

        var user = new User
        {
            Id = 62,
            FullName = "Reset User 3",
            Email = "reset3@test.com",
            Role = "Employee",
            PasswordHash = "initial_hash",
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var controller = new AuthController(db, config, emailService);

        // Request reset
        await controller.ForgotPassword(new ForgotPasswordRequest { Email = "reset3@test.com" });
        var rawToken = emailService.DispatchedEmails[0].Token;

        // Manually expire the token in database
        var tokenRecord = await db.PasswordResetTokens.FirstAsync(t => t.UserId == user.Id);
        tokenRecord.ExpiresAt = DateTime.UtcNow.AddMinutes(-5); // 5 minutes in the past
        await db.SaveChangesAsync();

        // Attempt reset with expired token
        var resetRequest = new ResetPasswordRequest
        {
            Email = "reset3@test.com",
            Token = rawToken,
            NewPassword = "NewPassword123!",
            ConfirmNewPassword = "NewPassword123!"
        };

        var resetResult = await controller.ResetPassword(resetRequest);
        var badRequest = Assert.IsType<BadRequestObjectResult>(resetResult);
        Assert.Contains("expired", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task ResetPassword_TamperedToken_FailsWithBadRequest()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var config = CreateTestConfiguration();
        var emailService = new TestEmailService();

        var user = new User
        {
            Id = 63,
            FullName = "Reset User 4",
            Email = "reset4@test.com",
            Role = "Employee",
            PasswordHash = "initial_hash",
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var controller = new AuthController(db, config, emailService);
        await controller.ForgotPassword(new ForgotPasswordRequest { Email = "reset4@test.com" });

        var resetRequest = new ResetPasswordRequest
        {
            Email = "reset4@test.com",
            Token = "invalid_or_tampered_token_string",
            NewPassword = "NewPassword123!",
            ConfirmNewPassword = "NewPassword123!"
        };

        var resetResult = await controller.ResetPassword(resetRequest);
        var badRequest = Assert.IsType<BadRequestObjectResult>(resetResult);
        Assert.Contains("Invalid", badRequest.Value?.ToString());
    }
}
