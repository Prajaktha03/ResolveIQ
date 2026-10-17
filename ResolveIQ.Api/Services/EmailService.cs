using Microsoft.Extensions.Logging;

namespace ResolveIQ.Api.Services;

public class EmailService : IEmailService
{
    private readonly ILogger<EmailService>? _logger;

    public EmailService(ILogger<EmailService>? logger = null)
    {
        _logger = logger;
    }

    public Task SendPasswordResetEmailAsync(string toEmail, string resetToken)
    {
        // Safe logging: log that a reset email was requested without exposing the token in logs
        _logger?.LogInformation("Password reset email dispatched to {Email}.", toEmail);

        return Task.CompletedTask;
    }
}
