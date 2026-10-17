using System.ComponentModel.DataAnnotations;

namespace ResolveIQ.Api.DTOs;

public class CreateCommentRequest
{
    [Required]
    [StringLength(2000, MinimumLength = 1)]
    public string Content { get; set; } = string.Empty;
}