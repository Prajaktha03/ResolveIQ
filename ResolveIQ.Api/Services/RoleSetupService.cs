using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.Models;

namespace ResolveIQ.Api.Services;

public class RoleSetupService
{
    private readonly AppDbContext _db;
    private readonly PasswordHasher<User> _hasher = new();

    public RoleSetupService(AppDbContext db)
    {
        _db = db;
    }

    public async Task CreateInitialAdminAsync(
        string fullName,
        string email,
        string password)
    {
        if (await _db.Users.AnyAsync(u => u.Role == "Admin"))
        {
            throw new InvalidOperationException(
                "An Admin already exists. Setup cancelled.");
        }

        email = email.Trim().ToLowerInvariant();

        if (await _db.Users.AnyAsync(u => u.Email == email))
        {
            throw new InvalidOperationException(
                "That email already exists. Use another email or promote the existing user.");
        }

        var admin = new User
        {
            FullName = fullName.Trim(),
            Email = email,
            Role = "Admin",
            CreatedAt = DateTime.UtcNow
        };

        admin.PasswordHash =
            _hasher.HashPassword(admin, password);

        _db.Users.Add(admin);
        await _db.SaveChangesAsync();
    }
}