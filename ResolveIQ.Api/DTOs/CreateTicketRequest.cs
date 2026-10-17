using System.ComponentModel.DataAnnotations;

namespace ResolveIQ.Api.DTOs;

public class CreateTicketRequest
{
    [Required, StringLength(150)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(Low|Medium|High|Critical)$")]
    public string Priority { get; set; } = "Medium";
}
