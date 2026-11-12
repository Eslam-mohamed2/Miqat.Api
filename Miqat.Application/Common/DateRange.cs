namespace Miqat.Application.Common;

/// <summary>A stretch of time in UTC: from <see cref="From"/> up to, but not including, <see cref="To"/>.</summary>
public readonly record struct DateRange(DateTime From, DateTime To)
{
    /// <summary>The longest stretch a calendar view may ask for: a year.</summary>
    public const int MaxCalendarDays = 366;

    public TimeSpan Length => To - From;

    /// <summary>True when something lasting from <paramref name="start"/> to <paramref name="end"/> touches this range.</summary>
    public bool Overlaps(DateTime start, DateTime end) =>
        start < To && (end > From || (start == end && start >= From));

    /// <summary>
    /// Builds a range from request values. Dates from a query string carry no
    /// time zone, so they are read as UTC like dates in a request body.
    /// Rejects (400) a missing or backwards range and one longer than <paramref name="maxDays"/>.
    /// </summary>
    public static DateRange Require(DateTime? from, DateTime? to, int maxDays)
    {
        var start = UtcDateTimeConverter.Normalize(from)
            ?? throw new ApiException("\"from\" is required.", 400);
        var end = UtcDateTimeConverter.Normalize(to)
            ?? throw new ApiException("\"to\" is required.", 400);

        if (end <= start)
            throw new ApiException("\"to\" must be after \"from\".", 400);
        if ((end - start).TotalDays > maxDays)
            throw new ApiException($"The range cannot be longer than {maxDays} days.", 400);

        return new DateRange(start, end);
    }
}
