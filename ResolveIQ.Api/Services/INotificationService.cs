using ResolveIQ.Api.Models;

namespace ResolveIQ.Api.Services;

public interface INotificationService
{
    Task<Notification?> CreateNotificationAsync(
        int recipientUserId,
        string type,
        string title,
        string message,
        int? ticketId = null,
        string? deduplicationKey = null,
        CancellationToken cancellationToken = default);

    Task<List<Notification>> CreateNotificationsForUsersAsync(
        IEnumerable<int> recipientUserIds,
        string type,
        string title,
        string message,
        int? ticketId = null,
        string? deduplicationKeyPrefix = null,
        int? excludeUserId = null,
        CancellationToken cancellationToken = default);

    Task<List<Notification>> CreateNotificationsForRoleAsync(
        string role,
        string type,
        string title,
        string message,
        int? ticketId = null,
        string? deduplicationKeyPrefix = null,
        int? excludeUserId = null,
        CancellationToken cancellationToken = default);
}
