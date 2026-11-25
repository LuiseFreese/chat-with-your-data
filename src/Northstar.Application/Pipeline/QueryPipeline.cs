using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Northstar.Application.Abstractions;
using Northstar.Domain.Dax;
using Northstar.Domain.Metrics;
using Northstar.Domain.Querying;
using Northstar.Domain.Results;
using Northstar.Domain.Security;

namespace Northstar.Application.Pipeline;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<QueryOutcome>))]
public enum QueryOutcome { Answer, ClarificationRequired, Rejected, Error }

public sealed record QueryRequest(string Question, ConversationContext Context, string? SelectedMetricId = null);

public sealed record QueryResponse(
    QueryOutcome Outcome,
    QueryExecutionRecord Record,
    QueryResult? Result,
    ResultExplanation? Explanation,
    ClarificationRequest? Clarification,
    ValidatedQuery? Query,
    string? Message,
    ConversationContext NextContext);

/// <summary>
/// The governed pipeline: parse → validate → (clarify) → build DAX → execute → explain. The parser only
/// proposes; everything that reaches a data provider has been produced by deterministic server-side code.
/// </summary>
public sealed class QueryPipeline(
    IQueryIntentParser parser,
    IAnalyticsDataProvider provider,
    IUserContextAccessor userAccessor,
    IntentValidator validator,
    DaxQueryBuilder daxBuilder,
    IQueryAuditStore audit,
    ILogger<QueryPipeline> logger)
{
    public static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(30);

    public async Task<QueryResponse> ExecuteAsync(QueryRequest request, CancellationToken ct)
    {
        var total = Stopwatch.StartNew();
        var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
        var user = userAccessor.Current;
        var recordId = Guid.NewGuid();
        var question = request.Question.Trim();
        if (question.Length > 500) question = question[..500];

        using var scope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId, ["User"] = user.UserPrincipalName });

        QueryIntent? intent = null;
        var parse = Stopwatch.StartNew();
        try
        {
            // Resuming after a clarification: reuse the pending intent, only swap the metric.
            if (request.SelectedMetricId is not null && request.Context.PendingIntent is not null)
            {
                intent = request.Context.PendingIntent with { Metric = request.SelectedMetricId, ClarificationRequired = false };
                question = request.Context.PendingQuestion ?? question;
            }
            else
            {
                intent = await parser.ParseAsync(question, request.Context, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Intent parsing failed");
            return Fail(recordId, correlationId, question, user, null, [], parse.Elapsed, total.Elapsed, "Intent parsing failed", "The question could not be interpreted right now. Please try again.", request.Context);
        }
        parse.Stop();

        var outcome = validator.Validate(intent);

        if (outcome.NeedsClarification)
        {
            var record = BuildRecord(recordId, correlationId, question, user, intent, outcome.Issues, null, null, parse.Elapsed, TimeSpan.Zero, total.Elapsed, "Clarification required", null);
            audit.Add(record);
            var next = new ConversationContext(question, intent, null, Append(request.Context.RecentQuestions, question));
            return new QueryResponse(QueryOutcome.ClarificationRequired, record, null, null, outcome.Clarification, null, outcome.Clarification!.Question, next);
        }

        if (!outcome.IsValid)
        {
            var errors = outcome.Issues.Where(i => i.Severity == ValidationSeverity.Error).Select(i => i.Message);
            var record = BuildRecord(recordId, correlationId, question, user, intent, outcome.Issues, null, null, parse.Elapsed, TimeSpan.Zero, total.Elapsed, "Rejected by validation", null);
            audit.Add(record);
            logger.LogWarning("Query rejected: {Issues}", string.Join("; ", errors));
            return new QueryResponse(QueryOutcome.Rejected, record, null, null, null, null,
                "This request cannot be executed: " + string.Join(" ", errors), ClearPending(request.Context, question));
        }

        var query = outcome.Query!;
        var dax = daxBuilder.Build(query);
        var exec = Stopwatch.StartNew();
        QueryResult result;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ExecutionTimeout);
            result = await provider.ExecuteAsync(query, dax, user, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail(recordId, correlationId, question, user, intent, outcome.Issues, parse.Elapsed, total.Elapsed, "Timeout", $"The data source did not answer within {ExecutionTimeout.TotalSeconds:0} seconds.", request.Context, dax, query);
        }
        catch (DataProviderException ex)
        {
            logger.LogError(ex, "Data provider failed: {Code}", ex.Code);
            return Fail(recordId, correlationId, question, user, intent, outcome.Issues, parse.Elapsed, total.Elapsed, ex.Code, ex.UserMessage, request.Context, dax, query);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected provider failure");
            return Fail(recordId, correlationId, question, user, intent, outcome.Issues, parse.Elapsed, total.Elapsed, "ProviderError", "The query could not be executed. The incident has been logged.", request.Context, dax, query);
        }
        exec.Stop();

        var explanation = new ResultExplanation(
            query.Metric.BusinessName, query.Metric.Description, query.Metric.Formula, query.Metric.SemanticMeasure, query.Metric.BusinessOwner,
            query.Period.Label, query.ComparePeriod?.Label,
            query.Filters.Select(f => $"{f.Dimension.BusinessName} = {f.Value}").ToList(),
            query.Dimensions.Select(d => d.BusinessName).ToList(),
            provider.DataOrigin,
            user.IsSimulated ? $"Simulated persona {user.DisplayName} – {user.AccessDescription}" : $"Signed-in user {user.UserPrincipalName}; row-level security enforced by the semantic model");

        var ok = BuildRecord(recordId, correlationId, question, user, intent, outcome.Issues, query, dax, parse.Elapsed, exec.Elapsed, total.Elapsed, $"Answer ({result.Rows.Count} rows)", null);
        audit.Add(ok);
        return new QueryResponse(QueryOutcome.Answer, ok, result, explanation, null, query, null, ClearPending(request.Context, question));
    }

    private QueryResponse Fail(Guid id, string correlationId, string question, UserContext user, QueryIntent? intent, IReadOnlyList<ValidationIssue> issues,
        TimeSpan parse, TimeSpan total, string error, string userMessage, ConversationContext ctx, DaxQuery? dax = null, ValidatedQuery? query = null)
    {
        var record = BuildRecord(id, correlationId, question, user, intent, issues, query, dax, parse, TimeSpan.Zero, total, "Error", error);
        audit.Add(record);
        return new QueryResponse(QueryOutcome.Error, record, null, null, null, query, userMessage, ClearPending(ctx, question));
    }

    private QueryExecutionRecord BuildRecord(Guid id, string correlationId, string question, UserContext user, QueryIntent? intent, IReadOnlyList<ValidationIssue> issues,
        ValidatedQuery? query, DaxQuery? dax, TimeSpan parse, TimeSpan exec, TimeSpan total, string outcome, string? error) => new()
    {
        Id = id, CorrelationId = correlationId, Timestamp = DateTimeOffset.UtcNow, Question = question,
        Mode = provider.Mode.ToString(), ParserName = parser.Name, ProviderName = provider.Name, User = user.UserPrincipalName,
        Intent = intent, ValidationIssues = issues, SelectedMetric = query?.Metric.BusinessName, Dax = dax?.Text, SemanticModel = dax?.SemanticModel,
        ParseDuration = parse, ExecutionDuration = exec, TotalDuration = total, Outcome = outcome, Error = error,
    };

    private static ConversationContext ClearPending(ConversationContext ctx, string question) =>
        new(null, null, null, Append(ctx.RecentQuestions, question));

    private static IReadOnlyList<string> Append(IReadOnlyList<string> list, string q) => list.Append(q).TakeLast(10).ToList();
}

public sealed class DataProviderException(string code, string userMessage, string? detail = null, Exception? inner = null)
    : Exception(detail ?? userMessage, inner)
{
    public string Code { get; } = code;
    public string UserMessage { get; } = userMessage;
}

