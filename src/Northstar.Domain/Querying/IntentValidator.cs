using Northstar.Domain.Metrics;

namespace Northstar.Domain.Querying;

/// <summary>A query that passed every server-side check. Only this type reaches the DAX builder.</summary>
public sealed record ValidatedQuery(
    MetricDefinition Metric,
    IReadOnlyList<DimensionDefinition> Dimensions,
    Period Period,
    Period? ComparePeriod,
    IReadOnlyList<DimensionFilter> Filters,
    int? TopN,
    VisualizationKind Visualization)
{
    public const int MaxTopN = 25;
    public bool IsComparison => ComparePeriod is not null;
}

public sealed record DimensionFilter(DimensionDefinition Dimension, string Value);

public sealed record ValidationIssue(string Code, string Message, ValidationSeverity Severity = ValidationSeverity.Error);

public enum ValidationSeverity { Info, Warning, Error }

public sealed record ClarificationRequest(string Term, string Question, IReadOnlyList<MetricDefinition> Options);

public sealed record ValidationOutcome(
    ValidatedQuery? Query,
    ClarificationRequest? Clarification,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsValid => Query is not null && Issues.All(i => i.Severity != ValidationSeverity.Error);
    public bool NeedsClarification => Clarification is not null;
    public bool IsRejected => Query is null && Clarification is null;
}

/// <summary>
/// Deterministic server-side gate between the LLM and the query builder. Everything the model returned is
/// checked against the catalog; nothing is passed through as free text.
/// </summary>
public sealed class IntentValidator(MetricCatalog catalog)
{
    public ValidationOutcome Validate(QueryIntent intent)
    {
        var issues = new List<ValidationIssue>();

        if (intent.UnsupportedRequest)
        {
            issues.Add(new("UNSUPPORTED_REQUEST", intent.UnsupportedReason ?? "The request is outside the approved analytics scope."));
            return new ValidationOutcome(null, null, issues);
        }

        // 1. Metric: must be an approved catalog metric or a known ambiguous term.
        var metric = catalog.FindMetric(intent.Metric);
        if (metric is null)
        {
            var ambiguous = catalog.FindAmbiguousTerm(intent.Metric);
            if (ambiguous is not null || intent.ClarificationRequired)
            {
                ambiguous ??= catalog.AmbiguousTerms[0];
                var options = ambiguous.CandidateMetricIds.Select(id => catalog.FindMetric(id)!).ToList();
                issues.Add(new("AMBIGUOUS_METRIC", $"'{ambiguous.Term}' maps to {options.Count} approved definitions; clarification required.", ValidationSeverity.Info));
                return new ValidationOutcome(null, new ClarificationRequest(ambiguous.Term, ambiguous.Question, options), issues);
            }
            issues.Add(new("UNKNOWN_METRIC", intent.Metric is null
                ? "No supported metric was recognised in the question."
                : $"'{intent.Metric}' is not a published metric."));
            return new ValidationOutcome(null, null, issues);
        }
        if (!metric.ApprovedForConversationalQuerying)
        {
            issues.Add(new("METRIC_NOT_APPROVED", $"'{metric.BusinessName}' is not approved for conversational querying."));
            return new ValidationOutcome(null, null, issues);
        }

        // 2. Dimensions
        var dimensions = new List<DimensionDefinition>();
        foreach (var d in intent.Dimensions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var dim = catalog.FindDimension(d);
            if (dim is null) issues.Add(new("UNKNOWN_DIMENSION", $"'{d}' is not an approved dimension."));
            else dimensions.Add(dim);
        }
        if (dimensions.Count > 2) issues.Add(new("TOO_MANY_DIMENSIONS", "At most two dimensions can be combined in one answer."));
        foreach (var dim in dimensions.Where(d => d.LineGrain && !metric.LineGrain))
            issues.Add(new("GRAIN_MISMATCH", $"'{metric.BusinessName}' includes returns and/or finance adjustments, which are booked per order and region – it cannot be broken down by {dim.BusinessName}. Use Gross Revenue, Total Discounts or Order Count for that breakdown."));

        // 3. Filters – dimension must exist and value must be a known member.
        var filters = new List<DimensionFilter>();
        foreach (var (key, value) in intent.Filters)
        {
            var dim = catalog.FindDimension(key);
            if (dim is null) { issues.Add(new("UNKNOWN_FILTER", $"'{key}' is not an approved filter dimension.")); continue; }
            if (dim.KnownValues.Count == 0) { issues.Add(new("UNFILTERABLE_DIMENSION", $"'{dim.BusinessName}' cannot be used as a filter; use the period instead.")); continue; }
            if (dim.LineGrain && !metric.LineGrain) { issues.Add(new("GRAIN_MISMATCH", $"'{metric.BusinessName}' cannot be filtered by {dim.BusinessName} (returns and adjustments are not booked at that level).")); continue; }
            var known = dim.KnownValues.FirstOrDefault(v => v.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (known is null) { issues.Add(new("UNKNOWN_FILTER_VALUE", $"'{value}' is not a known {dim.BusinessName}.")); continue; }
            filters.Add(new DimensionFilter(dim, known));
        }

        // 4. Period(s)
        Period? period = null, compare = null;
        try { period = Period.Resolve(intent.Period ?? PeriodSpec.All()); }
        catch (PeriodException ex) { issues.Add(new("INVALID_PERIOD", ex.Message)); }
        if (intent.ComparePeriod is not null)
        {
            try { compare = Period.Resolve(intent.ComparePeriod); }
            catch (PeriodException ex) { issues.Add(new("INVALID_COMPARE_PERIOD", ex.Message)); }
            if (compare is not null && period is not null && compare.Type != period.Type)
                issues.Add(new("PERIOD_TYPE_MISMATCH", "Both periods of a comparison must be of the same type (e.g. quarter vs quarter)."));
        }

        // 5. Top-N bounds
        int? topN = intent.TopN;
        if (topN is not null)
        {
            if (topN < 1) issues.Add(new("INVALID_TOPN", "Top-N must be at least 1."));
            else if (topN > ValidatedQuery.MaxTopN) { issues.Add(new("TOPN_CAPPED", $"Top-N was capped to {ValidatedQuery.MaxTopN}.", ValidationSeverity.Warning)); topN = ValidatedQuery.MaxTopN; }
            if (dimensions.Count == 0) issues.Add(new("TOPN_WITHOUT_DIMENSION", "Top-N requires a dimension to rank."));
        }

        if (issues.Any(i => i.Severity == ValidationSeverity.Error) || period is null)
            return new ValidationOutcome(null, null, issues);

        var viz = intent.Visualization == VisualizationKind.Auto ? ChooseVisualization(dimensions.Count, compare is not null) : intent.Visualization;
        issues.Add(new("OK", $"Metric '{metric.BusinessName}' → measure [{metric.SemanticMeasure}] approved.", ValidationSeverity.Info));
        return new ValidationOutcome(new ValidatedQuery(metric, dimensions, period, compare, filters, topN, viz), null, issues);
    }

    private static VisualizationKind ChooseVisualization(int dimensionCount, bool comparison) =>
        (dimensionCount, comparison) switch
        {
            (0, false) => VisualizationKind.Number,
            (0, true) => VisualizationKind.ComparisonBar,
            (1, true) => VisualizationKind.ComparisonBar,
            (1, false) => VisualizationKind.Bar,
            _ => VisualizationKind.Table,
        };
}
