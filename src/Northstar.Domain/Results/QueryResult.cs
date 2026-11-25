namespace Northstar.Domain.Results;

public sealed record ResultRow(IReadOnlyList<string> Keys, decimal? Value, decimal? CompareValue);

/// <summary>Typed, validated result of a query. Produced by every data provider, regardless of origin.</summary>
public sealed record QueryResult(
    IReadOnlyList<string> GroupColumns,
    IReadOnlyList<ResultRow> Rows,
    string ValueLabel,
    string? CompareValueLabel,
    bool Truncated,
    IReadOnlyList<string> Warnings)
{
    public decimal? Total => Rows.Count == 1 && GroupColumns.Count == 0 ? Rows[0].Value : Rows.Sum(r => r.Value ?? 0);
    public static QueryResult Empty(string valueLabel) => new([], [], valueLabel, null, false, []);
}

public sealed record ResultExplanation(
    string MetricName,
    string MetricDefinition,
    string Formula,
    string SemanticMeasure,
    string BusinessOwner,
    string PeriodLabel,
    string? ComparePeriodLabel,
    IReadOnlyList<string> AppliedFilters,
    IReadOnlyList<string> Dimensions,
    string DataOrigin,
    string? SecurityContext);
