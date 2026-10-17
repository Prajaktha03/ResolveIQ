namespace ResolveIQ.Api.DTOs;

public class TicketDashboardResponse
{
    /// <summary>Total tickets within the user's authorized scope.</summary>
    public int TotalTickets { get; set; }

    /// <summary>Tickets with 'Open' status within scope.</summary>
    public int OpenTickets { get; set; }

    /// <summary>Tickets with 'InProgress' status within scope.</summary>
    public int InProgressTickets { get; set; }

    /// <summary>Tickets with 'Resolved' status within scope.</summary>
    public int ResolvedTickets { get; set; }

    /// <summary>
    /// Current open or in-progress tickets where at least one SLA milestone is at risk of breaching (&lt;= 1 hour remaining).
    /// Resolved tickets with completed checks are never counted as at risk.
    /// </summary>
    public int AtRiskTickets { get; set; }

    /// <summary>
    /// Total unique tickets within scope that have breached at least one SLA milestone (first-response or resolution).
    /// Counted uniquely per ticket even when both milestones breached.
    /// </summary>
    public int BreachedTickets { get; set; }

    /// <summary>Total individual SLA milestone checks that were completed within deadline.</summary>
    public int SlaChecksMet { get; set; }

    /// <summary>Total individual SLA milestone checks that breached their deadline.</summary>
    public int SlaChecksBreached { get; set; }

    /// <summary>
    /// SLA compliance percentage: (SlaChecksMet / (SlaChecksMet + SlaChecksBreached)) * 100.
    /// Pending and unavailable milestones are excluded from the denominator. Safely returns 0 when no checks completed.
    /// </summary>
    public double SlaCompliancePercentage { get; set; }
}
