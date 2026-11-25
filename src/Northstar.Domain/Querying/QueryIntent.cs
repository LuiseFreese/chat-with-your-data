using System.Text.Json.Serialization;

namespace Northstar.Domain.Querying;

/// <summary>Structured output of the intent parser. This is the ONLY thing the LLM produces.</summary>
public sealed record QueryIntent
{
    /// <summary>Metric id from the published catalog, or an ambiguous term id (e.g. "revenue") when the question is not specific.</summary>
    [JsonPropertyName("metric")] public string? Metric { get; init; }

    [JsonPropertyName("dimensions")] public IReadOnlyList<string> Dimensions { get; init; } = [];

    [JsonPropertyName("period")] public PeriodSpec? Period { get; init; }

    /// <summary>Second period for a comparison question ("Q3 2025 vs Q3 2026").</summary>
    [JsonPropertyName("comparePeriod")] public PeriodSpec? ComparePeriod { get; init; }

    /// <summary>Dimension id → value. Values are validated against the catalog's known values.</summary>
    [JsonPropertyName("filters")] public IReadOnlyDictionary<string, string> Filters { get; init; } = new Dictionary<string, string>();

    [JsonPropertyName("topN")] public int? TopN { get; init; }

    [JsonPropertyName("visualization")] public VisualizationKind Visualization { get; init; } = VisualizationKind.Auto;

    [JsonPropertyName("clarificationRequired")] public bool ClarificationRequired { get; init; }

    /// <summary>Set when the question is outside what the application supports (e.g. asks for raw records or personal data).</summary>
    [JsonPropertyName("unsupportedRequest")] public bool UnsupportedRequest { get; init; }

    [JsonPropertyName("unsupportedReason")] public string? UnsupportedReason { get; init; }

    /// <summary>Free-text explanation of how the question was interpreted – shown to the user, never executed.</summary>
    [JsonPropertyName("interpretation")] public string? Interpretation { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<VisualizationKind>))]
public enum VisualizationKind { Auto, Number, Bar, Line, Table, ComparisonBar }

[JsonConverter(typeof(JsonStringEnumConverter<PeriodType>))]
public enum PeriodType { Year, Quarter, Month, YearToDate, All }

public sealed record PeriodSpec
{
    [JsonPropertyName("type")] public PeriodType Type { get; init; }
    [JsonPropertyName("year")] public int? Year { get; init; }
    [JsonPropertyName("quarter")] public int? Quarter { get; init; }
    [JsonPropertyName("month")] public int? Month { get; init; }

    public static PeriodSpec OfQuarter(int year, int quarter) => new() { Type = PeriodType.Quarter, Year = year, Quarter = quarter };
    public static PeriodSpec OfYear(int year) => new() { Type = PeriodType.Year, Year = year };
    public static PeriodSpec OfMonth(int year, int month) => new() { Type = PeriodType.Month, Year = year, Month = month };
    public static PeriodSpec All() => new() { Type = PeriodType.All };
}
