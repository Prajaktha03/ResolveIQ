namespace ResolveIQ.Api.DTOs;

public class TicketSlaStatusResponse
{
    public int TicketId { get; set; }

    public DateTime? FirstResponseDueAt { get; set; }
    public DateTime? FirstRespondedAt { get; set; }
    public string FirstResponseStatus { get; set; } = "On Track";

    public DateTime? ResolutionDueAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string ResolutionStatus { get; set; } = "On Track";
}
