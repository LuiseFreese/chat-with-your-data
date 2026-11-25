using Northstar.Application.Abstractions;

namespace Northstar.Application.Services;

public interface IHealthContributor
{
    string Component { get; }
    Task<ProviderHealth> CheckAsync(CancellationToken ct);
}

public sealed record ComponentHealth(string Component, bool Healthy, string Status, TimeSpan? Latency, string? Detail, DateTimeOffset CheckedAt);

public sealed record SystemHealthSnapshot(
    ExecutionMode Mode,
    IReadOnlyList<ComponentHealth> Components,
    QueryExecutionRecord? LastSuccessfulQuery,
    IReadOnlyList<QueryExecutionRecord> RecentErrors,
    TimeSpan? AverageDuration,
    int QueriesTotal);

/// <summary>Collects live status from every registered contributor plus the audit trail. Nothing is hardcoded.</summary>
public sealed class SystemHealthService(IEnumerable<IHealthContributor> contributors, IQueryAuditStore audit, IAnalyticsDataProvider provider)
{
    public async Task<SystemHealthSnapshot> GetAsync(CancellationToken ct)
    {
        var results = new List<ComponentHealth>();
        foreach (var c in contributors)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                var h = await c.CheckAsync(cts.Token);
                results.Add(new ComponentHealth(c.Component, h.Healthy, h.Status, h.Latency, h.Detail, DateTimeOffset.UtcNow));
            }
            catch (Exception ex)
            {
                results.Add(new ComponentHealth(c.Component, false, "Check failed", null, ex.GetType().Name, DateTimeOffset.UtcNow));
            }
        }

        var recent = audit.Recent(200);
        var lastOk = recent.FirstOrDefault(r => r.Outcome.StartsWith("Answer"));
        var errors = recent.Where(r => r.Error is not null).Take(10).ToList();
        var avg = recent.Count == 0 ? (TimeSpan?)null : TimeSpan.FromMilliseconds(recent.Average(r => r.TotalDuration.TotalMilliseconds));
        return new SystemHealthSnapshot(provider.Mode, results, lastOk, errors, avg, recent.Count);
    }
}
