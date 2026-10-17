using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.DTOs;

namespace ResolveIQ.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("general")]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public NotificationsController(AppDbContext db)
    {
        _db = db;
    }

    private int? CurrentUserId
    {
        get
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(id, out var userId) ? userId : null;
        }
    }

    // GET: api/Notifications
    [HttpGet]
    public async Task<IActionResult> GetNotifications([FromQuery] NotificationQueryParameters? parameters = null)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        parameters ??= new NotificationQueryParameters();

        var query = _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId);

        if (parameters.IsRead.HasValue)
        {
            query = query.Where(n => n.IsRead == parameters.IsRead.Value);
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Skip((parameters.Page - 1) * parameters.PageSize)
            .Take(parameters.PageSize)
            .Select(n => new NotificationResponse
            {
                Id = n.Id,
                UserId = n.UserId,
                Type = n.Type,
                Title = n.Title,
                Message = n.Message,
                TicketId = n.TicketId,
                CreatedAt = n.CreatedAt,
                IsRead = n.IsRead,
                ReadAt = n.ReadAt
            })
            .ToListAsync();

        return Ok(new PagedResult<NotificationResponse>
        {
            Items = items,
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        });
    }

    // GET: api/Notifications/unread-count
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        var unreadCount = await _db.Notifications
            .CountAsync(n => n.UserId == userId && !n.IsRead);

        return Ok(new UnreadCountResponse
        {
            UnreadCount = unreadCount
        });
    }

    // PATCH: api/Notifications/5/read
    [HttpPatch("{id:int}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        var notification = await _db.Notifications.FindAsync(id);

        if (notification is null)
            return NotFound(new { message = "Notification not found." });

        if (notification.UserId != userId)
            return Forbid();

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return Ok(new NotificationResponse
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
        });
    }

    // PATCH: api/Notifications/read-all
    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        var unread = await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var notification in unread)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = "All notifications marked as read.",
            updatedCount = unread.Count
        });
    }
}
