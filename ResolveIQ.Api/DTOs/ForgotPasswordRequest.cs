using System.ComponentModel.DataAnnotations;

namespace ResolveIQ.Api.DTOs;

public class ForgotPasswordRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;
}
