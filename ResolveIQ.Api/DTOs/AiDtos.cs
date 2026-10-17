using System.ComponentModel.DataAnnotations;

namespace ResolveIQ.Api.DTOs;

public class ClassifyTicketRequest
{
    public int? TicketId { get; set; }

    [StringLength(200)]
    public string? Title { get; set; }

    [StringLength(4000)]
    public string? Description { get; set; }
}

public class TicketClassificationResult
{
    public string Category { get; set; } = "General";
    public string? Subcategory { get; set; }
    public string SuggestedPriority { get; set; } = "Medium";
    public double? ConfidenceScore { get; set; }
    public bool IsConfidenceCalibrated { get; set; } = false;
    public string? ConfidenceCalibrationNote { get; set; } = "Model-reported heuristic confidence; not statistically calibrated.";
    public string Rationale { get; set; } = string.Empty;
    public List<string> SuggestedTags { get; set; } = new();
}

public class RecommendPriorityRequest
{
    public int? TicketId { get; set; }

    [StringLength(200)]
    public string? Title { get; set; }

    [StringLength(4000)]
    public string? Description { get; set; }

    [StringLength(50)]
    public string? Impact { get; set; }

    [StringLength(50)]
    public string? Urgency { get; set; }
}

public class PriorityRecommendationResult
{
    public string? CurrentPriority { get; set; }
    public string RecommendedPriority { get; set; } = "Medium";
    public double? ConfidenceScore { get; set; }
    public bool IsConfidenceCalibrated { get; set; } = false;
    public string? ConfidenceCalibrationNote { get; set; } = "Model-reported heuristic confidence; not statistically calibrated.";
    public string Rationale { get; set; } = string.Empty;
    public string? UncertaintyOrMissingInfo { get; set; }
    public string? SlaFirstResponseTarget { get; set; }
    public string? SlaResolutionTarget { get; set; }
}

public class TroubleshootingResult
{
    public int TicketId { get; set; }
    public string TicketTitle { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> HypothesizedCauses { get; set; } = new();
    public List<string> TroubleshootingSteps { get; set; } = new();
    public List<string> ClarifyingQuestions { get; set; } = new();
    public List<KnowledgeArticleSummaryDto> RelevantKnowledgeArticles { get; set; } = new();
    public List<SimilarTicketSummaryDto> SimilarResolvedTickets { get; set; } = new();
    public string GroundingNotice { get; set; } = string.Empty;
}

public class SimilarTicketSummaryDto
{
    public int TicketId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Priority { get; set; } = "Medium";
    public DateTime? ResolvedAt { get; set; }
    public string Excerpt { get; set; } = string.Empty;
}
