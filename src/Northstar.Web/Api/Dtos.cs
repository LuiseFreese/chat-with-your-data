using Northstar.Application.Pipeline;
using Northstar.Domain.Querying;

namespace Northstar.Web.Api;

public sealed record QueryApiRequest(
    string Question,
    string? SelectedMetricId = null,
    ConversationStateDto? Conversation = null);

public sealed record ConversationStateDto(string? PendingQuestion, QueryIntent? PendingIntent, IReadOnlyList<string> RecentQuestions)
{
    public Northstar.Application.Abstractions.ConversationContext ToContext() => new(PendingQuestion, PendingIntent, null, RecentQuestions);
    public static ConversationStateDto From(Northstar.Application.Abstractions.ConversationContext c) => new(c.PendingQuestion, c.PendingIntent, c.RecentQuestions);
}

public sealed record QueryApiResponse(
    Guid QueryId,
    string CorrelationId,
    QueryOutcome Outcome,
    string Mode,
    string? Message,
    ResultDto? Result,
    ExplanationDto? Explanation,
    ClarificationDto? Clarification,
    ConversationStateDto Conversation,
    double DurationMs);

public sealed record ResultDto(string Metric, string Period, string? ComparePeriod, IReadOnlyList<string> GroupColumns, IReadOnlyList<ResultRowDto> Rows, bool Truncated, IReadOnlyList<string> Warnings);
public sealed record ResultRowDto(IReadOnlyList<string> Keys, decimal? Value, decimal? CompareValue);
public sealed record ExplanationDto(string Metric, string Definition, string Formula, string SemanticMeasure, string Owner, string DataOrigin, string? SecurityContext, IReadOnlyList<string> Filters);
public sealed record ClarificationDto(string Term, string Question, IReadOnlyList<MetricOptionDto> Options);
public sealed record MetricOptionDto(string Id, string Name, string Description);

public sealed record MetricDto(string Id, string BusinessName, string Description, string Formula, string SemanticMeasure, string BusinessOwner, IReadOnlyList<string> RelatedMetrics, bool ApprovedForConversationalQuerying, IReadOnlyList<string> Synonyms, string? AmbiguityGroup, string? ConfusionNote);

public sealed record ExplanationApiResponse(Guid QueryId, string CorrelationId, string Question, QueryIntent? ParsedIntent, IReadOnlyList<ValidationIssue> Validation, string? SelectedMetric, string? Dax, string? SemanticModel, string Mode, string Provider, string Parser, string Outcome, double ParseMs, double ExecutionMs, double TotalMs);

public sealed record HealthApiResponse(string Mode, bool Healthy, IReadOnlyList<ComponentDto> Components, DateTimeOffset? LastSuccessfulQuery, int RecentErrors, double? AverageDurationMs);
public sealed record ComponentDto(string Component, bool Healthy, string Status, double? LatencyMs, string? Detail);
