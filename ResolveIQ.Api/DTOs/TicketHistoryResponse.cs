namespace ResolveIQ.Api.DTOs;

public class TicketHistoryResponse
{
    public int Id { get; set; }

    public int TicketId { get; set; }

    public int? UserId { get; set; }

    public string ActorName { get; set; } = string.Empty;

    public string ActorRole { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public DateTime CreatedAt { get; set; }
}
