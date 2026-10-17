using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Models;
using ResolveIQ.Api.Services;

namespace ResolveIQ.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("ai")]
public class AiController : ControllerBase
{
    private readonly IAiIntelligenceService _aiService;
    private readonly IKnowledgeRetrievalService _retrievalService;
    private readonly AppDbContext _db;
    private readonly ILogger<AiController> _logger;

    public AiController(
        IAiIntelligenceService aiService,
        IKnowledgeRetrievalService retrievalService,
        AppDbContext db,
        ILogger<AiController> logger)
    {
        _aiService = aiService;
        _retrievalService = retrievalService;
        _db = db;
        _logger = logger;
    }

    private int? CurrentUserId
    {
        get
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(id, out var userId) ? userId : null;
        }
    }

    private string CurrentUserRole =>
        User.FindFirstValue(ClaimTypes.Role) ?? "Employee";

    // POST: api/AI/classify-ticket
    [HttpPost("classify-ticket")]
    public async Task<IActionResult> ClassifyTicket(
        [FromBody] ClassifyTicketRequest request,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        string title;
        string description;

        if (request.TicketId.HasValue)
        {
            var ticket = await _db.Tickets
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == request.TicketId.Value, cancellationToken);

            if (ticket is null)
                return NotFound(new { message = "Ticket not found." });

            if (!CanAccessTicket(ticket, userId))
                return Forbid();

            title = !string.IsNullOrWhiteSpace(request.Title) ? request.Title : ticket.Title;
            description = !string.IsNullOrWhiteSpace(request.Description) ? request.Description : ticket.Description;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Description))
            {
                return BadRequest(new { message = "Title and description are required when TicketId is not provided." });
            }

            if (request.Title.Length > 200)
                return BadRequest(new { message = "Title cannot exceed 200 characters." });

            if (request.Description.Length > 4000)
                return BadRequest(new { message = "Description cannot exceed 4000 characters." });

            title = request.Title;
            description = request.Description;
        }

        try
        {
            var result = await _aiService.ClassifyTicketAsync(title, description, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleAiException(ex);
        }
    }

    // POST: api/AI/recommend-priority
    [HttpPost("recommend-priority")]
    public async Task<IActionResult> RecommendPriority(
        [FromBody] RecommendPriorityRequest request,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        string title;
        string description;
        string? currentPriority = null;

        if (request.TicketId.HasValue)
        {
            var ticket = await _db.Tickets
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == request.TicketId.Value, cancellationToken);

            if (ticket is null)
                return NotFound(new { message = "Ticket not found." });

            if (!CanAccessTicket(ticket, userId))
                return Forbid();

            title = !string.IsNullOrWhiteSpace(request.Title) ? request.Title : ticket.Title;
            description = !string.IsNullOrWhiteSpace(request.Description) ? request.Description : ticket.Description;
            currentPriority = ticket.Priority;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Description))
            {
                return BadRequest(new { message = "Title and description are required when TicketId is not provided." });
            }

            if (request.Title.Length > 200)
                return BadRequest(new { message = "Title cannot exceed 200 characters." });

            if (request.Description.Length > 4000)
                return BadRequest(new { message = "Description cannot exceed 4000 characters." });

            title = request.Title;
            description = request.Description;
        }

        try
        {
            var result = await _aiService.RecommendPriorityAsync(
                title,
                description,
                currentPriority,
                request.Impact,
                request.Urgency,
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleAiException(ex);
        }
    }

    // POST: api/AI/troubleshoot/5
    [HttpPost("troubleshoot/{ticketId:int}")]
    public async Task<IActionResult> Troubleshoot(
        int ticketId,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        var ticket = await _db.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == ticketId, cancellationToken);

        if (ticket is null)
            return NotFound(new { message = "Ticket not found." });

        if (!CanAccessTicket(ticket, userId))
            return Forbid();

        var userRole = CurrentUserRole;

        // Grounding retrieval: Fetch relevant knowledge articles
        var queryText = $"{ticket.Title} {ticket.Description}";
        var articles = await _retrievalService.SearchArticlesAsync(queryText, maxResults: 3, cancellationToken);

        // Grounding retrieval: Fetch similar resolved tickets matching caller authorization
        var similarTickets = await _retrievalService.FindSimilarResolvedTicketsAsync(
            ticket.Title,
            userId,
            userRole,
            excludeTicketId: ticket.Id,
            maxResults: 3,
            cancellationToken);

        try
        {
            var result = await _aiService.TroubleshootTicketAsync(
                ticket,
                articles,
                similarTickets,
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleAiException(ex);
        }
    }

    private IActionResult HandleAiException(Exception ex)
    {
        if (ex is AiServiceUnavailableException unavailableEx)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                CreateProblemDetails(503, "AI Service Unavailable", unavailableEx.Message));
        }

        if (ex is TimeoutException timeoutEx)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout,
                CreateProblemDetails(504, "AI Request Timeout", timeoutEx.Message));
        }

        if (ex is AiProviderException providerEx)
        {
            if (providerEx.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                providerEx.Message.Contains("quota", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    CreateProblemDetails(429, "AI Quota / Rate Limit Exceeded", providerEx.Message));
            }

            return StatusCode(StatusCodes.Status502BadGateway,
                CreateProblemDetails(502, "AI Provider Error", providerEx.Message));
        }

        _logger.LogError(ex, "Unexpected error executing AI intelligence operation.");
        return StatusCode(StatusCodes.Status500InternalServerError,
            CreateProblemDetails(500, "Internal Server Error", "An unexpected error occurred while processing your request."));
    }

    private bool CanAccessTicket(Ticket ticket, int userId)
    {
        if (User.IsInRole("Admin"))
            return true;

        if (User.IsInRole("Agent"))
            return ticket.AssignedToUserId == userId;

        return ticket.CreatedByUserId == userId;
    }

    private ProblemDetails CreateProblemDetails(int statusCode, string title, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = HttpContext.Request.Path,
            Type = $"https://httpstatuses.com/{statusCode}"
        };
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return problem;
    }
}
