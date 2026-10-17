using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Hubs;
using ResolveIQ.Api.Models;

namespace ResolveIQ.Api.Services;

public class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    private readonly IHubContext<NotificationHub>? _hubContext;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        AppDbContext db,
        IHubContext<NotificationHub>? hubContext = null,
        ILogger<NotificationService>? logger = null)
    {
        _db = db;
        _hubContext = hubContext;
        _logger = logger ?? NullLogger<NotificationService>.Instance;
    }

    public async Task<Notification?> CreateNotificationAsync(
        int recipientUserId,
        string type,
        string title,
        string message,
        int? ticketId = null,
        string? deduplicationKey = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(deduplicationKey))
        {
            var alreadyExists = await _db.Notifications
                .AnyAsync(n => n.DeduplicationKey == deduplicationKey, cancellationToken);

            if (alreadyExists)
            {
                _logger.LogInformation("Notification with deduplication key {DeduplicationKey} already exists. Skipping duplicate.", deduplicationKey);
                return null;
            }
        }

        var notification = new Notification
        {
            UserId = recipientUserId,
            Type = type,
            Title = title,
            Message = message,
            TicketId = ticketId,
            CreatedAt = DateTime.UtcNow,
            IsRead = false,
            DeduplicationKey = deduplicationKey
        };

        _db.Notifications.Add(notification);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // If another concurrent request or poll created the deduplicated notification first, handle gracefully
            if (!string.IsNullOrWhiteSpace(deduplicationKey))
            {
                _logger.LogWarning(ex, "Concurrency conflict on deduplication key {DeduplicationKey}. Skipping duplicate.", deduplicationKey);
                return null;
            }

            throw;
        }

        // Real-time dispatch via SignalR to recipient user
        if (_hubContext != null)
        {
            var responseDto = new NotificationResponse
            {
                Id = notification.Id,
                UserId = notification.UserId,
                Type = notification.Type,
                Title = notification.Title,
                Message = notification.Message,
                TicketId = notification.TicketId,
                CreatedAt = notification.CreatedAt,
                IsRead = notification.IsRead,
                ReadAt = notification.ReadAt
            };

            try
            {
                await _hubContext.Clients.User(recipientUserId.ToString())
                    .SendAsync("ReceiveNotification", responseDto, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deliver real-time SignalR notification to user {UserId}.", recipientUserId);
            }
        }

        return notification;
    }

    public async Task<List<Notification>> CreateNotificationsForUsersAsync(
        IEnumerable<int> recipientUserIds,
        string type,
        string title,
        string message,
        int? ticketId = null,
        string? deduplicationKeyPrefix = null,
        int? excludeUserId = null,
        CancellationToken cancellationToken = default)
    {
        var created = new List<Notification>();

        foreach (var userId in recipientUserIds.Distinct())
        {
            if (excludeUserId.HasValue && userId == excludeUserId.Value)
            {
                continue; // Do not notify the actor of their own action
            }

            var dedupKey = string.IsNullOrWhiteSpace(deduplicationKeyPrefix)
                ? null
                : $"{deduplicationKeyPrefix}_user_{userId}";

            var notification = await CreateNotificationAsync(
                userId,
                type,
                title,
                message,
                ticketId,
                dedupKey,
                cancellationToken);

            if (notification != null)
            {
                created.Add(notification);
            }
        }

        return created;
    }

    public async Task<List<Notification>> CreateNotificationsForRoleAsync(
        string role,
        string type,
        string title,
        string message,
        int? ticketId = null,
        string? deduplicationKeyPrefix = null,
        int? excludeUserId = null,
        CancellationToken cancellationToken = default)
    {
        var targetUserIds = await _db.Users
            .Where(u => u.Role == role)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        return await CreateNotificationsForUsersAsync(
            targetUserIds,
            type,
            title,
            message,
            ticketId,
            deduplicationKeyPrefix,
            excludeUserId,
            cancellationToken);
    }
}
