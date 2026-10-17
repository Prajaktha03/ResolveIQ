using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ResolveIQ.Api.Controllers;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Models;
using ResolveIQ.Api.Services;
using Xunit;

namespace ResolveIQ.Tests;

public class NotificationApiAndLifecycleTests
{
    private static async Task<(NotificationsController controller, AppDbContext db, User userA, User userB)> CreateSetupAsync()
    {
        var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, _) = await TestHelpers.SeedDefaultUsersAsync(db);

        var controller = new NotificationsController(db);
        return (controller, db, employee, agent);
    }

    [Fact]
    public async Task GetNotifications_ScopedToCaller_NeverExposesOtherUsersNotifications()
    {
        var (controller, db, userA, userB) = await CreateSetupAsync();

        // Seed notifications for userA and userB
        db.Notifications.AddRange(
            new Notification { Id = 1, UserId = userA.Id, Type = "NewComment", Title = "User A Notif 1", Message = "Msg A1", CreatedAt = DateTime.UtcNow.AddMinutes(-10) },
            new Notification { Id = 2, UserId = userA.Id, Type = "TicketAssigned", Title = "User A Notif 2", Message = "Msg A2", CreatedAt = DateTime.UtcNow.AddMinutes(-5) },
            new Notification { Id = 3, UserId = userB.Id, Type = "NewComment", Title = "User B Secret Notif", Message = "Msg B", CreatedAt = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        // Authenticate as User A
        TestHelpers.SetUserContext(controller, userA.Id, userA.Role);

        var result = await controller.GetNotifications(new NotificationQueryParameters());
        var okResult = Assert.IsType<OkObjectResult>(result);
        var paged = Assert.IsType<PagedResult<NotificationResponse>>(okResult.Value);

        Assert.Equal(2, paged.TotalCount);
        Assert.All(paged.Items, n => Assert.Equal(userA.Id, n.UserId));
        Assert.DoesNotContain(paged.Items, n => n.Title.Contains("User B"));
    }

    [Fact]
    public async Task GetNotifications_FilterIsRead_ReturnsOnlyMatchingReadState()
    {
        var (controller, db, userA, _) = await CreateSetupAsync();

        db.Notifications.AddRange(
            new Notification { Id = 10, UserId = userA.Id, Title = "Unread 1", IsRead = false, CreatedAt = DateTime.UtcNow.AddMinutes(-10) },
            new Notification { Id = 11, UserId = userA.Id, Title = "Read 1", IsRead = true, ReadAt = DateTime.UtcNow.AddMinutes(-5), CreatedAt = DateTime.UtcNow.AddMinutes(-8) },
            new Notification { Id = 12, UserId = userA.Id, Title = "Unread 2", IsRead = false, CreatedAt = DateTime.UtcNow.AddMinutes(-2) }
        );
        await db.SaveChangesAsync();

        TestHelpers.SetUserContext(controller, userA.Id, userA.Role);

        // Filter unread
        var unreadResult = await controller.GetNotifications(new NotificationQueryParameters { IsRead = false });
        var unreadPaged = Assert.IsType<PagedResult<NotificationResponse>>(Assert.IsType<OkObjectResult>(unreadResult).Value);
        Assert.Equal(2, unreadPaged.TotalCount);
        Assert.All(unreadPaged.Items, n => Assert.False(n.IsRead));

        // Filter read
        var readResult = await controller.GetNotifications(new NotificationQueryParameters { IsRead = true });
        var readPaged = Assert.IsType<PagedResult<NotificationResponse>>(Assert.IsType<OkObjectResult>(readResult).Value);
        Assert.Single(readPaged.Items);
        Assert.True(readPaged.Items[0].IsRead);
    }

    [Fact]
    public async Task GetNotifications_PaginationAndClamping_WorksDeterministic()
    {
        var (controller, db, userA, _) = await CreateSetupAsync();

        for (int i = 1; i <= 15; i++)
        {
            db.Notifications.Add(new Notification
            {
                Id = i,
                UserId = userA.Id,
                Title = $"Notif {i:D2}",
                CreatedAt = DateTime.UtcNow.AddMinutes(i)
            });
        }
        await db.SaveChangesAsync();

        TestHelpers.SetUserContext(controller, userA.Id, userA.Role);

        // First page, size 5 (ordered descending by CreatedAt)
        var p1 = await controller.GetNotifications(new NotificationQueryParameters { Page = 1, PageSize = 5 });
        var p1Result = Assert.IsType<PagedResult<NotificationResponse>>(Assert.IsType<OkObjectResult>(p1).Value);
        Assert.Equal(5, p1Result.Items.Count);
        Assert.Equal(15, p1Result.TotalCount);
        Assert.Equal(3, p1Result.TotalPages);
        Assert.True(p1Result.HasNextPage);
        Assert.False(p1Result.HasPreviousPage);

        // Page clamping: negative page clamps to 1
        var clampPage = await controller.GetNotifications(new NotificationQueryParameters { Page = -5, PageSize = 5 });
        var clampResult = Assert.IsType<PagedResult<NotificationResponse>>(Assert.IsType<OkObjectResult>(clampPage).Value);
        Assert.Equal(1, clampResult.Page);

        // Page size clamping: 0 clamps to default 10; > 100 clamps to 100
        var clampSize0 = await controller.GetNotifications(new NotificationQueryParameters { PageSize = 0 });
        Assert.Equal(10, Assert.IsType<PagedResult<NotificationResponse>>(Assert.IsType<OkObjectResult>(clampSize0).Value).PageSize);

        var clampSizeMax = await controller.GetNotifications(new NotificationQueryParameters { PageSize = 999 });
        Assert.Equal(100, Assert.IsType<PagedResult<NotificationResponse>>(Assert.IsType<OkObjectResult>(clampSizeMax).Value).PageSize);
    }

    [Fact]
    public async Task GetUnreadCount_ReturnsAccurateCountForCaller()
    {
        var (controller, db, userA, userB) = await CreateSetupAsync();

        db.Notifications.AddRange(
            new Notification { Id = 1, UserId = userA.Id, IsRead = false },
            new Notification { Id = 2, UserId = userA.Id, IsRead = false },
            new Notification { Id = 3, UserId = userA.Id, IsRead = true },
            new Notification { Id = 4, UserId = userB.Id, IsRead = false } // user B's unread
        );
        await db.SaveChangesAsync();

        TestHelpers.SetUserContext(controller, userA.Id, userA.Role);

        var result = await controller.GetUnreadCount();
        var okResult = Assert.IsType<OkObjectResult>(result);
        var unread = Assert.IsType<UnreadCountResponse>(okResult.Value);

        Assert.Equal(2, unread.UnreadCount);
    }

    [Fact]
    public async Task MarkAsRead_CallerOwnsNotification_UpdatesIsReadAndReadAt()
    {
        var (controller, db, userA, _) = await CreateSetupAsync();

        var notif = new Notification
        {
            Id = 20,
            UserId = userA.Id,
            Title = "Read Test",
            IsRead = false,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        db.Notifications.Add(notif);
        await db.SaveChangesAsync();

        TestHelpers.SetUserContext(controller, userA.Id, userA.Role);

        var result = await controller.MarkAsRead(notif.Id);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<NotificationResponse>(okResult.Value);

        Assert.True(response.IsRead);
        Assert.NotNull(response.ReadAt);

        var inDb = await db.Notifications.FindAsync(notif.Id);
        Assert.True(inDb!.IsRead);
        Assert.NotNull(inDb.ReadAt);
    }

    [Fact]
    public async Task MarkAsRead_OtherUserNotification_ReturnsForbidden()
    {
        var (controller, db, userA, userB) = await CreateSetupAsync();

        var notifB = new Notification
        {
            Id = 21,
            UserId = userB.Id,
            Title = "User B Secret",
            IsRead = false
        };
        db.Notifications.Add(notifB);
        await db.SaveChangesAsync();

        // User A attempts to mark User B's notification read
        TestHelpers.SetUserContext(controller, userA.Id, userA.Role);

        var result = await controller.MarkAsRead(notifB.Id);
        Assert.IsType<ForbidResult>(result);

        // Must remain unread
        var inDb = await db.Notifications.FindAsync(notifB.Id);
        Assert.False(inDb!.IsRead);
    }

    [Fact]
    public async Task MarkAsRead_NonExistentNotification_ReturnsNotFound()
    {
        var (controller, _, userA, _) = await CreateSetupAsync();
        TestHelpers.SetUserContext(controller, userA.Id, userA.Role);

        var result = await controller.MarkAsRead(99999);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task MarkAllAsRead_MarksAllCallerUnreadAsRead_ReturnsCount()
    {
        var (controller, db, userA, userB) = await CreateSetupAsync();

        db.Notifications.AddRange(
            new Notification { Id = 31, UserId = userA.Id, IsRead = false },
            new Notification { Id = 32, UserId = userA.Id, IsRead = false },
            new Notification { Id = 33, UserId = userA.Id, IsRead = true },
            new Notification { Id = 34, UserId = userB.Id, IsRead = false } // user B unread
        );
        await db.SaveChangesAsync();

        TestHelpers.SetUserContext(controller, userA.Id, userA.Role);

        var result = await controller.MarkAllAsRead();
        Assert.IsType<OkObjectResult>(result);

        // User A has 0 unread now
        var userAUnread = await db.Notifications.CountAsync(n => n.UserId == userA.Id && !n.IsRead);
        Assert.Equal(0, userAUnread);

        // User B's unread notification remains unread
        var userBNotif = await db.Notifications.FindAsync(34);
        Assert.False(userBNotif!.IsRead);
    }

    [Fact]
    public async Task NotificationService_Deduplication_SkipsDuplicatesWithSameKey()
    {
        using var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, _, _) = await TestHelpers.SeedDefaultUsersAsync(db);

        var service = new NotificationService(db);

        var key = "unique_alert_key_100";

        // First call: creates notification
        var first = await service.CreateNotificationAsync(employee.Id, "SlaBreached", "Alert", "Msg", 1, key);
        Assert.NotNull(first);
        Assert.Equal(1, await db.Notifications.CountAsync());

        // Second call: duplicate key -> returns null and does NOT insert
        var second = await service.CreateNotificationAsync(employee.Id, "SlaBreached", "Alert", "Msg", 1, key);
        Assert.Null(second);
        Assert.Equal(1, await db.Notifications.CountAsync());
    }
}
