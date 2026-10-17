using Microsoft.EntityFrameworkCore;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.DTOs;

namespace ResolveIQ.Api.Services;

public class KnowledgeRetrievalService : IKnowledgeRetrievalService
{
    private readonly AppDbContext _db;
    private readonly ILogger<KnowledgeRetrievalService> _logger;

    public KnowledgeRetrievalService(AppDbContext db, ILogger<KnowledgeRetrievalService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<KnowledgeArticleSummaryDto>> SearchArticlesAsync(
        string query,
        int maxResults = 3,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var terms = ExtractSearchTerms(query);
        if (terms.Length == 0)
            return [];

        var articlesQuery = _db.KnowledgeArticles
            .AsNoTracking()
            .Where(a => a.IsPublished);

        // Fetch candidate articles matching any of the top terms
        var primaryTerm = terms[0];
        var candidates = await articlesQuery
            .Where(a =>
                a.Title.ToLower().Contains(primaryTerm) ||
                a.Category.ToLower().Contains(primaryTerm) ||
                (a.Tags != null && a.Tags.ToLower().Contains(primaryTerm)) ||
                a.Content.ToLower().Contains(primaryTerm))
            .OrderByDescending(a => a.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        // Score candidates based on term frequency across title, tags, and content
        var scored = candidates
            .Select(a =>
            {
                var titleLower = a.Title.ToLowerInvariant();
                var tagsLower = a.Tags?.ToLowerInvariant() ?? string.Empty;
                var categoryLower = a.Category.ToLowerInvariant();
                var contentLower = a.Content.ToLowerInvariant();

                int score = 0;
                foreach (var term in terms)
                {
                    if (titleLower.Contains(term)) score += 5;
                    if (tagsLower.Contains(term)) score += 4;
                    if (categoryLower.Contains(term)) score += 3;
                    if (contentLower.Contains(term)) score += 1;
                }
                return new { Article = a, Score = score };
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Article.CreatedAt)
            .Take(maxResults)
            .Select(x => new KnowledgeArticleSummaryDto
            {
                Id = x.Article.Id,
                Title = x.Article.Title,
                Category = x.Article.Category,
                Excerpt = GetExcerpt(x.Article.Content, 200),
                Tags = x.Article.Tags
            })
            .ToList();

        return scored;
    }

    public async Task<List<SimilarTicketSummaryDto>> FindSimilarResolvedTicketsAsync(
        string query,
        int currentUserId,
        string userRole,
        int? excludeTicketId = null,
        int maxResults = 3,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var terms = ExtractSearchTerms(query);
        if (terms.Length == 0)
            return [];

        var ticketsQuery = _db.Tickets
            .AsNoTracking()
            .Where(t => t.Status == "Resolved");

        if (excludeTicketId.HasValue)
        {
            ticketsQuery = ticketsQuery.Where(t => t.Id != excludeTicketId.Value);
        }

        // Enforce strict object-level authorization on retrieval:
        // Employees can only retrieve their own resolved tickets
        // Agents can retrieve tickets assigned to them
        // Admins can retrieve all resolved tickets
        if (userRole == "Admin")
        {
            // Admins can see all resolved tickets
        }
        else if (userRole == "Agent")
        {
            ticketsQuery = ticketsQuery.Where(t => t.AssignedToUserId == currentUserId);
        }
        else
        {
            ticketsQuery = ticketsQuery.Where(t => t.CreatedByUserId == currentUserId);
        }

        var primaryTerm = terms[0];
        var candidates = await ticketsQuery
            .Where(t =>
                t.Title.ToLower().Contains(primaryTerm) ||
                t.Description.ToLower().Contains(primaryTerm))
            .OrderByDescending(t => t.ResolvedAt ?? t.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        var scored = candidates
            .Select(t =>
            {
                var titleLower = t.Title.ToLowerInvariant();
                var descLower = t.Description.ToLowerInvariant();

                int score = 0;
                foreach (var term in terms)
                {
                    if (titleLower.Contains(term)) score += 4;
                    if (descLower.Contains(term)) score += 1;
                }
                return new { Ticket = t, Score = score };
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Ticket.ResolvedAt ?? x.Ticket.CreatedAt)
            .Take(maxResults)
            .Select(x => new SimilarTicketSummaryDto
            {
                TicketId = x.Ticket.Id,
                Title = x.Ticket.Title,
                Priority = x.Ticket.Priority,
                ResolvedAt = x.Ticket.ResolvedAt,
                Excerpt = GetExcerpt(x.Ticket.Description, 150)
            })
            .ToList();

        return scored;
    }

    private static string[] ExtractSearchTerms(string query)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "is", "are", "was", "were", "to", "for", "in", "on", "at",
            "and", "or", "of", "it", "with", "as", "by", "this", "that", "my", "your", "ticket", "issue"
        };

        var words = query
            .Split([' ', ',', '.', '!', '?', ';', ':', '-', '(', ')', '[', ']', '/', '\\'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => w.ToLowerInvariant())
            .Where(w => w.Length >= 3 && !stopWords.Contains(w))
            .Distinct()
            .Take(6)
            .ToArray();

        return words;
    }

    private static string GetExcerpt(string content, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var cleaned = content.Replace("\r\n", " ").Replace('\n', ' ').Trim();
        if (cleaned.Length <= maxLength)
            return cleaned;

        return cleaned[..maxLength] + "...";
    }
}
