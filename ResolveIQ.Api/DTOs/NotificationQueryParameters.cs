namespace ResolveIQ.Api.DTOs;

public class NotificationQueryParameters
{
    private int _page = 1;
    private int _pageSize = 10;

    public bool? IsRead { get; set; }

    public int Page
    {
        get => _page;
        set => _page = Math.Max(1, value);
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => 10,
            > 100 => 100,
            _ => value
        };
    }
}
