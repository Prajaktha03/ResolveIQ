namespace ResolveIQ.Api.Models;

public class Notification
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    // Types: TicketCreated, TicketAssigned, NewComment, StatusChanged, PriorityChanged, SlaAtRisk, SlaBreached, Escalated
    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public int? TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsRead { get; set; } = false;

    public DateTime? ReadAt { get; set; }

    // Unique deduplication key to guarantee idempotent notifications across retries & polling loops
    public string? DeduplicationKey { get; set; }
}
