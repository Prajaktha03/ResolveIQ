
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Models;

namespace ResolveIQ.Api.Services;

public class SlaService
{
    private readonly TimeProvider _timeProvider;

    public SlaService(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public (TimeSpan FirstResponse, TimeSpan Resolution)
        GetTargets(string priority) =>
        priority switch
        {
            "Critical" => (TimeSpan.FromMinutes(15), TimeSpan.FromHours(4)),
            "High" => (TimeSpan.FromHours(1), TimeSpan.FromHours(8)),
            "Medium" => (TimeSpan.FromHours(4), TimeSpan.FromHours(24)),
            "Low" => (TimeSpan.FromHours(8), TimeSpan.FromHours(72)),
            _ => throw new ArgumentException(
                "Invalid ticket priority.", nameof(priority))
        };

    public (DateTime FirstResponseDueAt, DateTime ResolutionDueAt)
        CalculateDeadlines(string priority, DateTime createdAt)
    {
        var targets = GetTargets(priority);

        return (
            createdAt.Add(targets.FirstResponse),
            createdAt.Add(targets.Resolution)
        );
    }

    public TicketSlaStatusResponse GetStatus(Ticket ticket)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        return new TicketSlaStatusResponse
        {
            TicketId = ticket.Id,
            FirstResponseDueAt = ticket.FirstResponseDueAt,
            FirstRespondedAt = ticket.FirstRespondedAt,
            FirstResponseStatus = EvaluateFirstResponse(ticket, now),

            ResolutionDueAt = ticket.ResolutionDueAt,
            ResolvedAt = ticket.ResolvedAt,
            ResolutionStatus = EvaluateResolution(ticket, now)
        };
    }

    public static string EvaluateFirstResponse(Ticket ticket, DateTime now)
    {
        if (ticket.FirstResponseDueAt is null)
            return "Unavailable";

        if (ticket.FirstRespondedAt.HasValue)
        {
            return ticket.FirstRespondedAt.Value <= ticket.FirstResponseDueAt.Value
                ? "Met"
                : "Breached";
        }

        // A ticket resolved without a recorded first response was closed without support
        // having ever provided a first response. It must not be marked as Met, At Risk, or On Track.
        if (ticket.Status == "Resolved" || ticket.ResolvedAt.HasValue)
        {
            return "Breached";
        }

        if (now > ticket.FirstResponseDueAt.Value)
            return "Breached";

        if (ticket.FirstResponseDueAt.Value - now <= TimeSpan.FromHours(1))
            return "At Risk";

        return "On Track";
    }

    public static string EvaluateResolution(Ticket ticket, DateTime now)
    {
        if (ticket.ResolutionDueAt is null)
            return "Unavailable";

        if (ticket.ResolvedAt.HasValue)
        {
            return ticket.ResolvedAt.Value <= ticket.ResolutionDueAt.Value
                ? "Met"
                : "Breached";
        }

        // If status is Resolved but ResolvedAt was not explicitly recorded,
        // it is resolved; if no timestamp, fallback check.
        if (ticket.Status == "Resolved")
        {
            return "Met";
        }

        if (now > ticket.ResolutionDueAt.Value)
            return "Breached";

        if (ticket.ResolutionDueAt.Value - now <= TimeSpan.FromHours(1))
            return "At Risk";

        return "On Track";
    }
}

