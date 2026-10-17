using Microsoft.EntityFrameworkCore;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Models;

namespace ResolveIQ.Api.Services;

public class TicketService : ITicketService
{
    private readonly AppDbContext _db;
    private readonly SlaService _slaService;
    private readonly INotificationService? _notificationService;

    public TicketService(
        AppDbContext db,
        SlaService slaService,
        INotificationService? notificationService = null)
    {
        _db = db;
        _slaService = slaService;
        _notificationService = notificationService;
    }

    public async Task<List<Ticket>> GetAllAsync()
    {
        return await _db.Tickets
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<Ticket?> GetByIdAsync(int id)
    {
        return await _db.Tickets.FindAsync(id);
    }

    public async Task<Ticket> CreateAsync(
        CreateTicketRequest request,
        int userId)
    {
        var createdAt = DateTime.UtcNow;

        var deadlines = _slaService.CalculateDeadlines(
            request.Priority,
            createdAt);

        var ticket = new Ticket
        {
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Priority = request.Priority,
            Status = "Open",
            CreatedAt = createdAt,
            CreatedByUserId = userId,

            FirstResponseDueAt = deadlines.FirstResponseDueAt,
            ResolutionDueAt = deadlines.ResolutionDueAt
        };

        // Use transaction where supported (e.g. PostgreSQL) to ensure ticket creation and audit history are atomic
        if (_db.Database.IsRelational())
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                using var tx = await _db.Database.BeginTransactionAsync();
                _db.Tickets.Add(ticket);
                await _db.SaveChangesAsync();

                _db.TicketHistories.Add(new TicketHistory
                {
                    TicketId = ticket.Id,
                    UserId = userId,
                    Action = "Created",
                    OldValue = null,
                    NewValue =
                        $"Status: {ticket.Status}; Priority: {ticket.Priority}",
                    CreatedAt = createdAt
                });

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            });
        }
        else
        {
            _db.Tickets.Add(ticket);
            await _db.SaveChangesAsync();

            _db.TicketHistories.Add(new TicketHistory
            {
                TicketId = ticket.Id,
                UserId = userId,
                Action = "Created",
                OldValue = null,
                NewValue =
                    $"Status: {ticket.Status}; Priority: {ticket.Priority}",
                CreatedAt = createdAt
            });

            await _db.SaveChangesAsync();
        }

        // Notify Admins of new ticket creation (excluding creator)
        if (_notificationService != null)
        {
            await _notificationService.CreateNotificationsForRoleAsync(
                "Admin",
                "TicketCreated",
                $"New Ticket #{ticket.Id}: {ticket.Title}",
                $"Ticket #{ticket.Id} created with priority {ticket.Priority}.",
                ticket.Id,
                deduplicationKeyPrefix: $"ticket_created_{ticket.Id}",
                excludeUserId: userId);
        }

        return ticket;
    }

    public async Task<Ticket?> UpdateAsync(
        int id,
        UpdateTicketRequest request,
        int userId)
    {
        var ticket = await _db.Tickets.FindAsync(id);

        if (ticket is null)
            return null;

        var oldStatus = ticket.Status;
        var oldPriority = ticket.Priority;
        var now = DateTime.UtcNow;

        var statusChanged = oldStatus != request.Status;
        var priorityChanged = oldPriority != request.Priority;

        // Record status changes.
        if (statusChanged)
        {
            _db.TicketHistories.Add(new TicketHistory
            {
                TicketId = ticket.Id,
                UserId = userId,
                Action = "StatusChanged",
                OldValue = oldStatus,
                NewValue = request.Status,
                CreatedAt = now
            });

            if (request.Status == "Resolved" &&
                oldStatus != "Resolved")
            {
                ticket.ResolvedAt = now;
            }
            else if (oldStatus == "Resolved" &&
                     request.Status != "Resolved")
            {
                ticket.ResolvedAt = null;
            }
        }

        // Record priority changes and recalculate SLA deadlines.
        if (priorityChanged)
        {
            _db.TicketHistories.Add(new TicketHistory
            {
                TicketId = ticket.Id,
                UserId = userId,
                Action = "PriorityChanged",
                OldValue = oldPriority,
                NewValue = request.Priority,
                CreatedAt = now
            });

            var deadlines = _slaService.CalculateDeadlines(
                request.Priority,
                ticket.CreatedAt);

            ticket.FirstResponseDueAt = deadlines.FirstResponseDueAt;
            ticket.ResolutionDueAt = deadlines.ResolutionDueAt;
        }

        ticket.Status = request.Status;
        ticket.Priority = request.Priority;

        await _db.SaveChangesAsync();

        // Milestone 2 notifications for status and priority changes
        if (_notificationService != null)
        {
            var recipients = new List<int>();
            if (ticket.CreatedByUserId.HasValue && ticket.CreatedByUserId.Value != userId)
                recipients.Add(ticket.CreatedByUserId.Value);
            if (ticket.AssignedToUserId.HasValue && ticket.AssignedToUserId.Value != userId)
                recipients.Add(ticket.AssignedToUserId.Value);

            if (statusChanged)
            {
                await _notificationService.CreateNotificationsForUsersAsync(
                    recipients,
                    "StatusChanged",
                    $"Ticket #{ticket.Id} Status Updated",
                    $"Status of Ticket #{ticket.Id} changed from {oldStatus} to {request.Status}.",
                    ticket.Id,
                    excludeUserId: userId);
            }

            if (priorityChanged)
            {
                await _notificationService.CreateNotificationsForUsersAsync(
                    recipients,
                    "PriorityChanged",
                    $"Ticket #{ticket.Id} Priority Updated",
                    $"Priority of Ticket #{ticket.Id} changed from {oldPriority} to {request.Priority}.",
                    ticket.Id,
                    excludeUserId: userId);
            }
        }

        return ticket;
    }
}