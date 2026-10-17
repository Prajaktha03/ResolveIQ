using System.ComponentModel.DataAnnotations;

namespace ResolveIQ.Api.DTOs;

public class CreateKnowledgeArticleRequest
{
    [Required]
    [StringLength(200, MinimumLength = 3)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(8000, MinimumLength = 10)]
    public string Content { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Category { get; set; } = "General";

    [StringLength(200)]
    public string? Tags { get; set; }

    public bool IsPublished { get; set; } = true;
}

public class UpdateKnowledgeArticleRequest
{
    [Required]
    [StringLength(200, MinimumLength = 3)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(8000, MinimumLength = 10)]
    public string Content { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Category { get; set; } = "General";

    [StringLength(200)]
    public string? Tags { get; set; }

    public bool IsPublished { get; set; } = true;
}

public class KnowledgeArticleResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Tags { get; set; }
    public bool IsPublished { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? AuthorUserId { get; set; }
    public string? AuthorName { get; set; }
}

public class KnowledgeArticleSummaryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public string? Tags { get; set; }
}
