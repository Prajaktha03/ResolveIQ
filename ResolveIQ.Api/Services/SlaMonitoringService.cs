using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.Models;

namespace ResolveIQ.Api.Services;

public class SlaMonitoringService : ISlaMonitoringService
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SlaMonitoringService> _logger;
    private readonly INotificationService? _notificationService;
    private readonly IConfiguration? _configuration;

    public SlaMonitoringService(
        AppDbContext db,
        TimeProvider? timeProvider = null,
        ILogger<SlaMonitoringService>? logger = null,
        INotificationService? notificationService = null,
        IConfiguration? configuration = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<SlaMonitoringService>.Instance;
        _notificationService = notificationService;
        _configuration = configuration;
    }

    public async Task<int> EvaluateDeadlinesAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var breachEventsRecorded = 0;

        // Configurable warning window (default 15 minutes) and escalation delay (default 30 minutes)
        var warningMinutes = _configuration?.GetValue<int>("SlaMonitoring:WarningWindowMinutes", 15) ?? 15;
        var warningWindow = TimeSpan.FromMinutes(Math.Max(1, warningMinutes));

        var escalationMinutes = _configuration?.GetValue<int>("SlaMonitoring:EscalationDelayMinutes", 30) ?? 30;
        var escalationDelay = TimeSpan.FromMinutes(Math.Max(0, escalationMinutes));

        // Query active (unresolved) tickets
        var activeTickets = await _db.Tickets
            .Where(t => t.Status != "Resolved")
            .ToListAsync(cancellationToken);

        // Preload admin IDs for notifications
        List<int>? adminIds = null;
        if (_notificationService != null)
        {
            adminIds = await _db.Users
                .Where(u => u.Role == "Admin")
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);
        }

        foreach (var ticket in activeTickets)
        {
            // -------------------------------------------------------------
            // 1. Evaluate First Response Deadline
            // -------------------------------------------------------------
            if (ticket.FirstRespondedAt == null && ticket.FirstResponseDueAt.HasValue)
            {
                var due = ticket.FirstResponseDueAt.Value;

                if (now > due)
                {
                    // BREACHED
                    var alreadyLogged = await _db.TicketHistories
                        .AnyAsync(h => h.TicketId == ticket.Id && h.Action == "FirstResponseBreached", cancellationToken);

                    if (!alreadyLogged)
                    {
                        _db.TicketHistories.Add(new TicketHistory
                        {
                            TicketId = ticket.Id,
                            UserId = null, // System-triggered SLA breach event
                            Action = "FirstResponseBreached",
                            OldValue = due.ToString("o"),
                            NewValue = $"First response deadline of {due:o} was breached.",
                            CreatedAt = now
                        });
                        breachEventsRecorded++;
                    }

                    // Send breach notification (idempotent via deduplication key)
                    if (_notificationService != null && adminIds != null)
                    {
                        var breachRecipients = new List<int>(adminIds);
                        if (ticket.AssignedToUserId.HasValue)
                        {
                            breachRecipients.Add(ticket.AssignedToUserId.Value);
                        }

                        await _notificationService.CreateNotificationsForUsersAsync(
                            breachRecipients,
                            "SlaBreached",
                            $"[SLA BREACHED] First Response Missed for Ticket #{ticket.Id}",
                            $"Ticket #{ticket.Id} ({ticket.Priority}) missed first response deadline of {due:g} UTC.",
                            ticket.Id,
                            deduplicationKeyPrefix: $"sla_breached_first_response_{ticket.Id}",
                            cancellationToken: cancellationToken);
                    }

                    // Milestone 4: Escalation rule for Critical/High tickets remaining unresponded past escalation delay
                    if ((ticket.Priority == "Critical" || ticket.Priority == "High") && now >= due.Add(escalationDelay))
                    {
                        var alreadyEscalated = await _db.TicketHistories
                            .AnyAsync(h => h.TicketId == ticket.Id && h.Action == "Escalated" && h.NewValue != null && h.NewValue.Contains("FirstResponse"), cancellationToken);

                        if (!alreadyEscalated)
                        {
                            _db.TicketHistories.Add(new TicketHistory
                            {
                                TicketId = ticket.Id,
                                UserId = null, // System actor convention compatible with foreign key
                                Action = "Escalated",
                                OldValue = ticket.Priority,
                                NewValue = $"Escalated to Admins due to unaddressed FirstResponse SLA breach on {ticket.Priority} ticket.",
                                CreatedAt = now
                            });

                            if (_notificationService != null && adminIds != null)
                            {
                                await _notificationService.CreateNotificationsForUsersAsync(
                                    adminIds,
                                    "Escalated",
                                    $"[ESCALATION] Ticket #{ticket.Id} ({ticket.Priority}) First Response Breach",
                                    $"Ticket #{ticket.Id} ({ticket.Priority}) has been escalated to Admins due to unaddressed first response SLA breach.",
                                    ticket.Id,
                                    deduplicationKeyPrefix: $"escalation_{ticket.Id}_first_response",
                                    cancellationToken: cancellationToken);
                            }
                        }
                    }
                }
                else if (due - now <= warningWindow)
                {
                    // AT RISK (WARNING)
                    if (_notificationService != null && adminIds != null)
                    {
                        var warnRecipients = new List<int>(adminIds);
                        if (ticket.AssignedToUserId.HasValue)
                        {
                            warnRecipients.Add(ticket.AssignedToUserId.Value);
                        }

                        await _notificationService.CreateNotificationsForUsersAsync(
                            warnRecipients,
                            "SlaAtRisk",
                            $"[SLA AT RISK] First Response Due Soon for Ticket #{ticket.Id}",
                            $"Ticket #{ticket.Id} ({ticket.Priority}) first response deadline is at {due:g} UTC (within {warningMinutes} minutes).",
                            ticket.Id,
                            deduplicationKeyPrefix: $"sla_warning_first_response_{ticket.Id}",
                            cancellationToken: cancellationToken);
                    }
                }
            }

            // -------------------------------------------------------------
            // 2. Evaluate Resolution Deadline
            // -------------------------------------------------------------
            if (ticket.ResolvedAt == null && ticket.ResolutionDueAt.HasValue)
            {
                var due = ticket.ResolutionDueAt.Value;

                if (now > due)
                {
                    // BREACHED
                    var alreadyLogged = await _db.TicketHistories
                        .AnyAsync(h => h.TicketId == ticket.Id && h.Action == "ResolutionBreached", cancellationToken);

                    if (!alreadyLogged)
                    {
                        _db.TicketHistories.Add(new TicketHistory
                        {
                            TicketId = ticket.Id,
                            UserId = null, // System-triggered SLA breach event
                            Action = "ResolutionBreached",
                            OldValue = due.ToString("o"),
                            NewValue = $"Resolution deadline of {due:o} was breached.",
                            CreatedAt = now
                        });
                        breachEventsRecorded++;
                    }

                    // Send breach notification (idempotent via deduplication key)
                    if (_notificationService != null && adminIds != null)
                    {
                        var breachRecipients = new List<int>(adminIds);
                        if (ticket.AssignedToUserId.HasValue)
                        {
                            breachRecipients.Add(ticket.AssignedToUserId.Value);
                        }

                        await _notificationService.CreateNotificationsForUsersAsync(
                            breachRecipients,
                            "SlaBreached",
                            $"[SLA BREACHED] Resolution Missed for Ticket #{ticket.Id}",
                            $"Ticket #{ticket.Id} ({ticket.Priority}) missed resolution deadline of {due:g} UTC.",
                            ticket.Id,
                            deduplicationKeyPrefix: $"sla_breached_resolution_{ticket.Id}",
                            cancellationToken: cancellationToken);
                    }

                    // Milestone 4: Escalation rule for Critical/High tickets remaining unresolved past escalation delay
                    if ((ticket.Priority == "Critical" || ticket.Priority == "High") && now >= due.Add(escalationDelay))
                    {
                        var alreadyEscalated = await _db.TicketHistories
                            .AnyAsync(h => h.TicketId == ticket.Id && h.Action == "Escalated" && h.NewValue != null && h.NewValue.Contains("Resolution"), cancellationToken);

                        if (!alreadyEscalated)
                        {
                            _db.TicketHistories.Add(new TicketHistory
                            {
                                TicketId = ticket.Id,
                                UserId = null, // System actor convention compatible with foreign key
                                Action = "Escalated",
                                OldValue = ticket.Priority,
                                NewValue = $"Escalated to Admins due to unaddressed Resolution SLA breach on {ticket.Priority} ticket.",
                                CreatedAt = now
                            });

                            if (_notificationService != null && adminIds != null)
                            {
                                await _notificationService.CreateNotificationsForUsersAsync(
                                    adminIds,
                                    "Escalated",
                                    $"[ESCALATION] Ticket #{ticket.Id} ({ticket.Priority}) Resolution Breach",
                                    $"Ticket #{ticket.Id} ({ticket.Priority}) has been escalated to Admins due to unaddressed resolution SLA breach.",
                                    ticket.Id,
                                    deduplicationKeyPrefix: $"escalation_{ticket.Id}_resolution",
                                    cancellationToken: cancellationToken);
                            }
                        }
                    }
                }
                else if (due - now <= warningWindow)
                {
                    // AT RISK (WARNING)
                    if (_notificationService != null && adminIds != null)
                    {
                        var warnRecipients = new List<int>(adminIds);
                        if (ticket.AssignedToUserId.HasValue)
                        {
                            warnRecipients.Add(ticket.AssignedToUserId.Value);
                        }

                        await _notificationService.CreateNotificationsForUsersAsync(
                            warnRecipients,
                            "SlaAtRisk",
                            $"[SLA AT RISK] Resolution Due Soon for Ticket #{ticket.Id}",
                            $"Ticket #{ticket.Id} ({ticket.Priority}) resolution deadline is at {due:g} UTC (within {warningMinutes} minutes).",
                            ticket.Id,
                            deduplicationKeyPrefix: $"sla_warning_resolution_{ticket.Id}",
                            cancellationToken: cancellationToken);
                    }
                }
            }
        }

        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(cancellationToken);
            if (breachEventsRecorded > 0)
            {
                _logger.LogInformation("Recorded {Count} SLA breach event(s).", breachEventsRecorded);
            }
        }

        return breachEventsRecorded;
    }
}
