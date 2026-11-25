using System.Diagnostics;
using Northstar.Application.Abstractions;
using Northstar.Application.Services;
using Northstar.Domain.Dax;
using Northstar.Domain.Data;
using Northstar.Domain.Querying;
using Northstar.Domain.Results;
using Northstar.Domain.Security;

namespace Northstar.Infrastructure.Local;

/// <summary>
/// Local Demo Mode provider. Evaluates the validated query against the in-memory synthetic dataset using the
/// same measure definitions as the governed semantic model. The DAX is produced and shown but – obviously –
/// not executed here. Results are labelled as local; they never claim to come from Fabric.
/// </summary>
public sealed class LocalDemoDataProvider(LocalMetricEvaluator evaluator) : IAnalyticsDataProvider, IHealthContributor
{
    public string Name => "LocalDemoDataProvider";
    public ExecutionMode Mode => ExecutionMode.LocalDemo;
    public string DataOrigin => $"Local synthetic dataset (seed {evaluator.Data.Seed}) – NOT Fabric";
    public string Component => "Local synthetic dataset";

    public Task<QueryResult> ExecuteAsync(ValidatedQuery query, DaxQuery dax, UserContext user, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(evaluator.Evaluate(query, user));
    }

    public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var d = evaluator.Data;
        return Task.FromResult(new ProviderHealth(true, "Loaded", sw.Elapsed, $"{d.OrderLines.Count:N0} order lines, {d.Returns.Count:N0} returns, {d.Adjustments.Count:N0} adjustments"));
    }

    Task<ProviderHealth> IHealthContributor.CheckAsync(CancellationToken ct) => CheckHealthAsync(ct);
}

/// <summary>Prototype source: "revenue" = gross minus discounts over the synthetic order table; returns, adjustments and RLS do not exist here.</summary>
public sealed class LocalPrototypeDataSource(SyntheticDataset data) : IPrototypeDataSource
{
    public string Name => "Prototype: direct access to the synthetic order table (no governance)";
    public string ImplicitRevenueDefinition => "SUM(order amount − discount) over all orders, incl. cancelled; no returns, no finance adjustments";

    public Task<IReadOnlyList<ResultRow>> QueryRevenueAsync(Period? period, string? region, bool groupByRegion, CancellationToken ct)
    {
        var lines = data.OrderLines.Where(l => (period is null || period.Contains(l.OrderDate)) && (region is null || l.Region == region));
        IReadOnlyList<ResultRow> rows = groupByRegion
            ? lines.GroupBy(l => l.Region).OrderBy(g => g.Key).Select(g => new ResultRow([g.Key], g.Sum(l => l.GrossAmount - l.DiscountAmount), null)).ToList()
            : [new ResultRow([], lines.Sum(l => l.GrossAmount - l.DiscountAmount), null)];
        return Task.FromResult(rows);
    }
}

/// <summary>Simulated identity for Local Demo Mode. Switching persona is a UI action and is clearly labelled as simulated.</summary>
public sealed class SimulatedUserContextAccessor : IUserContextAccessor
{
    private UserContext _current = DemoPersonas.Cfo;
    public UserContext Current => _current;
    public void Switch(UserContext persona) => _current = persona;
}
