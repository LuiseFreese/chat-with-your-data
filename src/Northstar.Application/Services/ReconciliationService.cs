using System.Collections.Concurrent;
using Northstar.Application.Abstractions;
using Northstar.Domain.Dax;
using Northstar.Domain.Data;
using Northstar.Domain.Metrics;
using Northstar.Domain.Querying;
using Northstar.Domain.Security;

namespace Northstar.Application.Services;

public sealed class InMemoryQueryAuditStore : IQueryAuditStore
{
    private readonly ConcurrentDictionary<Guid, QueryExecutionRecord> _records = new();
    public void Add(QueryExecutionRecord record) => _records[record.Id] = record;
    public QueryExecutionRecord? Get(Guid id) => _records.GetValueOrDefault(id);
    public IReadOnlyList<QueryExecutionRecord> Recent(int count = 50) => _records.Values.OrderByDescending(r => r.Timestamp).Take(count).ToList();
}

public sealed record ReconciliationLine(string MetricId, string Name, decimal Value, string Definition, string Owner);

public sealed record ReconciliationReport(
    Period Period,
    IReadOnlyList<ReconciliationLine> Lines,
    decimal GrossRevenue,
    decimal TotalDiscounts,
    decimal TotalReturns,
    decimal NetRevenue,
    decimal PipelineAdjustments,
    decimal ExcelAdjustments,
    decimal AdjustedNetRevenueWithoutExcel,
    decimal AdjustedNetRevenue,
    IReadOnlyList<FinanceAdjustment> ExcelAdjustmentDetails,
    string DataOrigin);

/// <summary>
/// Explains why Sales, Finance and Management report different numbers. Uses the same provider and the same
/// measure definitions as the chat – there is no second calculation path.
/// </summary>
public sealed class ReconciliationService(IAnalyticsDataProvider provider, IUserContextAccessor user, MetricCatalog catalog, DaxQueryBuilder dax, SyntheticDataset dataset)
{
    public async Task<ReconciliationReport> BuildAsync(PeriodSpec periodSpec, string? region, CancellationToken ct)
    {
        var period = Period.Resolve(periodSpec);
        var filters = new List<DimensionFilter>();
        if (region is not null)
        {
            var dim = catalog.FindDimension("region")!;
            var value = dim.KnownValues.FirstOrDefault(v => v.Equals(region, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Unknown region");
            filters.Add(new DimensionFilter(dim, value));
        }

        async Task<decimal> Measure(string id)
        {
            var metric = catalog.FindMetric(id)!;
            var q = new ValidatedQuery(metric, [], period, null, filters, null, VisualizationKind.Number);
            var r = await provider.ExecuteAsync(q, dax.Build(q), user.Current, ct);
            return r.Rows.FirstOrDefault()?.Value ?? 0m;
        }

        var gross = await Measure("gross_revenue");
        var discounts = await Measure("total_discounts");
        var returns = await Measure("total_returns");
        var net = await Measure("net_revenue");
        var adjAll = await Measure("finance_adjustments");
        var adjPipeline = await Measure("finance_adjustments_pipeline_only");
        var adjusted = await Measure("adjusted_net_revenue");

        var lines = new[] { "gross_revenue", "net_revenue", "adjusted_net_revenue" }
            .Select(id => catalog.FindMetric(id)!)
            .Select(m => new ReconciliationLine(m.Id, m.BusinessName, m.Id switch { "gross_revenue" => gross, "net_revenue" => net, _ => adjusted }, m.Description, m.BusinessOwner))
            .ToList();

        var current = user.Current;
        var details = dataset.Adjustments
            .Where(a => a.Source == AdjustmentSource.Excel && period.Contains(a.Date) && current.CanSee(a.Region) && (region is null || a.Region.Equals(region, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(a => a.Date).ToList();

        return new ReconciliationReport(period, lines, gross, discounts, returns, net, adjPipeline, adjAll - adjPipeline, net + adjPipeline, adjusted, details, provider.DataOrigin);
    }
}
