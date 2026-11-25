namespace Northstar.Domain.Querying;

/// <summary>A resolved, closed date interval. Start inclusive, End exclusive.</summary>
public sealed record Period(PeriodType Type, DateOnly Start, DateOnly EndExclusive, string Label)
{
    public const int MinYear = 2025;
    public const int MaxYear = 2026;

    public static Period Resolve(PeriodSpec spec, DateOnly? today = null)
    {
        switch (spec.Type)
        {
            case PeriodType.All:
                return new Period(PeriodType.All, new DateOnly(MinYear, 1, 1), new DateOnly(MaxYear + 1, 1, 1), $"{MinYear}–{MaxYear}");
            case PeriodType.Year:
                {
                    var y = RequireYear(spec.Year);
                    return new Period(PeriodType.Year, new DateOnly(y, 1, 1), new DateOnly(y + 1, 1, 1), y.ToString());
                }
            case PeriodType.Quarter:
                {
                    var y = RequireYear(spec.Year);
                    if (spec.Quarter is < 1 or > 4 or null) throw new PeriodException("Quarter must be between 1 and 4.");
                    var q = spec.Quarter.Value;
                    var start = new DateOnly(y, (q - 1) * 3 + 1, 1);
                    return new Period(PeriodType.Quarter, start, start.AddMonths(3), $"Q{q} {y}");
                }
            case PeriodType.Month:
                {
                    var y = RequireYear(spec.Year);
                    if (spec.Month is < 1 or > 12 or null) throw new PeriodException("Month must be between 1 and 12.");
                    var start = new DateOnly(y, spec.Month.Value, 1);
                    return new Period(PeriodType.Month, start, start.AddMonths(1), start.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture));
                }
            case PeriodType.YearToDate:
                {
                    var t = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
                    var y = Math.Clamp(spec.Year ?? t.Year, MinYear, MaxYear);
                    return new Period(PeriodType.YearToDate, new DateOnly(y, 1, 1), t.AddDays(1), $"YTD {y}");
                }
            default:
                throw new PeriodException($"Unsupported period type {spec.Type}.");
        }
    }

    private static int RequireYear(int? year)
    {
        if (year is null) throw new PeriodException("A year is required.");
        if (year < MinYear || year > MaxYear) throw new PeriodException($"Year must be between {MinYear} and {MaxYear}; data outside this range is not available.");
        return year.Value;
    }

    public bool Contains(DateOnly d) => d >= Start && d < EndExclusive;
}

public sealed class PeriodException(string message) : Exception(message);
