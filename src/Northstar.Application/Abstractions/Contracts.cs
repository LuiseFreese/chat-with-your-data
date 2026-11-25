using Northstar.Domain.Dax;
using Northstar.Domain.Querying;
using Northstar.Domain.Results;
using Northstar.Domain.Security;

namespace Northstar.Application.Abstractions;

public enum ExecutionMode { LocalDemo, Connected }

/// <summary>Turns a natural-language question into a structured intent. Never executes anything.</summary>
public interface IQueryIntentParser
{
    string Name { get; }
    Task<QueryIntent> ParseAsync(string question, ConversationContext context, CancellationToken ct);
}

/// <summary>Conversation state kept across a clarification round-trip.</summary>
public sealed record ConversationContext(
    string? PendingQuestion,
    QueryIntent? PendingIntent,
    string? SelectedMetricId,
    IReadOnlyList<string> RecentQuestions)
{
    public static readonly ConversationContext Empty = new(null, null, null, []);
}

/// <summary>Executes a validated query. Implementations: Fabric (Execute Queries API) and Local Demo.</summary>
public interface IAnalyticsDataProvider
{
    string Name { get; }
    ExecutionMode Mode { get; }
    /// <summary>Human-readable origin shown next to every result, e.g. "Fabric semantic model CWD-GovernedModel".</summary>
    string DataOrigin { get; }
    Task<QueryResult> ExecuteAsync(ValidatedQuery query, DaxQuery dax, UserContext user, CancellationToken ct);
    Task<ProviderHealth> CheckHealthAsync(CancellationToken ct);
}

public sealed record ProviderHealth(bool Healthy, string Status, TimeSpan? Latency = null, string? Detail = null);

/// <summary>Resolves the identity a query runs for (Entra sign-in or simulated persona).</summary>
public interface IUserContextAccessor
{
    UserContext Current { get; }
}

public interface IQueryAuditStore
{
    void Add(QueryExecutionRecord record);
    QueryExecutionRecord? Get(Guid id);
    IReadOnlyList<QueryExecutionRecord> Recent(int count = 50);
}

/// <summary>Everything the Query Inspector shows for one request.</summary>
public sealed record QueryExecutionRecord
{
    public required Guid Id { get; init; }
    public required string CorrelationId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string Question { get; init; }
    public required string Mode { get; init; }
    public required string ParserName { get; init; }
    public required string ProviderName { get; init; }
    public required string User { get; init; }
    public QueryIntent? Intent { get; init; }
    public IReadOnlyList<ValidationIssue> ValidationIssues { get; init; } = [];
    public string? SelectedMetric { get; init; }
    public string? Dax { get; init; }
    public string? SemanticModel { get; init; }
    public TimeSpan ParseDuration { get; init; }
    public TimeSpan ExecutionDuration { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public string Outcome { get; init; } = "";
    public string? Error { get; init; }
}
