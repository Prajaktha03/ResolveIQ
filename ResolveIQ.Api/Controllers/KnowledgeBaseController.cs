using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Models;

namespace ResolveIQ.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("general")]
public class KnowledgeBaseController : ControllerBase
{
    private readonly AppDbContext _db;

    public KnowledgeBaseController(AppDbContext db)
    {
        _db = db;
    }

    private int? CurrentUserId
    {
        get
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(id, out var userId) ? userId : null;
        }
    }

    // GET: api/KnowledgeBase?search=...&category=...&page=1&pageSize=10
    [HttpGet]
    public async Task<IActionResult> GetArticles(
        [FromQuery] string? search = null,
        [FromQuery] string? category = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _db.KnowledgeArticles
            .AsNoTracking()
            .Include(a => a.AuthorUser)
            .Where(a => a.IsPublished);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(a =>
                a.Title.ToLower().Contains(term) ||
                a.Content.ToLower().Contains(term) ||
                (a.Tags != null && a.Tags.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var cat = category.Trim().ToLower();
            query = query.Where(a => a.Category.ToLower() == cat);
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new KnowledgeArticleResponse
            {
                Id = a.Id,
                Title = a.Title,
                Content = a.Content,
                Category = a.Category,
                Tags = a.Tags,
                IsPublished = a.IsPublished,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt,
                AuthorUserId = a.AuthorUserId,
                AuthorName = a.AuthorUser != null ? a.AuthorUser.FullName : "System"
            })
            .ToListAsync();

        return Ok(new PagedResult<KnowledgeArticleResponse>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    // GET: api/KnowledgeBase/5
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var article = await _db.KnowledgeArticles
            .AsNoTracking()
            .Include(a => a.AuthorUser)
            .FirstOrDefaultAsync(a => a.Id == id && a.IsPublished);

        if (article is null)
            return NotFound(new { message = "Knowledge article not found." });

        return Ok(new KnowledgeArticleResponse
        {
            Id = article.Id,
            Title = article.Title,
            Content = article.Content,
            Category = article.Category,
            Tags = article.Tags,
            IsPublished = article.IsPublished,
            CreatedAt = article.CreatedAt,
            UpdatedAt = article.UpdatedAt,
            AuthorUserId = article.AuthorUserId,
            AuthorName = article.AuthorUser != null ? article.AuthorUser.FullName : "System"
        });
    }

    // POST: api/KnowledgeBase
    [HttpPost]
    [Authorize(Roles = "Agent,Admin")]
    public async Task<IActionResult> Create([FromBody] CreateKnowledgeArticleRequest request)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var article = new KnowledgeArticle
        {
            Title = request.Title.Trim(),
            Content = request.Content.Trim(),
            Category = request.Category.Trim(),
            Tags = request.Tags?.Trim(),
            IsPublished = request.IsPublished,
            CreatedAt = DateTime.UtcNow,
            AuthorUserId = userId
        };

        _db.KnowledgeArticles.Add(article);
        await _db.SaveChangesAsync();

        var author = await _db.Users.FindAsync(userId);

        return CreatedAtAction(
            nameof(GetById),
            new { id = article.Id },
            new KnowledgeArticleResponse
            {
                Id = article.Id,
                Title = article.Title,
                Content = article.Content,
                Category = article.Category,
                Tags = article.Tags,
                IsPublished = article.IsPublished,
                CreatedAt = article.CreatedAt,
                AuthorUserId = article.AuthorUserId,
                AuthorName = author?.FullName ?? "User"
            });
    }

    // PUT: api/KnowledgeBase/5
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Agent,Admin")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateKnowledgeArticleRequest request)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var article = await _db.KnowledgeArticles.FindAsync(id);
        if (article is null)
            return NotFound(new { message = "Knowledge article not found." });

        article.Title = request.Title.Trim();
        article.Content = request.Content.Trim();
        article.Category = request.Category.Trim();
        article.Tags = request.Tags?.Trim();
        article.IsPublished = request.IsPublished;
        article.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var author = article.AuthorUserId.HasValue
            ? await _db.Users.FindAsync(article.AuthorUserId.Value)
            : null;

        return Ok(new KnowledgeArticleResponse
        {
            Id = article.Id,
            Title = article.Title,
            Content = article.Content,
            Category = article.Category,
            Tags = article.Tags,
            IsPublished = article.IsPublished,
            CreatedAt = article.CreatedAt,
            UpdatedAt = article.UpdatedAt,
            AuthorUserId = article.AuthorUserId,
            AuthorName = author?.FullName ?? "System"
        });
    }

    // DELETE: api/KnowledgeBase/5
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var article = await _db.KnowledgeArticles.FindAsync(id);
        if (article is null)
            return NotFound(new { message = "Knowledge article not found." });

        _db.KnowledgeArticles.Remove(article);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Knowledge article deleted successfully.", id });
    }
}
