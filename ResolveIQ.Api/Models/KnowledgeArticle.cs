namespace ResolveIQ.Api.Models;

public class KnowledgeArticle
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string Category { get; set; } = "General";

    public string? Tags { get; set; }

    public bool IsPublished { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public int? AuthorUserId { get; set; }
    public User? AuthorUser { get; set; }
}
