namespace AriesMagicAppointmentSystem.ViewModels;

/// <summary>Read-only, filter-preserving navigation for the Owner financial queues.</summary>
public sealed class QueuePaginationViewModel
{
    public QueuePaginationViewModel(int totalCount, int page, int pageSize, string action, Dictionary<string, string> filters)
    {
        TotalCount = totalCount;
        PageSize = pageSize is 10 or 25 or 50 ? pageSize : 10;
        TotalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        Page = Math.Clamp(page, 1, TotalPages);
        Action = action;
        Filters = filters;
    }

    public int TotalCount { get; }
    public int Page { get; }
    public int PageSize { get; }
    public int TotalPages { get; }
    public int Skip => (Page - 1) * PageSize;
    public int First => TotalCount == 0 ? 0 : Skip + 1;
    public int Last => Math.Min(Skip + PageSize, TotalCount);
    public int AwaitingReviewCount { get; init; }
    public int OcrAttentionCount { get; init; }
    public int AwaitingFinalVerificationCount { get; init; }
    public string Action { get; }
    public Dictionary<string, string> Filters { get; }

    public Dictionary<string, string> Routes(int? page = null) => new(Filters)
    {
        ["page"] = (page ?? Page).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["pageSize"] = PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };
}
