namespace ResolveIQ.Api.DTOs;

public class EscalationEventResponse
{
    public int TicketId { get; set; }
    public string TicketTitle { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? AssignedToUserId { get; set; }
    public string? AssignedToName { get; set; }
    public DateTime EscalatedAt { get; set; }
    public string Reason { get; set; } = string.Empty;
}
