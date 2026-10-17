using System.ComponentModel.DataAnnotations;

namespace ResolveIQ.Api.DTOs;

public class UpdateProfileRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;
}
