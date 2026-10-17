using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.Hubs;
using ResolveIQ.Api.Models;

namespace ResolveIQ.Tests;

public static class TestHelpers
{
    public static AppDbContext CreateInMemoryDbContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName ?? Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(options);
    }

    public static ClaimsPrincipal CreateClaimsPrincipal(int userId, string role, string fullName = "Test User", string email = "test@example.com")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, fullName),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, role)
        };

        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    public static void SetUserContext(ControllerBase controller, int userId, string role, string fullName = "Test User", string email = "test@example.com")
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = CreateClaimsPrincipal(userId, role, fullName, email)
            }
        };
    }

    public static async Task<(User employee, User agent, User admin)> SeedDefaultUsersAsync(AppDbContext db)
    {
        var employee = new User
        {
            Id = 1,
            FullName = "Employee One",
            Email = "employee1@test.com",
            PasswordHash = "hashed_pw",
            Role = "Employee",
            CreatedAt = DateTime.UtcNow
        };

        var agent = new User
        {
            Id = 2,
            FullName = "Agent One",
            Email = "agent1@test.com",
            PasswordHash = "hashed_pw",
            Role = "Agent",
            CreatedAt = DateTime.UtcNow
        };

        var admin = new User
        {
            Id = 3,
            FullName = "Admin One",
            Email = "admin1@test.com",
            PasswordHash = "hashed_pw",
            Role = "Admin",
            CreatedAt = DateTime.UtcNow
        };

        db.Users.AddRange(employee, agent, admin);
        await db.SaveChangesAsync();

        return (employee, agent, admin);
    }
}

public class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _currentTime;

    public TestTimeProvider(DateTimeOffset initialTime)
    {
        _currentTime = initialTime;
    }

    public override DateTimeOffset GetUtcNow() => _currentTime;

    public void SetUtcNow(DateTimeOffset newTime) => _currentTime = newTime;

    public void Advance(TimeSpan duration) => _currentTime = _currentTime.Add(duration);
}

public class TestEmailService : ResolveIQ.Api.Services.IEmailService
{
    public List<(string Email, string Token)> DispatchedEmails { get; } = new();

    public Task SendPasswordResetEmailAsync(string toEmail, string resetToken)
    {
        DispatchedEmails.Add((toEmail, resetToken));
        return Task.CompletedTask;
    }
}

public class TestHubContext : IHubContext<NotificationHub>
{
    public TestHubClients MockClients { get; } = new();
    public IHubClients Clients => MockClients;
    public IGroupManager Groups => throw new NotImplementedException();
}

public class TestHubClients : IHubClients
{
    public Dictionary<string, TestClientProxy> UserProxies { get; } = new();

    public IClientProxy All => new TestClientProxy();
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new TestClientProxy();
    public IClientProxy Client(string connectionId) => new TestClientProxy();
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new TestClientProxy();
    public IClientProxy Group(string groupName) => new TestClientProxy();
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new TestClientProxy();
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => new TestClientProxy();

    public IClientProxy User(string userId)
    {
        if (!UserProxies.TryGetValue(userId, out var proxy))
        {
            proxy = new TestClientProxy();
            UserProxies[userId] = proxy;
        }
        return proxy;
    }

    public IClientProxy Users(IReadOnlyList<string> userIds) => new TestClientProxy();
}

public class TestClientProxy : IClientProxy
{
    public List<(string Method, object?[] Args)> Invocations { get; } = new();
    public bool ShouldThrow { get; set; }

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        if (ShouldThrow)
            throw new InvalidOperationException("Simulated transient transport failure.");

        Invocations.Add((method, args));
        return Task.CompletedTask;
    }
}
