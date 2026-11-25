using System.Text.RegularExpressions;
using Northstar.Application.Abstractions;
using Northstar.Domain.Metrics;
using Northstar.Domain.Querying;

namespace Northstar.Infrastructure.Local;

/// <summary>
/// Rule-based parser for Local Demo Mode. Covers the conference scenarios and straightforward variations without
/// any external AI service. Produces exactly the same <see cref="QueryIntent"/> contract as the Azure OpenAI parser.
/// </summary>
public sealed partial class DeterministicDemoQueryIntentParser(MetricCatalog catalog) : IQueryIntentParser
{
    public string Name => "DeterministicDemoQueryIntentParser";

    public Task<QueryIntent> ParseAsync(string question, ConversationContext context, CancellationToken ct) =>
        Task.FromResult(Parse(question));

    public QueryIntent Parse(string question)
    {
        var lower = question.ToLowerInvariant().Trim();

        if (UnsupportedPattern().IsMatch(lower))
        {
            return new QueryIntent
            {
                UnsupportedRequest = true,
                UnsupportedReason = "The request asks for record-level or personal data, or tries to bypass access rules. Only approved aggregated metrics are available.",
                Interpretation = "Request for raw/personal data detected.",
            };
        }

        // Periods – up to two for comparisons.
        var periods = new List<PeriodSpec>();
        foreach (Match m in QuarterPattern().Matches(lower))
            periods.Add(PeriodSpec.OfQuarter(int.Parse(m.Groups["y"].Value), int.Parse(m.Groups["q"].Value)));
        if (periods.Count == 0)
        {
            foreach (Match m in MonthPattern().Matches(lower))
                periods.Add(PeriodSpec.OfMonth(int.Parse(m.Groups["y"].Value), MonthNumber(m.Groups["m"].Value)));
        }
        if (periods.Count == 0)
        {
            foreach (Match m in YearPattern().Matches(lower))
                periods.Add(PeriodSpec.OfYear(int.Parse(m.Groups["y"].Value)));
        }
        var isComparison = periods.Count == 2 && (lower.Contains("compar") || lower.Contains(" vs") || lower.Contains(" and ") || lower.Contains("versus"));

        // Dimensions: "by region", "per business unit", "top 5 categories"
        var dims = new List<string>();
        foreach (var d in catalog.Dimensions)
            if (d.Synonyms.Any(s => Regex.IsMatch(lower, $@"\b(by|per|across|for each|split by|broken down by|top\s+\d+)\s+{Regex.Escape(s)}\b")))
                dims.Add(d.Id);

        // Filters: "in West", "West region", "for dealers", "E-Bike"
        var filters = new Dictionary<string, string>();
        foreach (var d in catalog.Dimensions.Where(d => d.KnownValues.Count > 0 && !dims.Contains(d.Id)))
            foreach (var v in d.KnownValues)
                if (Regex.IsMatch(lower, $@"\b(in|for|of)\s+(the\s+)?{Regex.Escape(v.ToLowerInvariant())}\b(?!\s+region[s]?\s+(and|,))") || Regex.IsMatch(lower, $@"\b{Regex.Escape(v.ToLowerInvariant())}\s+region\b"))
                { filters[d.Id] = v; break; }

        int? topN = null;
        var top = TopPattern().Match(lower);
        if (top.Success) topN = int.Parse(top.Groups["n"].Value);

        // Metric
        var (metric, ambiguous) = catalog.ResolveTerm(lower);
        string? metricId = metric?.Id ?? ambiguous?.Term;
        var interpretation = metric is not null
            ? $"Metric '{metric.BusinessName}' recognised."
            : ambiguous is not null ? $"'{ambiguous.Term}' matches several approved definitions." : "No metric recognised.";

        var clarification = metric is null && ambiguous is not null;
        if (lower.Contains("different") && lower.Contains("finance"))
        {
            metricId = "finance_adjustments";
            clarification = false;
            interpretation = "Question about the gap between Sales and Finance numbers → showing Finance Adjustments; see Data Reconciliation for the full bridge.";
        }

        return new QueryIntent
        {
            Metric = metricId,
            Dimensions = dims,
            Period = periods.Count > 0 ? periods[0] : PeriodSpec.All(),
            ComparePeriod = isComparison ? periods[1] : null,
            Filters = filters,
            TopN = topN,
            Visualization = VisualizationKind.Auto,
            ClarificationRequired = clarification,
            Interpretation = interpretation,
        };
    }

    private static int MonthNumber(string m) => m[..3] switch
    {
        "jan" => 1, "feb" => 2, "mar" => 3, "apr" => 4, "may" => 5, "jun" => 6,
        "jul" => 7, "aug" => 8, "sep" => 9, "oct" => 10, "nov" => 11, _ => 12,
    };

    [GeneratedRegex(@"\bq(?<q>[1-4])\s*[/-]?\s*(?<y>20\d\d)\b")] private static partial Regex QuarterPattern();
    [GeneratedRegex(@"\b(?<m>january|february|march|april|may|june|july|august|september|october|november|december|jan|feb|mar|apr|jun|jul|aug|sep|sept|oct|nov|dec)\s+(?<y>20\d\d)\b")] private static partial Regex MonthPattern();
    [GeneratedRegex(@"\b(?<y>20\d\d)\b")] private static partial Regex YearPattern();
    [GeneratedRegex(@"\btop\s+(?<n>\d{1,3})\b")] private static partial Regex TopPattern();
    [GeneratedRegex(@"ignore (the|all|any)? ?(restrictions|rules|rls|security|policy)|customer records|contact details|private|personal data|email address|phone number|all customers|raw data|drop table|delete|select \*|show me everything|bypass")] private static partial Regex UnsupportedPattern();
}
