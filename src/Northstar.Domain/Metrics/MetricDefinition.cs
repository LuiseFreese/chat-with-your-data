namespace Northstar.Domain.Metrics;

/// <summary>
/// A business metric published by the application. The LLM may only choose from these ids;
/// the DAX builder may only reference <see cref="SemanticMeasure"/>.
/// </summary>
public sealed record MetricDefinition(
    string Id,
    string BusinessName,
    string Description,
    string Formula,
    string SemanticMeasure,
    string BusinessOwner,
    IReadOnlyList<string> RelatedMetrics,
    bool ApprovedForConversationalQuerying,
    IReadOnlyList<string> Synonyms,
    string? AmbiguityGroup = null,
    string FormatString = "#,##0 €",
    string? ConfusionNote = null,
    bool LineGrain = false)
{
    public bool IsCurrency => FormatString.Contains('€');
}

/// <summary>A dimension the application allows for grouping and filtering. <paramref name="LineGrain"/> dimensions
/// (product, customer) exist only on order lines; returns and finance adjustments are booked at order/region level,
/// so metrics that include them cannot be broken down by these dimensions.</summary>
public sealed record DimensionDefinition(
    string Id,
    string BusinessName,
    string SemanticTable,
    string SemanticColumn,
    string Description,
    IReadOnlyList<string> KnownValues,
    IReadOnlyList<string> Synonyms,
    bool LineGrain = false);

/// <summary>A business term that maps to several metrics and therefore needs clarification.</summary>
public sealed record AmbiguousTerm(string Term, string Question, IReadOnlyList<string> CandidateMetricIds, IReadOnlyList<string> Synonyms);
