namespace ResolveIQ.Api.Models;

public class Ticket
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Priority { get; set; } = "Medium";

    public string Status { get; set; } = "Open";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Ticket ownership
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public int? AssignedToUserId { get; set; }
    public User? AssignedToUser { get; set; }

    // SLA deadlines
    public DateTime? FirstResponseDueAt { get; set; }

    public DateTime? ResolutionDueAt { get; set; }

    // SLA tracking
    public DateTime? FirstRespondedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }
}