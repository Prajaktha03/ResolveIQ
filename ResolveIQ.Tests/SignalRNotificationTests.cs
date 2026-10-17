using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Hubs;
using ResolveIQ.Api.Services;
using Xunit;

namespace ResolveIQ.Tests;

public class SignalRNotificationTests
{
    [Fact]
    public async Task NotificationService_DispatchesRealtimeSignalR_ToRecipientUserId()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, _, _) = await TestHelpers.SeedDefaultUsersAsync(db);

        var testHub = new TestHubContext();
        var service = new NotificationService(db, testHub);

        var created = await service.CreateNotificationAsync(
            employee.Id,
            "TicketAssigned",
            "Realtime Alert",
            "You have been assigned Ticket #1.",
            ticketId: 1);

        Assert.NotNull(created);

        // Verify SignalR dispatched to employee.Id
        var userIdStr = employee.Id.ToString();
        Assert.True(testHub.MockClients.UserProxies.ContainsKey(userIdStr));

        var userProxy = testHub.MockClients.UserProxies[userIdStr];
        Assert.Single(userProxy.Invocations);

        var invocation = userProxy.Invocations[0];
        Assert.Equal("ReceiveNotification", invocation.Method);
        Assert.Single(invocation.Args);

        var dto = Assert.IsType<NotificationResponse>(invocation.Args[0]);
        Assert.Equal(created.Id, dto.Id);
        Assert.Equal(employee.Id, dto.UserId);
        Assert.Equal("Realtime Alert", dto.Title);
        Assert.Equal("TicketAssigned", dto.Type);
    }

    [Fact]
    public async Task NotificationService_TransientSignalRFailure_DoesNotCorruptOrRollbackPersistedNotification()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, _, _) = await TestHelpers.SeedDefaultUsersAsync(db);

        var testHub = new TestHubContext();
        // Configure SignalR proxy for employee to throw an exception upon send
        var userProxy = testHub.MockClients.User(employee.Id.ToString()) as TestClientProxy;
        userProxy!.ShouldThrow = true;

        var service = new NotificationService(db, testHub);

        // Service must NOT throw; transient failure is logged, and notification is safely persisted
        var created = await service.CreateNotificationAsync(
            employee.Id,
            "TicketCreated",
            "Resilient Notification",
            "Message even if WebSocket is disconnected.",
            ticketId: 99);

        Assert.NotNull(created);

        // Verify record is successfully stored in database
        var inDb = await db.Notifications.FirstOrDefaultAsync(n => n.Id == created.Id);
        Assert.NotNull(inDb);
        Assert.Equal("Resilient Notification", inDb.Title);
        Assert.Equal(employee.Id, inDb.UserId);
        Assert.False(inDb.IsRead);
    }

    [Fact]
    public async Task NotificationHub_OnConnected_EnrollsInRoleGroup()
    {
        var hub = new NotificationHub();

        var groupManagerMock = new TestGroupManager();
        var hubCallerContextMock = new TestHubCallerContext("test_connection_id", "Agent");

        hub.Context = hubCallerContextMock;
        hub.Groups = groupManagerMock;

        await hub.OnConnectedAsync();

        // Enrolled in Role_Agent group
        Assert.Single(groupManagerMock.AddedToGroups);
        var (connId, groupName) = groupManagerMock.AddedToGroups[0];
        Assert.Equal("test_connection_id", connId);
        Assert.Equal("Role_Agent", groupName);
    }
}

public class TestGroupManager : IGroupManager
{
    public List<(string ConnectionId, string GroupName)> AddedToGroups { get; } = new();

    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        AddedToGroups.Add((connectionId, groupName));
        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public class TestHubCallerContext : HubCallerContext
{
    private readonly string _connectionId;
    private readonly ClaimsPrincipal _user;

    public TestHubCallerContext(string connectionId, string role)
    {
        _connectionId = connectionId;
        _user = TestHelpers.CreateClaimsPrincipal(1, role);
    }

    public override string ConnectionId => _connectionId;
    public override string? UserIdentifier => "1";
    public override ClaimsPrincipal? User => _user;
    public override IDictionary<object, object?> Items => new Dictionary<object, object?>();
    public override IFeatureCollection Features => throw new NotImplementedException();
    public override CancellationToken ConnectionAborted => CancellationToken.None;
    public override void Abort() { }
}
