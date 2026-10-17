
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Models;
using ResolveIQ.Api.Services;

namespace ResolveIQ.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("general")]
public class TicketsController : ControllerBase
{
    private readonly ITicketService _service;
    private readonly AppDbContext _db;
    private readonly SlaService _slaService;
    private readonly INotificationService? _notificationService;

    public TicketsController(
        ITicketService service,
        AppDbContext db,
        SlaService slaService,
        INotificationService? notificationService = null)
    {
        _service = service;
        _db = db;
        _slaService = slaService;
        _notificationService = notificationService;
    }

    private int? CurrentUserId
    {
        get
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(id, out var userId) ? userId : null;
        }
    }

    private bool IsAgentOrAdmin =>
        User.IsInRole("Agent") || User.IsInRole("Admin");

    // GET: api/Tickets
    // Consistent access policy with search, filtering, sorting, and pagination:
    // Employees see their own tickets.
    // Agents see tickets assigned to them.
    // Admins see all tickets.
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] TicketQueryParameters? parameters = null)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        parameters ??= new TicketQueryParameters();

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var query = _db.Tickets.AsNoTracking();

        if (User.IsInRole("Admin"))
        {
            // Admins see all tickets
        }
        else if (User.IsInRole("Agent"))
        {
            query = query.Where(t => t.AssignedToUserId == userId);
        }
        else
        {
            query = query.Where(t => t.CreatedByUserId == userId);
        }

        // 1. Search by title, description, and ticket identifier
        if (!string.IsNullOrWhiteSpace(parameters.Search))
        {
            var term = parameters.Search.Trim().ToLower();
            var hasId = int.TryParse(term, out var searchId);

            query = query.Where(t =>
                t.Title.ToLower().Contains(term) ||
                t.Description.ToLower().Contains(term) ||
                (hasId && t.Id == searchId));
        }

        // 2. Filters
        if (!string.IsNullOrWhiteSpace(parameters.Status))
        {
            query = query.Where(t => t.Status == parameters.Status);
        }

        if (!string.IsNullOrWhiteSpace(parameters.Priority))
        {
            query = query.Where(t => t.Priority == parameters.Priority);
        }

        if (parameters.AssigneeId.HasValue)
        {
            query = query.Where(t => t.AssignedToUserId == parameters.AssigneeId.Value);
        }

        if (parameters.CreatorId.HasValue)
        {
            query = query.Where(t => t.CreatedByUserId == parameters.CreatorId.Value);
        }

        if (parameters.CreatedAtFrom.HasValue)
        {
            query = query.Where(t => t.CreatedAt >= parameters.CreatedAtFrom.Value);
        }

        if (parameters.CreatedAtTo.HasValue)
        {
            query = query.Where(t => t.CreatedAt <= parameters.CreatedAtTo.Value);
        }

        var totalCount = await query.CountAsync();

        // 3. Sorting with deterministic secondary ordering
        var isDesc = string.Equals(parameters.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        query = (parameters.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "title" => isDesc
                ? query.OrderByDescending(t => t.Title).ThenByDescending(t => t.Id)
                : query.OrderBy(t => t.Title).ThenBy(t => t.Id),

            "priority" => isDesc
                ? query.OrderByDescending(t => t.Priority).ThenByDescending(t => t.Id)
                : query.OrderBy(t => t.Priority).ThenBy(t => t.Id),

            "status" => isDesc
                ? query.OrderByDescending(t => t.Status).ThenByDescending(t => t.Id)
                : query.OrderBy(t => t.Status).ThenBy(t => t.Id),

            "id" => isDesc
                ? query.OrderByDescending(t => t.Id)
                : query.OrderBy(t => t.Id),

            _ => isDesc
                ? query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                : query.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
        };

        // 4. Pagination
        var items = await query
            .Skip((parameters.Page - 1) * parameters.PageSize)
            .Take(parameters.PageSize)
            .ToListAsync();

        return Ok(new PagedResult<Ticket>
        {
            Items = items,
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        });
    }

    // GET: api/Tickets/assigned
    [HttpGet("assigned")]
    [Authorize(Roles = "Agent,Admin")]
    public async Task<IActionResult> GetAssignedTickets([FromQuery] TicketQueryParameters? parameters = null)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        parameters ??= new TicketQueryParameters();

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var query = _db.Tickets
            .AsNoTracking()
            .Where(t => t.AssignedToUserId == userId);

        if (!string.IsNullOrWhiteSpace(parameters.Search))
        {
            var term = parameters.Search.Trim().ToLower();
            var hasId = int.TryParse(term, out var searchId);

            query = query.Where(t =>
                t.Title.ToLower().Contains(term) ||
                t.Description.ToLower().Contains(term) ||
                (hasId && t.Id == searchId));
        }

        if (!string.IsNullOrWhiteSpace(parameters.Status))
        {
            query = query.Where(t => t.Status == parameters.Status);
        }

        if (!string.IsNullOrWhiteSpace(parameters.Priority))
        {
            query = query.Where(t => t.Priority == parameters.Priority);
        }

        if (parameters.CreatedAtFrom.HasValue)
        {
            query = query.Where(t => t.CreatedAt >= parameters.CreatedAtFrom.Value);
        }

        if (parameters.CreatedAtTo.HasValue)
        {
            query = query.Where(t => t.CreatedAt <= parameters.CreatedAtTo.Value);
        }

        var totalCount = await query.CountAsync();

        var isDesc = string.Equals(parameters.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        query = (parameters.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "title" => isDesc
                ? query.OrderByDescending(t => t.Title).ThenByDescending(t => t.Id)
                : query.OrderBy(t => t.Title).ThenBy(t => t.Id),

            "priority" => isDesc
                ? query.OrderByDescending(t => t.Priority).ThenByDescending(t => t.Id)
                : query.OrderBy(t => t.Priority).ThenBy(t => t.Id),

            "status" => isDesc
                ? query.OrderByDescending(t => t.Status).ThenByDescending(t => t.Id)
                : query.OrderBy(t => t.Status).ThenBy(t => t.Id),

            "id" => isDesc
                ? query.OrderByDescending(t => t.Id)
                : query.OrderBy(t => t.Id),

            _ => isDesc
                ? query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                : query.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
        };

        var items = await query
            .Skip((parameters.Page - 1) * parameters.PageSize)
            .Take(parameters.PageSize)
            .ToListAsync();

        return Ok(new PagedResult<Ticket>
        {
            Items = items,
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        });
    }

    // GET: api/Tickets/dashboard
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        var query = _db.Tickets.AsNoTracking();

        if (User.IsInRole("Employee"))
        {
            query = query.Where(t => t.CreatedByUserId == userId);
        }
        else if (User.IsInRole("Agent"))
        {
            query = query.Where(t => t.AssignedToUserId == userId);
        }

        var tickets = await query.ToListAsync();

        var response = new TicketDashboardResponse
        {
            TotalTickets = tickets.Count,
            OpenTickets = tickets.Count(t => t.Status == "Open"),
            InProgressTickets = tickets.Count(t => t.Status == "InProgress"),
            ResolvedTickets = tickets.Count(t => t.Status == "Resolved")
        };

        foreach (var ticket in tickets)
        {
            var sla = _slaService.GetStatus(ticket);

            // Prevent resolved tickets with completed SLA checks from being counted as currently at risk.
            if (ticket.Status != "Resolved")
            {
                if (sla.FirstResponseStatus == "At Risk" || sla.ResolutionStatus == "At Risk")
                {
                    response.AtRiskTickets++;
                }
            }

            // Count breached tickets uniquely, even when both SLA milestones are in breach.
            if (sla.FirstResponseStatus == "Breached" || sla.ResolutionStatus == "Breached")
            {
                response.BreachedTickets++;
            }

            // Milestone checks met/breached
            if (sla.FirstResponseStatus == "Met")
                response.SlaChecksMet++;
            else if (sla.FirstResponseStatus == "Breached")
                response.SlaChecksBreached++;

            if (sla.ResolutionStatus == "Met")
                response.SlaChecksMet++;
            else if (sla.ResolutionStatus == "Breached")
                response.SlaChecksBreached++;
        }

        var completedChecks =
            response.SlaChecksMet + response.SlaChecksBreached;

        response.SlaCompliancePercentage = completedChecks == 0
            ? 0
            : Math.Round(
                response.SlaChecksMet * 100.0 / completedChecks, 2);

        return Ok(response);
    }

    // GET: api/Tickets/5
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        var ticket = await _service.GetByIdAsync(id);

        if (ticket is null)
            return NotFound();

        if (!IsAgentOrAdmin && ticket.CreatedByUserId != userId)
            return Forbid();

        if (User.IsInRole("Agent") &&
            ticket.AssignedToUserId != userId)
            return Forbid();

        return Ok(ticket);
    }

    // GET: api/Tickets/5/sla
    [HttpGet("{id:int}/sla")]
    public async Task<IActionResult> GetSlaStatus(int id)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        var ticket = await _db.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);

        if (ticket is null)
            return NotFound();

        if (!IsAgentOrAdmin && ticket.CreatedByUserId != userId)
            return Forbid();

        if (User.IsInRole("Agent") &&
            ticket.AssignedToUserId != userId)
            return Forbid();

        return Ok(_slaService.GetStatus(ticket));
    }

    // GET: api/Tickets/5/history
    [HttpGet("{id:int}/history")]
    public async Task<IActionResult> GetHistory(int id)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        var ticket = await _db.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);

        if (ticket is null)
            return NotFound(new { message = "Ticket not found." });

        if (User.IsInRole("Admin"))
        {
            // Admins can view all ticket histories
        }
        else if (User.IsInRole("Agent"))
        {
            if (ticket.AssignedToUserId != userId)
                return Forbid();
        }
        else
        {
            if (ticket.CreatedByUserId != userId)
                return Forbid();
        }

        var history = await _db.TicketHistories
            .AsNoTracking()
            .Include(h => h.User)
            .Where(h => h.TicketId == id)
            .OrderBy(h => h.CreatedAt)
            .Select(h => new TicketHistoryResponse
            {
                Id = h.Id,
                TicketId = h.TicketId,
                UserId = h.UserId,
                ActorName = h.User != null ? h.User.FullName : "System",
                ActorRole = h.User != null ? h.User.Role : "System",
                Action = h.Action,
                OldValue = h.OldValue,
                NewValue = h.NewValue,
                CreatedAt = h.CreatedAt
            })
            .ToListAsync();

        return Ok(history);
    }

    // POST: api/Tickets
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateTicketRequest request)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (string.IsNullOrWhiteSpace(request.Title) ||
            string.IsNullOrWhiteSpace(request.Description))
        {
            return BadRequest(new
            {
                message = "Title and description are required."
            });
        }

        var ticket = await _service.CreateAsync(request, userId);

        return CreatedAtAction(
            nameof(GetById),
            new { id = ticket.Id },
            ticket);
    }

    // PATCH: api/Tickets/5
    [HttpPatch("{id:int}")]
    [Authorize(Roles = "Agent,Admin")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateTicketRequest request)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var existingTicket = await _db.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);

        if (existingTicket is null)
            return NotFound();

        if (!User.IsInRole("Admin") &&
            existingTicket.AssignedToUserId != userId)
        {
            return Forbid();
        }

        var ticket = await _service.UpdateAsync(id, request, userId);

        if (ticket is null)
            return NotFound();

        return Ok(ticket);
    }

    // PATCH: api/Tickets/5/assign/3
    [HttpPatch("{id:int}/assign/{agentId:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Assign(
        int id,
        int agentId)
    {
        if (CurrentUserId is not int adminId)
            return Unauthorized();

        var ticket = await _db.Tickets.FindAsync(id);

        if (ticket is null)
            return NotFound();

        var agent = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == agentId);

        if (agent is null || agent.Role != "Agent")
        {
            return BadRequest(new
            {
                message = "The specified user is not an agent."
            });
        }

        var oldAgentId = ticket.AssignedToUserId;

        if (oldAgentId == agentId)
        {
            return Ok(new
            {
                message = "Ticket is already assigned to this agent.",
                ticket.Id,
                ticket.AssignedToUserId
            });
        }

        ticket.AssignedToUserId = agentId;

        _db.TicketHistories.Add(new TicketHistory
        {
            TicketId = ticket.Id,
            UserId = adminId,
            Action = "Assigned",
            OldValue = oldAgentId?.ToString(),
            NewValue = agentId.ToString(),
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        if (_notificationService != null)
        {
            // Notify assigned agent
            await _notificationService.CreateNotificationAsync(
                agentId,
                "TicketAssigned",
                $"Assigned to Ticket #{ticket.Id}",
                $"You have been assigned to Ticket #{ticket.Id}: {ticket.Title}.",
                ticket.Id);

            // If reassigned from another agent, notify old agent
            if (oldAgentId.HasValue && oldAgentId.Value != agentId)
            {
                await _notificationService.CreateNotificationAsync(
                    oldAgentId.Value,
                    "TicketAssigned",
                    $"Unassigned from Ticket #{ticket.Id}",
                    $"Ticket #{ticket.Id} has been reassigned to another agent.",
                    ticket.Id);
            }

            // Notify ticket creator (if creator is not the admin doing the assignment)
            if (ticket.CreatedByUserId.HasValue && ticket.CreatedByUserId.Value != adminId)
            {
                await _notificationService.CreateNotificationAsync(
                    ticket.CreatedByUserId.Value,
                    "TicketAssigned",
                    $"Ticket #{ticket.Id} Assigned",
                    $"Your ticket #{ticket.Id} has been assigned to an agent.",
                    ticket.Id);
            }
        }

        return Ok(new
        {
            message = "Ticket assigned successfully.",
            ticket.Id,
            ticket.AssignedToUserId
        });
    }

    // GET: api/Tickets/escalations
    [HttpGet("escalations")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetEscalations()
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        if (!User.IsInRole("Admin"))
            return Forbid();

        var escalations = await _db.TicketHistories
            .AsNoTracking()
            .Include(h => h.Ticket)
            .ThenInclude(t => t.AssignedToUser)
            .Where(h => h.Action == "Escalated")
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => new EscalationEventResponse
            {
                TicketId = h.TicketId,
                TicketTitle = h.Ticket.Title,
                Priority = h.Ticket.Priority,
                Status = h.Ticket.Status,
                AssignedToUserId = h.Ticket.AssignedToUserId,
                AssignedToName = h.Ticket.AssignedToUser != null ? h.Ticket.AssignedToUser.FullName : null,
                EscalatedAt = h.CreatedAt,
                Reason = h.NewValue ?? "SLA Breach Escalation"
            })
            .ToListAsync();

        return Ok(escalations);
    }
}
