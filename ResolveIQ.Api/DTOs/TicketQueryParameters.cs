using System.ComponentModel.DataAnnotations;

namespace ResolveIQ.Api.DTOs;

public class TicketQueryParameters
{
    /// <summary>Search term matched against title, description, and ticket ID.</summary>
    public string? Search { get; set; }

    /// <summary>Filter by ticket status: Open, InProgress, Resolved.</summary>
    [RegularExpression("^(Open|InProgress|Resolved)$", ErrorMessage = "Status must be Open, InProgress, or Resolved.")]
    public string? Status { get; set; }

    /// <summary>Filter by ticket priority: Low, Medium, High, Critical.</summary>
    [RegularExpression("^(Low|Medium|High|Critical)$", ErrorMessage = "Priority must be Low, Medium, High, or Critical.")]
    public string? Priority { get; set; }

    /// <summary>Filter by assigned agent ID.</summary>
    public int? AssigneeId { get; set; }

    /// <summary>Filter by ticket creator user ID.</summary>
    public int? CreatorId { get; set; }

    /// <summary>Filter tickets created on or after this UTC timestamp.</summary>
    public DateTime? CreatedAtFrom { get; set; }

    /// <summary>Filter tickets created on or before this UTC timestamp.</summary>
    public DateTime? CreatedAtTo { get; set; }

    /// <summary>Sort column: CreatedAt, Title, Priority, Status, or Id. Default is CreatedAt.</summary>
    public string? SortBy { get; set; } = "CreatedAt";

    /// <summary>Sort direction: asc or desc. Default is desc.</summary>
    [RegularExpression("^(asc|desc|ASC|DESC)$", ErrorMessage = "SortDirection must be 'asc' or 'desc'.")]
    public string? SortDirection { get; set; } = "desc";

    private int _page = 1;
    /// <summary>1-based page index. Default is 1.</summary>
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    private int _pageSize = 10;
    /// <summary>Number of items per page (1 to 100). Default is 10.</summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? 10 : (value > 100 ? 100 : value);
    }
}
