using System.Diagnostics;
using System.Text.RegularExpressions;
using Northstar.Application.Abstractions;
using Northstar.Domain.Querying;
using Northstar.Domain.Results;

namespace Northstar.Application.Services;

/// <summary>
/// Intentionally simplified "first prototype" data access. It has exactly one notion of revenue and no idea of
/// returns, adjustments, approvals or row-level security. Educational only.
/// </summary>
public interface IPrototypeDataSource
{
    string Name { get; }
    string ImplicitRevenueDefinition { get; }
    Task<IReadOnlyList<ResultRow>> QueryRevenueAsync(Period? period, string? region, bool groupByRegion, CancellationToken ct);
}

public sealed record PrototypeResponse(
    string Question,
    string? Answer,
    IReadOnlyList<ResultRow> Rows,
    bool GroupedByRegion,
    string PseudoQuery,
    string ImplicitDefinition,
    IReadOnlyList<string> WhatWentWrong,
    bool Blocked,
    string DataSource,
    TimeSpan Duration);

/// <summary>
/// Prototype Mode: keyword matching → one implicit definition → immediate number. No clarification, no catalog,
/// no explanation, no authorization. It never executes free-form SQL/DAX either – the "generated query" is shown,
/// not run, so the demo stays safe.
/// </summary>
public sealed partial class PrototypeQueryService(IPrototypeDataSource source)
{
    public async Task<PrototypeResponse> AskAsync(string question, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var lower = question.ToLowerInvariant();
        var problems = new List<string>();

        if (SensitivePattern().IsMatch(lower))
        {
            var sql = "SELECT * FROM Customers -- generated from the user's wording, would be executed as-is";
            return new PrototypeResponse(question, null, [], false, sql, source.ImplicitRevenueDefinition,
                ["The prototype forwards the request to a text-to-SQL prompt without checking scope or permissions.",
                 "Prompt-injection phrases like 'ignore the restrictions' are passed straight into the prompt.",
                 "In this demo the execution is blocked by a guard – a real prototype would return the rows."],
                true, source.Name, sw.Elapsed);
        }

        var period = ExtractPeriod(lower);
        var region = ExtractRegion(lower);
        var groupByRegion = lower.Contains("by region") || lower.Contains("per region") || lower.Contains("regions");

        var rows = await source.QueryRevenueAsync(period, region, groupByRegion, ct);
        var total = rows.Sum(r => r.Value ?? 0);
        var answer = groupByRegion
            ? $"Revenue by region{PeriodText(period)}: " + string.Join(", ", rows.Select(r => $"{r.Keys[0]} {r.Value?.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} €"))
            : $"Revenue{PeriodText(period)}{(region is null ? "" : $" in {region}")} was {total.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} €.";

        problems.Add($"'Revenue' was silently interpreted as: {source.ImplicitRevenueDefinition}.");
        problems.Add("No business definition was offered or shown to the user.");
        problems.Add("Finance's Excel corrections are not part of this number.");
        if (lower.Contains("finance")) problems.Add("The question was about a discrepancy, but the prototype can only return one number.");
        if (groupByRegion) problems.Add("All regions are returned regardless of who is asking – no row-level security.");
        if (lower.Contains("compare") || lower.Contains(" vs ")) problems.Add("Comparison was not understood; only one period was used.");

        var pseudo = BuildPseudoQuery(period, region, groupByRegion);
        return new PrototypeResponse(question, answer, rows, groupByRegion, pseudo, source.ImplicitRevenueDefinition, problems, false, source.Name, sw.Elapsed);
    }

    private static string PeriodText(Period? p) => p is null ? "" : $" in {p.Label}";

    private string BuildPseudoQuery(Period? period, string? region, bool group)
    {
        var where = new List<string>();
        if (period is not null) where.Add($"OrderDate >= '{period.Start:yyyy-MM-dd}' AND OrderDate < '{period.EndExclusive:yyyy-MM-dd}'");
        if (region is not null) where.Add($"Region = '{region}'");
        var sql = $"SELECT {(group ? "Region, " : "")}SUM(Amount) AS Revenue FROM Orders";
        if (where.Count > 0) sql += " WHERE " + string.Join(" AND ", where);
        if (group) sql += " GROUP BY Region";
        return sql + "  -- 'Amount' chosen by the model; nobody checked which amount";
    }

    internal static Period? ExtractPeriod(string lower)
    {
        var m = QuarterPattern().Match(lower);
        if (m.Success) return Period.Resolve(PeriodSpec.OfQuarter(int.Parse(m.Groups["y"].Value), int.Parse(m.Groups["q"].Value)));
        var y = YearPattern().Match(lower);
        if (y.Success) return Period.Resolve(PeriodSpec.OfYear(int.Parse(y.Groups["y"].Value)));
        return null;
    }

    internal static string? ExtractRegion(string lower) =>
        new[] { "East", "North", "South", "West" }.FirstOrDefault(r => Regex.IsMatch(lower, $@"\bin {r.ToLowerInvariant()}\b|\b{r.ToLowerInvariant()} region\b"));

    [GeneratedRegex(@"q(?<q>[1-4])\s*(?<y>20\d\d)")] private static partial Regex QuarterPattern();
    [GeneratedRegex(@"\b(?<y>20\d\d)\b")] private static partial Regex YearPattern();
    [GeneratedRegex(@"contact|private|personal|customer records|email|phone|ignore")] private static partial Regex SensitivePattern();
}

