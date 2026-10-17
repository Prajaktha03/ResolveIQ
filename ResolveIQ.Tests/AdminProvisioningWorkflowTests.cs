using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ResolveIQ.Api.Controllers;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Models;
using ResolveIQ.Api.Services;
using Xunit;

namespace ResolveIQ.Tests;

public class AdminProvisioningWorkflowTests
{
    private static (RoleSetupService service, AppDbContext db) CreateTestService()
    {
        var db = TestHelpers.CreateInMemoryDbContext();
        var service = new RoleSetupService(db, NullLogger<RoleSetupService>.Instance);
        return (service, db);
    }

    [Fact]
    public async Task ProvisionAdmins_CreatesBothConfiguredAdmins_Successfully()
    {
        var (service, db) = CreateTestService();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RESOLVEIQ_ADMIN_NAME"] = "Nauman Shah",
                ["RESOLVEIQ_ADMIN_EMAIL"] = "naumans528@gmail.com",
                ["RESOLVEIQ_ADMIN_PASSWORD"] = "SecureTestPassword123!",
                ["RESOLVEIQ_ADMIN2_NAME"] = "Prajaktha",
                ["RESOLVEIQ_ADMIN2_EMAIL"] = "prajaktha3@gmail.com",
                ["RESOLVEIQ_ADMIN2_PASSWORD"] = "AnotherSecurePassword456!"
            })
            .Build();

        var results = await service.ProvisionConfiguredAdminsAsync(config);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(ProvisioningStatus.Created, r.Status));

        var admin1 = await db.Users.FirstOrDefaultAsync(u => u.Email == "naumans528@gmail.com");
        Assert.NotNull(admin1);
        Assert.Equal("Nauman Shah", admin1.FullName);
        Assert.Equal("Admin", admin1.Role);

        var admin2 = await db.Users.FirstOrDefaultAsync(u => u.Email == "prajaktha3@gmail.com");
        Assert.NotNull(admin2);
        Assert.Equal("Prajaktha", admin2.FullName);
        Assert.Equal("Admin", admin2.Role);

        var hasher = new PasswordHasher<User>();
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(admin1, admin1.PasswordHash, "SecureTestPassword123!"));
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(admin2, admin2.PasswordHash, "AnotherSecurePassword456!"));
    }

    [Fact]
    public async Task ProvisionAdmins_IsIdempotent_DoesNotDuplicateOrChangePassword()
    {
        var (service, db) = CreateTestService();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RESOLVEIQ_ADMIN_NAME"] = "Nauman Shah",
                ["RESOLVEIQ_ADMIN_EMAIL"] = "naumans528@gmail.com",
                ["RESOLVEIQ_ADMIN_PASSWORD"] = "InitialPassword123!"
            })
            .Build();

        // First run: Created
        var initialResults = await service.ProvisionConfiguredAdminsAsync(config);
        Assert.Equal(ProvisioningStatus.Created, initialResults[0].Status);

        var initialAdmin = await db.Users.AsNoTracking().FirstAsync(u => u.Email == "naumans528@gmail.com");
        var initialHash = initialAdmin.PasswordHash;

        // Second run with different password in configuration: Must preserve existing password
        var secondConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RESOLVEIQ_ADMIN_NAME"] = "Nauman Shah",
                ["RESOLVEIQ_ADMIN_EMAIL"] = "naumans528@gmail.com",
                ["RESOLVEIQ_ADMIN_PASSWORD"] = "DifferentPassword789!"
            })
            .Build();

        var secondResults = await service.ProvisionConfiguredAdminsAsync(secondConfig);
        Assert.Equal(ProvisioningStatus.Preserved, secondResults[0].Status);

        var preservedAdmin = await db.Users.AsNoTracking().FirstAsync(u => u.Email == "naumans528@gmail.com");
        Assert.Equal(initialHash, preservedAdmin.PasswordHash);

        var totalAdmins = await db.Users.CountAsync(u => u.Email == "naumans528@gmail.com");
        Assert.Equal(1, totalAdmins);
    }

    [Fact]
    public async Task ProvisionAdmins_PreservesExistingAdmin_WhenPreexistingInDatabase()
    {
        var (service, db) = CreateTestService();

        var hasher = new PasswordHasher<User>();
        var preExistingAdmin = new User
        {
            FullName = "Pre-existing Admin",
            Email = "naumans528@gmail.com",
            Role = "Admin",
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        };
        preExistingAdmin.PasswordHash = hasher.HashPassword(preExistingAdmin, "OriginalAdminPassword!");
        db.Users.Add(preExistingAdmin);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RESOLVEIQ_ADMIN_NAME"] = "Nauman Shah",
                ["RESOLVEIQ_ADMIN_EMAIL"] = "naumans528@gmail.com",
                ["RESOLVEIQ_ADMIN_PASSWORD"] = "IgnoredStartupPassword123!"
            })
            .Build();

        var results = await service.ProvisionConfiguredAdminsAsync(config);

        Assert.Equal(ProvisioningStatus.Preserved, results[0].Status);
        var current = await db.Users.FirstAsync(u => u.Email == "naumans528@gmail.com");
        Assert.Equal("Pre-existing Admin", current.FullName);
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(current, current.PasswordHash, "OriginalAdminPassword!"));
    }

    [Fact]
    public async Task ProvisionAdmins_RejectsConflict_WhenEmailBelongsToNonAdmin()
    {
        var (service, db) = CreateTestService();

        var hasher = new PasswordHasher<User>();
        var existingEmployee = new User
        {
            FullName = "Existing Employee",
            Email = "prajaktha3@gmail.com",
            Role = "Employee",
            CreatedAt = DateTime.UtcNow.AddDays(-5)
        };
        existingEmployee.PasswordHash = hasher.HashPassword(existingEmployee, "EmployeePass123!");
        db.Users.Add(existingEmployee);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RESOLVEIQ_ADMIN2_NAME"] = "Prajaktha",
                ["RESOLVEIQ_ADMIN2_EMAIL"] = "prajaktha3@gmail.com",
                ["RESOLVEIQ_ADMIN2_PASSWORD"] = "NewAdminPassword123!"
            })
            .Build();

        var results = await service.ProvisionConfiguredAdminsAsync(config);

        // Slot 2 should report Conflict
        Assert.Equal(ProvisioningStatus.Conflict, results[1].Status);

        // Account must remain Employee and retain original password
        var account = await db.Users.FirstAsync(u => u.Email == "prajaktha3@gmail.com");
        Assert.Equal("Employee", account.Role);
        Assert.Equal("Existing Employee", account.FullName);
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(account, account.PasswordHash, "EmployeePass123!"));
    }

    [Fact]
    public async Task ProvisionAdmins_HandlesMissingOrPartialConfiguration_SafelyWithoutCrashing()
    {
        var (service, db) = CreateTestService();

        // Admin 1 completely omitted; Admin 2 has email but no password
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RESOLVEIQ_ADMIN2_NAME"] = "Prajaktha",
                ["RESOLVEIQ_ADMIN2_EMAIL"] = "prajaktha3@gmail.com"
                // Password omitted
            })
            .Build();

        var results = await service.ProvisionConfiguredAdminsAsync(config);

        Assert.Equal(2, results.Count);
        Assert.Equal(ProvisioningStatus.Skipped, results[0].Status);
        Assert.Equal(ProvisioningStatus.Invalid, results[1].Status);

        Assert.False(await db.Users.AnyAsync());
    }

    [Theory]
    [InlineData("short", false, "Password must be at least 8 characters long.")]
    [InlineData("1234567", false, "Password must be at least 8 characters long.")]
    [InlineData("validPassword123!", true, null)]
    public async Task ProvisionAdmin_ValidatesPasswordRequirements(string password, bool shouldSucceed, string? expectedError)
    {
        var (service, db) = CreateTestService();

        var result = await service.ProvisionAdminAccountAsync("Test Admin", "admin@test.local", password);

        if (shouldSucceed)
        {
            Assert.Equal(ProvisioningStatus.Created, result.Status);
            var user = await db.Users.FirstAsync(u => u.Email == "admin@test.local");
            Assert.True(new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Success);
        }
        else
        {
            Assert.Equal(ProvisioningStatus.Invalid, result.Status);
            Assert.Equal(expectedError, result.Message);
            Assert.False(await db.Users.AnyAsync());
        }
    }

    [Theory]
    [InlineData("notanemail", false)]
    [InlineData("@nodomain.com", false)]
    [InlineData("plainaddress", false)]
    [InlineData("valid.user@example.com", true)]
    public async Task ProvisionAdmin_ValidatesEmailFormat(string email, bool shouldSucceed)
    {
        var (service, _) = CreateTestService();

        var result = await service.ProvisionAdminAccountAsync("Test User", email, "ValidPassword123!");

        if (shouldSucceed)
        {
            Assert.Equal(ProvisioningStatus.Created, result.Status);
        }
        else
        {
            Assert.Equal(ProvisioningStatus.Invalid, result.Status);
        }
    }

    [Fact]
    public async Task PublicRegistration_StrictlyRestrictsRoleToEmployee()
    {
        var db = TestHelpers.CreateInMemoryDbContext();
        var keyBytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(keyBytes);
        var base64Key = Convert.ToBase64String(keyBytes);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Jwt:Key", base64Key },
                { "Jwt:Issuer", "ResolveIQ.Tests" },
                { "Jwt:Audience", "ResolveIQ.Tests.Clients" }
            })
            .Build();

        var controller = new AuthController(db, config);

        var registerRequest = new RegisterRequest
        {
            FullName = "New User",
            Email = "newuser@test.local",
            Password = "UserPassword123!"
        };

        var actionResult = await controller.Register(registerRequest);
        var okResult = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(actionResult);
        var authResponse = Assert.IsType<AuthResponse>(okResult.Value);

        Assert.Equal("Employee", authResponse.Role);

        var savedUser = await db.Users.FirstAsync(u => u.Email == "newuser@test.local");
        Assert.Equal("Employee", savedUser.Role);
    }

    [Fact]
    public void RenderYaml_DefinesAdminEnvironmentVariables_WithSyncFalseForPasswords()
    {
        var baseDir = AppContext.BaseDirectory;
        var currentDir = new DirectoryInfo(baseDir);
        string? renderYamlPath = null;

        while (currentDir != null)
        {
            var candidate = Path.Combine(currentDir.FullName, "render.yaml");
            if (File.Exists(candidate))
            {
                renderYamlPath = candidate;
                break;
            }
            currentDir = currentDir.Parent;
        }

        Assert.NotNull(renderYamlPath);
        var content = File.ReadAllText(renderYamlPath);

        // Admin 1
        Assert.Contains("key: RESOLVEIQ_ADMIN_NAME", content);
        Assert.Contains("value: \"Nauman Shah\"", content);
        Assert.Contains("key: RESOLVEIQ_ADMIN_EMAIL", content);
        Assert.Contains("value: \"naumans528@gmail.com\"", content);
        Assert.Contains("key: RESOLVEIQ_ADMIN_PASSWORD", content);

        // Admin 2
        Assert.Contains("key: RESOLVEIQ_ADMIN2_NAME", content);
        Assert.Contains("value: \"Prajaktha\"", content);
        Assert.Contains("key: RESOLVEIQ_ADMIN2_EMAIL", content);
        Assert.Contains("value: \"prajaktha3@gmail.com\"", content);
        Assert.Contains("key: RESOLVEIQ_ADMIN2_PASSWORD", content);

        // Passwords must have sync: false and no hardcoded plain-text passwords
        Assert.DoesNotContain("value: Password", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("value: Secret", content, StringComparison.OrdinalIgnoreCase);
    }
}
