using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Models;
using ResolveIQ.Api.Services;

namespace ResolveIQ.Api.Controllers;

[ApiController]
[Route("api/tickets/{ticketId:int}/comments")]
[Authorize]
[EnableRateLimiting("general")]
public class TicketCommentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly INotificationService? _notificationService;

    public TicketCommentsController(
        AppDbContext db,
        INotificationService? notificationService = null)
    {
        _db = db;
        _notificationService = notificationService;
    }

    private int? CurrentUserId
    {
        get
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(id, out var userId) ? userId : null;
        }
    }

    private async Task<bool> CanAccessTicketAsync(
        int ticketId, int userId)
    {
        var ticket = await _db.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == ticketId);

        if (ticket is null)
            return false;

        if (User.IsInRole("Admin"))
            return true;

        if (User.IsInRole("Agent"))
            return ticket.AssignedToUserId == userId;

        return ticket.CreatedByUserId == userId;
    }

    // GET: api/tickets/5/comments
    [HttpGet]
    public async Task<IActionResult> GetComments(int ticketId)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        var ticketExists = await _db.Tickets
            .AnyAsync(t => t.Id == ticketId);

        if (!ticketExists)
            return NotFound(new { message = "Ticket not found." });

        if (!await CanAccessTicketAsync(ticketId, userId))
            return Forbid();

        var comments = await _db.TicketComments
            .AsNoTracking()
            .Where(c => c.TicketId == ticketId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id,
                c.TicketId,
                c.UserId,
                Author = c.User.FullName,
                c.Content,
                c.CreatedAt
            })
            .ToListAsync();

        return Ok(comments);
    }

    // POST: api/tickets/5/comments
    [HttpPost]
    public async Task<IActionResult> AddComment(
        int ticketId,
        CreateCommentRequest request)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest(new
            {
                message = "Comment content is required."
            });
        }

        var ticket = await _db.Tickets
            .FirstOrDefaultAsync(t => t.Id == ticketId);

        if (ticket is null)
            return NotFound(new { message = "Ticket not found." });

        if (!await CanAccessTicketAsync(ticketId, userId))
            return Forbid();

        var now = DateTime.UtcNow;

        var comment = new TicketComment
        {
            TicketId = ticketId,
            UserId = userId,
            Content = request.Content.Trim(),
            CreatedAt = now
        };

        _db.TicketComments.Add(comment);

        // Milestone 1: When an authenticated Agent or Admin posts the first valid comment,
        // record FirstRespondedAt exactly once using UTC. Subsequent comments must not overwrite.
        // Employee comments do not count as the support team's first response.
        var isSupportUser = User.IsInRole("Agent") || User.IsInRole("Admin");
        if (isSupportUser && ticket.FirstRespondedAt == null)
        {
            ticket.FirstRespondedAt = now;

            _db.TicketHistories.Add(new TicketHistory
            {
                TicketId = ticket.Id,
                UserId = userId,
                Action = "FirstResponse",
                OldValue = null,
                NewValue = $"First support response recorded by user {userId}.",
                CreatedAt = now
            });
        }

        await _db.SaveChangesAsync();

        var authorName = User.FindFirstValue(ClaimTypes.Name)
            ?? User.Identity?.Name
            ?? "User";

        if (_notificationService != null)
        {
            // If author is Agent or Admin, notify ticket creator (if creator != userId)
            if (ticket.CreatedByUserId.HasValue && ticket.CreatedByUserId.Value != userId)
            {
                await _notificationService.CreateNotificationAsync(
                    ticket.CreatedByUserId.Value,
                    "NewComment",
                    $"New comment on Ticket #{ticket.Id}",
                    $"{authorName} commented on Ticket #{ticket.Id}.",
                    ticket.Id);
            }

            // If author is Employee (creator), notify assigned agent (if assigned and != userId)
            if (ticket.AssignedToUserId.HasValue && ticket.AssignedToUserId.Value != userId)
            {
                await _notificationService.CreateNotificationAsync(
                    ticket.AssignedToUserId.Value,
                    "NewComment",
                    $"New comment on Ticket #{ticket.Id}",
                    $"{authorName} commented on Ticket #{ticket.Id}.",
                    ticket.Id);
            }
            // If unassigned and creator commented, notify Admins
            else if (!ticket.AssignedToUserId.HasValue && ticket.CreatedByUserId == userId)
            {
                await _notificationService.CreateNotificationsForRoleAsync(
                    "Admin",
                    "NewComment",
                    $"New comment on Unassigned Ticket #{ticket.Id}",
                    $"{authorName} commented on unassigned Ticket #{ticket.Id}.",
                    ticket.Id,
                    excludeUserId: userId);
            }
        }

        return CreatedAtAction(
            nameof(GetComments),
            new { ticketId },
            new
            {
                comment.Id,
                comment.TicketId,
                comment.UserId,
                Author = authorName,
                comment.Content,
                comment.CreatedAt
            });
    }
}