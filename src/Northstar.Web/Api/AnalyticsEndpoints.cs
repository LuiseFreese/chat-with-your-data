using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Northstar.Application.Abstractions;
using Northstar.Application.Pipeline;
using Northstar.Application.Services;
using Northstar.Domain.Metrics;
using Northstar.Domain.Querying;

namespace Northstar.Web.Api;

/// <summary>
/// Operational API. Every endpoint goes through the same pipeline/catalog as the chat UI – there is no second
/// calculation path for other consumers (Power Apps, agents, …).
/// </summary>
public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithOpenApi();

        api.MapPost("/query", async Task<Results<Ok<QueryApiResponse>, BadRequest<string>>> ([FromBody] QueryApiRequest request, QueryPipeline pipeline, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 500)
                return TypedResults.BadRequest("Question must be between 1 and 500 characters.");
            var context = request.Conversation?.ToContext() ?? ConversationContext.Empty;
            var response = await pipeline.ExecuteAsync(new QueryRequest(request.Question, context, request.SelectedMetricId), ct);
            return TypedResults.Ok(Map(response));
        })
        .WithName("ExecuteQuery")
        .WithSummary("Answers a natural-language question through the governed pipeline (parse → validate → clarify → DAX → execute).");

        api.MapGet("/query/{id:guid}/explanation", Results<Ok<ExplanationApiResponse>, NotFound> (Guid id, IQueryAuditStore audit) =>
        {
            var r = audit.Get(id);
            if (r is null) return TypedResults.NotFound();
            return TypedResults.Ok(new ExplanationApiResponse(r.Id, r.CorrelationId, r.Question, r.Intent, r.ValidationIssues, r.SelectedMetric, r.Dax, r.SemanticModel,
                r.Mode, r.ProviderName, r.ParserName, r.Outcome, r.ParseDuration.TotalMilliseconds, r.ExecutionDuration.TotalMilliseconds, r.TotalDuration.TotalMilliseconds));
        })
        .WithName("GetQueryExplanation")
        .WithSummary("Returns the technical trace of a previous query: parsed intent, validation, generated DAX, timings.");

        api.MapGet("/metrics", (MetricCatalog catalog) => catalog.Metrics.Select(Map).OrderBy(m => m.BusinessName).ToList())
           .WithName("ListMetrics").WithSummary("Lists the central metric catalog.");

        api.MapGet("/metrics/{id}", Results<Ok<MetricDto>, NotFound> (string id, MetricCatalog catalog) =>
            catalog.FindMetric(id) is { } m ? TypedResults.Ok(Map(m)) : TypedResults.NotFound())
           .WithName("GetMetric");

        api.MapGet("/system/health", async (SystemHealthService health, CancellationToken ct) =>
        {
            var s = await health.GetAsync(ct);
            return new HealthApiResponse(s.Mode.ToString(), s.Components.All(c => c.Healthy),
                s.Components.Select(c => new ComponentDto(c.Component, c.Healthy, c.Status, c.Latency?.TotalMilliseconds, c.Detail)).ToList(),
                s.LastSuccessfulQuery?.Timestamp, s.RecentErrors.Count, s.AverageDuration?.TotalMilliseconds);
        })
        .WithName("GetSystemHealth");

        api.MapGet("/reconciliation", async (ReconciliationService service, int year, int? quarter, string? region, CancellationToken ct) =>
        {
            var spec = quarter is null ? PeriodSpec.OfYear(year) : PeriodSpec.OfQuarter(year, quarter.Value);
            var r = await service.BuildAsync(spec, region, ct);
            return new
            {
                period = r.Period.Label,
                r.GrossRevenue, r.TotalDiscounts, r.TotalReturns, r.NetRevenue, r.PipelineAdjustments, r.ExcelAdjustments, r.AdjustedNetRevenueWithoutExcel, r.AdjustedNetRevenue,
                dataOrigin = r.DataOrigin,
                excelAdjustmentDetails = r.ExcelAdjustmentDetails.Select(a => new { a.Key, a.Date, a.Region, a.BusinessUnit, a.Amount, a.Reason, a.ApprovedBy, a.Status }),
            };
        })
        .WithName("GetReconciliation").WithSummary("Bridge from Gross Revenue to Adjusted Net Revenue including the Excel corrections.");

        return app;
    }

    public static QueryApiResponse Map(QueryResponse r) => new(
        r.Record.Id, r.Record.CorrelationId, r.Outcome, r.Record.Mode, r.Message,
        r.Result is null ? null : new ResultDto(r.Query!.Metric.BusinessName, r.Result.ValueLabel, r.Result.CompareValueLabel, r.Result.GroupColumns,
            r.Result.Rows.Select(x => new ResultRowDto(x.Keys, x.Value, x.CompareValue)).ToList(), r.Result.Truncated, r.Result.Warnings),
        r.Explanation is null ? null : new ExplanationDto(r.Explanation.MetricName, r.Explanation.MetricDefinition, r.Explanation.Formula, r.Explanation.SemanticMeasure, r.Explanation.BusinessOwner, r.Explanation.DataOrigin, r.Explanation.SecurityContext, r.Explanation.AppliedFilters),
        r.Clarification is null ? null : new ClarificationDto(r.Clarification.Term, r.Clarification.Question, r.Clarification.Options.Select(o => new MetricOptionDto(o.Id, o.BusinessName, o.Description)).ToList()),
        ConversationStateDto.From(r.NextContext),
        r.Record.TotalDuration.TotalMilliseconds);

    private static MetricDto Map(MetricDefinition m) => new(m.Id, m.BusinessName, m.Description, m.Formula, m.SemanticMeasure, m.BusinessOwner, m.RelatedMetrics, m.ApprovedForConversationalQuerying, m.Synonyms, m.AmbiguityGroup, m.ConfusionNote);
}
