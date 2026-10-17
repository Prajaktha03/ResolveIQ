using System.ComponentModel.DataAnnotations;

namespace ResolveIQ.Api.DTOs;

public class UpdateTicketRequest
{
    [Required]
    [RegularExpression("^(Open|InProgress|Resolved)$")]
    public string Status { get; set; } = "Open";

    [Required]
    [RegularExpression("^(Low|Medium|High|Critical)$")]
    public string Priority { get; set; } = "Medium";
}
