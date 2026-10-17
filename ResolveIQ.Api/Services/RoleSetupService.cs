using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.Models;

namespace ResolveIQ.Api.Services;

public enum ProvisioningStatus
{
    Created,
    Preserved,
    Conflict,
    Skipped,
    Invalid
}

public record AdminProvisioningResult(
    string? Email,
    ProvisioningStatus Status,
    string Message
);

public class RoleSetupService
{
    private readonly AppDbContext _db;
    private readonly ILogger<RoleSetupService>? _logger;
    private readonly PasswordHasher<User> _hasher = new();

    public RoleSetupService(AppDbContext db, ILogger<RoleSetupService>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Securely and idempotently provisions a single Admin account if not already present.
    /// Preserves existing Admin accounts without re-hashing or altering passwords.
    /// Prevents promoting existing non-Admin (Employee/Agent) accounts and reports safe conflict.
    /// </summary>
    public async Task<AdminProvisioningResult> ProvisionAdminAccountAsync(
        string? fullName,
        string? email,
        string? password,
        CancellationToken cancellationToken = default)
    {
        // 1. Check for complete omission (slot skipped)
        if (string.IsNullOrWhiteSpace(fullName) &&
            string.IsNullOrWhiteSpace(email) &&
            string.IsNullOrWhiteSpace(password))
        {
            return new AdminProvisioningResult(null, ProvisioningStatus.Skipped, "Admin configuration not provided for this slot.");
        }

        // 2. Validate presence of all required fields
        if (string.IsNullOrWhiteSpace(fullName) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            _logger?.LogWarning("Incomplete Admin configuration: Full name, email, and password are all required.");
            return new AdminProvisioningResult(email?.Trim().ToLowerInvariant(), ProvisioningStatus.Invalid, "Full name, email, and password are all required.");
        }

        // 3. Validate password requirements (minimum 8 characters)
        if (password.Length < 8)
        {
            _logger?.LogWarning("Admin password validation failed: Password must be at least 8 characters long.");
            return new AdminProvisioningResult(email.Trim().ToLowerInvariant(), ProvisioningStatus.Invalid, "Password must be at least 8 characters long.");
        }

        // 4. Validate email format
        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (!IsValidEmail(normalizedEmail))
        {
            _logger?.LogWarning("Admin email validation failed: Invalid email format '{Email}'.", normalizedEmail);
            return new AdminProvisioningResult(normalizedEmail, ProvisioningStatus.Invalid, "A valid email address is required.");
        }

        // 5. Check if user already exists
        var existingUser = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (existingUser != null)
        {
            if (string.Equals(existingUser.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogInformation("Admin account for {Email} already exists; preserving existing account and credentials.", normalizedEmail);
                return new AdminProvisioningResult(normalizedEmail, ProvisioningStatus.Preserved, $"Admin account for '{normalizedEmail}' already exists; credentials preserved.");
            }

            // Existing account with non-Admin role: DO NOT automatically promote, report safe conflict
            _logger?.LogWarning("Conflict: Account for {Email} already exists with non-Admin role '{Role}'. Existing account was not modified.", normalizedEmail, existingUser.Role);
            return new AdminProvisioningResult(normalizedEmail, ProvisioningStatus.Conflict, $"Account for '{normalizedEmail}' already exists with role '{existingUser.Role}'. Existing account was not modified.");
        }

        // 6. Create new Admin user
        var admin = new User
        {
            FullName = fullName.Trim(),
            Email = normalizedEmail,
            Role = "Admin",
            CreatedAt = DateTime.UtcNow
        };

        admin.PasswordHash = _hasher.HashPassword(admin, password);
        _db.Users.Add(admin);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger?.LogInformation("Admin account successfully created for {Email}.", normalizedEmail);
            return new AdminProvisioningResult(normalizedEmail, ProvisioningStatus.Created, $"Admin account successfully created for '{normalizedEmail}'.");
        }
        catch (DbUpdateException ex)
        {
            // Concurrency handling: Another node or task may have inserted this user concurrently
            _logger?.LogWarning(ex, "Concurrency conflict during admin provisioning for {Email}. Verifying state.", normalizedEmail);

            // Detach failed entity to avoid dirty state in DbContext
            _db.Entry(admin).State = EntityState.Detached;

            var concurrentUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);
            if (concurrentUser != null && string.Equals(concurrentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                return new AdminProvisioningResult(normalizedEmail, ProvisioningStatus.Preserved, $"Admin account for '{normalizedEmail}' was provisioned concurrently; credentials preserved.");
            }

            return new AdminProvisioningResult(normalizedEmail, ProvisioningStatus.Conflict, $"Failed to provision admin account for '{normalizedEmail}' due to a database constraint conflict.");
        }
    }

    /// <summary>
    /// Reads configured Admin credentials for Admin 1 and Admin 2 from configuration / environment variables
    /// and provisions them safely and idempotently.
    /// </summary>
    public async Task<List<AdminProvisioningResult>> ProvisionConfiguredAdminsAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var results = new List<AdminProvisioningResult>();

        // Admin 1 (RESOLVEIQ_ADMIN_NAME, RESOLVEIQ_ADMIN_EMAIL, RESOLVEIQ_ADMIN_PASSWORD)
        var admin1Name = configuration["RESOLVEIQ_ADMIN_NAME"] ?? configuration["Admin:Name"];
        var admin1Email = configuration["RESOLVEIQ_ADMIN_EMAIL"] ?? configuration["Admin:Email"];
        var admin1Password = configuration["RESOLVEIQ_ADMIN_PASSWORD"] ?? configuration["Admin:Password"];

        var res1 = await ProvisionAdminAccountAsync(admin1Name, admin1Email, admin1Password, cancellationToken);
        results.Add(res1);

        // Admin 2 (RESOLVEIQ_ADMIN2_NAME, RESOLVEIQ_ADMIN2_EMAIL, RESOLVEIQ_ADMIN2_PASSWORD)
        var admin2Name = configuration["RESOLVEIQ_ADMIN2_NAME"] ?? configuration["Admin2:Name"];
        var admin2Email = configuration["RESOLVEIQ_ADMIN2_EMAIL"] ?? configuration["Admin2:Email"];
        var admin2Password = configuration["RESOLVEIQ_ADMIN2_PASSWORD"] ?? configuration["Admin2:Password"];

        var res2 = await ProvisionAdminAccountAsync(admin2Name, admin2Email, admin2Password, cancellationToken);
        results.Add(res2);

        return results;
    }

    /// <summary>
    /// Backward-compatible method for legacy setup routines.
    /// </summary>
    public async Task CreateInitialAdminAsync(
        string fullName,
        string email,
        string password)
    {
        var result = await ProvisionAdminAccountAsync(fullName, email, password);
        if (result.Status == ProvisioningStatus.Conflict || result.Status == ProvisioningStatus.Invalid)
        {
            throw new InvalidOperationException(result.Message);
        }
        if (result.Status == ProvisioningStatus.Preserved)
        {
            throw new InvalidOperationException("An Admin already exists. Setup cancelled.");
        }
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        try
        {
            var address = new MailAddress(email);
            return address.Address == email && email.Contains('.');
        }
        catch
        {
            return false;
        }
    }
}