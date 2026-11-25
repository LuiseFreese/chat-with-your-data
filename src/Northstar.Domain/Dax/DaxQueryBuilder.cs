using System.Globalization;
using System.Text;
using Northstar.Domain.Metrics;
using Northstar.Domain.Querying;

namespace Northstar.Domain.Dax;

public sealed record DaxQuery(string Text, string ValueColumn, string? CompareValueColumn, IReadOnlyList<string> GroupColumns, string SemanticModel);

/// <summary>
/// Deterministic DAX generation. Only catalog-defined tables, columns and measures are emitted; the only
/// user-influenced values are dates (as integers) and filter members that were validated against known values.
/// </summary>
public sealed class DaxQueryBuilder
{
    public const string ValueColumn = "[Value]";
    public const string CompareValueColumn = "[Compare Value]";

    public DaxQuery Build(ValidatedQuery q)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DEFINE");
        sb.AppendLine($"    VAR __Start = {DateLiteral(q.Period.Start)}");
        sb.AppendLine($"    VAR __End = {DateLiteral(q.Period.EndExclusive)}");
        if (q.ComparePeriod is not null)
        {
            sb.AppendLine($"    VAR __CompareStart = {DateLiteral(q.ComparePeriod.Start)}");
            sb.AppendLine($"    VAR __CompareEnd = {DateLiteral(q.ComparePeriod.EndExclusive)}");
        }
        sb.AppendLine("    VAR __PeriodFilter = FILTER(ALL('Date'[Date]), 'Date'[Date] >= __Start && 'Date'[Date] < __End)");
        if (q.ComparePeriod is not null)
            sb.AppendLine("    VAR __CompareFilter = FILTER(ALL('Date'[Date]), 'Date'[Date] >= __CompareStart && 'Date'[Date] < __CompareEnd)");
        sb.AppendLine("EVALUATE");

        var measure = $"[{q.Metric.SemanticMeasure}]";
        var groupColumns = q.Dimensions.Select(ColumnRef).ToList();
        var filterArgs = q.Filters.Select(f => $"TREATAS({{{StringLiteral(f.Value)}}}, {ColumnRef(f.Dimension)})").ToList();

        string valueExpr = $"CALCULATE({measure}, __PeriodFilter)";
        string? compareExpr = q.ComparePeriod is not null ? $"CALCULATE({measure}, __CompareFilter)" : null;

        if (groupColumns.Count == 0)
        {
            var row = new StringBuilder();
            row.Append("ROW(");
            row.Append($"\"Value\", {Wrap(valueExpr, filterArgs)}");
            if (compareExpr is not null) row.Append($", \"Compare Value\", {Wrap(compareExpr, filterArgs)}");
            row.Append(')');
            sb.AppendLine("    " + row);
        }
        else
        {
            var summarize = new StringBuilder();
            summarize.AppendLine("SUMMARIZECOLUMNS(");
            foreach (var g in groupColumns) summarize.AppendLine($"        {g},");
            foreach (var f in filterArgs) summarize.AppendLine($"        {f},");
            summarize.Append($"        \"Value\", {valueExpr}");
            if (compareExpr is not null) summarize.Append($",{Environment.NewLine}        \"Compare Value\", {compareExpr}");
            summarize.AppendLine();
            summarize.Append("    )");

            if (q.TopN is int n)
                sb.AppendLine($"    TOPN({n}, {summarize}, {ValueColumn}, DESC)");
            else
                sb.AppendLine("    " + summarize);

            sb.AppendLine(q.TopN is not null || q.Dimensions.All(d => d.SemanticTable != MetricCatalog.DateTable)
                ? $"ORDER BY {ValueColumn} DESC"
                : $"ORDER BY {string.Join(", ", groupColumns)} ASC");
        }

        return new DaxQuery(sb.ToString().TrimEnd(), ValueColumn, compareExpr is null ? null : CompareValueColumn, groupColumns, MetricCatalog.SemanticModelName);
    }

    private static string Wrap(string expr, IReadOnlyList<string> filters) =>
        filters.Count == 0 ? expr : $"CALCULATE({expr}, {string.Join(", ", filters)})";

    private static string ColumnRef(DimensionDefinition d) => $"'{d.SemanticTable}'[{d.SemanticColumn}]";

    private static string DateLiteral(DateOnly d) => $"DATE({d.Year}, {d.Month}, {d.Day})";

    private static string StringLiteral(string value) =>
        "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
