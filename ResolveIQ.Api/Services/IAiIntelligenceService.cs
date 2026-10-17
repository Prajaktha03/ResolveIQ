using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Models;

namespace ResolveIQ.Api.Services;

public interface IAiIntelligenceService
{
    bool IsConfigured { get; }

    Task<TicketClassificationResult> ClassifyTicketAsync(
        string title,
        string description,
        CancellationToken cancellationToken = default);

    Task<PriorityRecommendationResult> RecommendPriorityAsync(
        string title,
        string description,
        string? currentPriority,
        string? impact,
        string? urgency,
        CancellationToken cancellationToken = default);

    Task<TroubleshootingResult> TroubleshootTicketAsync(
        Ticket ticket,
        List<KnowledgeArticleSummaryDto> knowledgeArticles,
        List<SimilarTicketSummaryDto> similarTickets,
        CancellationToken cancellationToken = default);
}

public class AiServiceUnavailableException : Exception
{
    public AiServiceUnavailableException(string message) : base(message) { }
    public AiServiceUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}

public class AiProviderException : Exception
{
    public AiProviderException(string message) : base(message) { }
    public AiProviderException(string message, Exception innerException) : base(message, innerException) { }
}
