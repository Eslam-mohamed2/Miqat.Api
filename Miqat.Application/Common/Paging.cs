namespace Miqat.Application.Common;

public static class Paging
{
    public const int MaxPageSize = 100;

    /// <summary>Keeps client supplied paging values inside a safe range.</summary>
    public static (int PageIndex, int PageSize) Normalize(int pageIndex, int pageSize, int defaultPageSize = 20)
    {
        if (pageIndex < 0) pageIndex = 0;
        if (pageSize <= 0) pageSize = defaultPageSize;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;
        return (pageIndex, pageSize);
    }
}
