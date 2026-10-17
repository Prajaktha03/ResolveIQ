using System.ComponentModel.DataAnnotations;

namespace ResolveIQ.Api.DTOs;

public class UpdateUserRoleRequest
{
    [Required]
    public string Role { get; set; } = string.Empty;
}