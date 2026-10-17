using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.DTOs;

namespace ResolveIQ.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("general")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;

    public UsersController(AppDbContext db)
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

    // GET: api/Users/me
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUserProfile()
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null)
            return NotFound(new { message = "User not found." });

        return Ok(new UserProfileResponse
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role,
            CreatedAt = user.CreatedAt
        });
    }

    // PUT: api/Users/me
    [HttpPut("me")]
    [HttpPatch("me")]
    public async Task<IActionResult> UpdateCurrentUserProfile([FromBody] UpdateProfileRequest request)
    {
        if (CurrentUserId is not int userId)
            return Unauthorized();

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return NotFound(new { message = "User not found." });

        var newEmail = request.Email.Trim().ToLowerInvariant();

        // Check email uniqueness if email is changed
        if (!string.Equals(user.Email, newEmail, StringComparison.OrdinalIgnoreCase))
        {
            var emailExists = await _db.Users.AnyAsync(u => u.Id != userId && u.Email == newEmail);
            if (emailExists)
            {
                return Conflict(new { message = "An account with this email already exists." });
            }
        }

        user.FullName = request.FullName.Trim();
        user.Email = newEmail;

        await _db.SaveChangesAsync();

        return Ok(new UserProfileResponse
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role,
            CreatedAt = user.CreatedAt
        });
    }

    // GET: api/Users
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAll()
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var users = await _db.Users
            .AsNoTracking()
            .OrderBy(u => u.FullName)
            .Select(u => new UserProfileResponse
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email,
                Role = u.Role,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync();

        return Ok(users);
    }

    // GET: api/Users/5
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        if (CurrentUserId is not int currentUserId)
            return Unauthorized();

        // Users can view their own profile; only Admins can view other profiles
        if (currentUserId != id && !User.IsInRole("Admin"))
            return Forbid();

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user is null)
            return NotFound(new { message = "User not found." });

        return Ok(new UserProfileResponse
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role,
            CreatedAt = user.CreatedAt
        });
    }

    // PATCH: api/Users/2/role
    [HttpPatch("{id:int}/role")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateRole(
        int id,
        UpdateUserRoleRequest request)
    {
        var role = request.Role.Trim();

        if (role is not ("Employee" or "Agent"))
        {
            return BadRequest(new
            {
                message = "Role must be Employee or Agent. Admin roles cannot be assigned through this endpoint."
            });
        }

        var user = await _db.Users.FindAsync(id);

        if (user is null)
            return NotFound(new { message = "User not found." });

        if (user.Role == "Admin")
        {
            return BadRequest(new
            {
                message = "Admin accounts cannot be changed through this endpoint."
            });
        }

        user.Role = role;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = "User role updated successfully.",
            user.Id,
            user.FullName,
            user.Email,
            user.Role
        });
    }
}