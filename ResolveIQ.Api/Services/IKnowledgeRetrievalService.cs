using ResolveIQ.Api.DTOs;

namespace ResolveIQ.Api.Services;

public interface IKnowledgeRetrievalService
{
    Task<List<KnowledgeArticleSummaryDto>> SearchArticlesAsync(string query, int maxResults = 3, CancellationToken cancellationToken = default);

    Task<List<SimilarTicketSummaryDto>> FindSimilarResolvedTicketsAsync(
        string query,
        int currentUserId,
        string userRole,
        int? excludeTicketId = null,
        int maxResults = 3,
        CancellationToken cancellationToken = default);
}
