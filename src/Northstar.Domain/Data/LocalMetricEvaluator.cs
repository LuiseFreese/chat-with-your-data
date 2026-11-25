using Northstar.Domain.Metrics;
using Northstar.Domain.Querying;
using Northstar.Domain.Results;
using Northstar.Domain.Security;

namespace Northstar.Domain.Data;

/// <summary>
/// Implements the approved measure definitions over the synthetic dataset. This is the local equivalent of the
/// governed semantic model: the same definitions, the same grouping semantics, the same region restriction.
/// </summary>
public sealed class LocalMetricEvaluator(SyntheticDataset data)
{
    public SyntheticDataset Data => data;

    public QueryResult Evaluate(ValidatedQuery q, UserContext user, bool includeExcelAdjustments = true)
    {
        var slice = Slice(q.Filters, user);
        var keysOf = KeySelector(q.Dimensions);

        var groups = q.Dimensions.Count == 0
            ? [Array.Empty<string>()]
            : slice.Lines.Select(l => keysOf(l.Region, l.BusinessUnit, l.Category, l.Segment, l.OrderDate))
            .Concat(slice.Returns.Select(r => keysOf(r.Region, r.BusinessUnit, null, null, r.ReturnDate)))
            .Concat(slice.Adjustments.Select(a => keysOf(a.Region, a.BusinessUnit, null, null, a.Date)))
            .Where(k => k is not null)
            .Distinct(KeyComparer.Instance)
            .ToList();

        var rows = new List<ResultRow>();
        foreach (var key in groups)
        {
            var value = Compute(q.Metric.Id, slice, q.Period, key!, q.Dimensions, includeExcelAdjustments);
            decimal? compare = q.ComparePeriod is null ? null : Compute(q.Metric.Id, slice, q.ComparePeriod, key!, q.Dimensions, includeExcelAdjustments);
            if ((value is null or 0) && (compare is null or 0) && q.Dimensions.Count > 0) continue;
            rows.Add(new ResultRow(key!, value, compare));
        }

        IEnumerable<ResultRow> ordered = q.Dimensions.Any(d => d.SemanticTable == MetricCatalog.DateTable) && q.TopN is null
            ? rows.OrderBy(r => r.Keys[0], StringComparer.Ordinal)
            : rows.OrderByDescending(r => r.Value ?? 0);
        var list = ordered.ToList();
        var truncated = false;
        if (q.TopN is int n && list.Count > n) { list = list.Take(n).ToList(); truncated = true; }

        var warnings = new List<string>();
        if (!user.HasAllRegions) warnings.Add($"Rows limited to regions {user.AccessDescription} for {user.DisplayName} (simulated access).");
        return new QueryResult(q.Dimensions.Select(d => d.BusinessName).ToList(), list, q.Period.Label, q.ComparePeriod?.Label, truncated, warnings);
    }

    /// <summary>Single-number evaluation used by the reconciliation screen and the tests.</summary>
    public decimal Compute(string metricId, Period period, UserContext? user = null, string? region = null, bool includeExcelAdjustments = true)
    {
        var filters = new List<DimensionFilter>();
        var slice = Slice(filters, user ?? DemoPersonas.Cfo, region);
        return Compute(metricId, slice, period, [], [], includeExcelAdjustments) ?? 0m;
    }

    private decimal? Compute(string metricId, DataSlice s, Period p, IReadOnlyList<string> key, IReadOnlyList<DimensionDefinition> dims, bool includeExcel)
    {
        bool InKey(string region, string bu, string? cat, string? seg, DateOnly d)
        {
            if (dims.Count == 0) return true;
            var k = KeySelector(dims)(region, bu, cat, seg, d);
            return k is not null && KeyComparer.Instance.Equals(k, key);
        }

        var lines = s.Lines.Where(l => p.Contains(l.OrderDate) && l.OrderStatus != "Cancelled" && InKey(l.Region, l.BusinessUnit, l.Category, l.Segment, l.OrderDate)).ToList();
        var returns = s.Returns.Where(r => p.Contains(r.ReturnDate) && InKey(r.Region, r.BusinessUnit, null, null, r.ReturnDate)).ToList();
        var adjustments = s.Adjustments.Where(a => p.Contains(a.Date) && a.Status == "Approved" && (includeExcel || a.Source != AdjustmentSource.Excel) && InKey(a.Region, a.BusinessUnit, null, null, a.Date)).ToList();


        decimal gross = lines.Sum(l => l.GrossAmount);
        decimal discounts = lines.Sum(l => l.DiscountAmount);
        decimal ret = returns.Sum(r => r.Amount);
        decimal adj = adjustments.Sum(a => a.Amount);
        decimal adjPipeline = adjustments.Where(a => a.Source == AdjustmentSource.Pipeline).Sum(a => a.Amount);
        decimal net = gross - discounts - ret;
        int orders = lines.Select(l => l.OrderId).Distinct().Count();

        return metricId switch
        {
            "gross_revenue" => gross,
            "total_discounts" => discounts,
            "total_returns" => ret,
            "net_revenue" => net,
            "finance_adjustments" => adj,
            "finance_adjustments_pipeline_only" => adjPipeline,
            "adjusted_net_revenue" => net + adj,
            "order_count" => orders,
            "average_order_value" => orders == 0 ? null : Math.Round(net / orders, 2),
            _ => throw new InvalidOperationException($"Metric '{metricId}' has no local implementation."),
        };
    }

    private DataSlice Slice(IReadOnlyList<DimensionFilter> filters, UserContext user, string? regionOverride = null)
    {
        bool RegionOk(string r) => user.CanSee(r) && (regionOverride is null || r == regionOverride) && filters.Where(f => f.Dimension.Id == "region").All(f => f.Value == r);
        bool BuOk(string b) => filters.Where(f => f.Dimension.Id == "business_unit").All(f => f.Value == b);
        bool CatOk(string c) => filters.Where(f => f.Dimension.Id == "product_category").All(f => f.Value == c);
        bool SegOk(string sg) => filters.Where(f => f.Dimension.Id == "customer_segment").All(f => f.Value == sg);
        var lineGrainFilter = filters.Any(f => f.Dimension.Id is "product_category" or "customer_segment");

        return new DataSlice(
            data.OrderLines.Where(l => RegionOk(l.Region) && BuOk(l.BusinessUnit) && CatOk(l.Category) && SegOk(l.Segment)).ToList(),
            lineGrainFilter ? [] : data.Returns.Where(r => RegionOk(r.Region) && BuOk(r.BusinessUnit)).ToList(),
            lineGrainFilter ? [] : data.Adjustments.Where(a => RegionOk(a.Region) && BuOk(a.BusinessUnit)).ToList());
    }

    private static Func<string, string, string?, string?, DateOnly, IReadOnlyList<string>?> KeySelector(IReadOnlyList<DimensionDefinition> dims) =>
        (region, bu, cat, seg, date) =>
        {
            var keys = new List<string>(dims.Count);
            foreach (var d in dims)
            {
                string? k = d.Id switch
                {
                    "region" => region,
                    "business_unit" => bu,
                    "product_category" => cat,
                    "customer_segment" => seg,
                    "quarter" => $"Q{(date.Month - 1) / 3 + 1} {date.Year}",
                    "month" => $"{date.Year}-{date.Month:00}",
                    _ => null,
                };
                if (k is null) return null;
                keys.Add(k);
            }
            return keys;
        };

    private sealed record DataSlice(List<OrderLine> Lines, List<Return> Returns, List<FinanceAdjustment> Adjustments);

    private sealed class KeyComparer : IEqualityComparer<IReadOnlyList<string>?>
    {
        public static readonly KeyComparer Instance = new();
        public bool Equals(IReadOnlyList<string>? x, IReadOnlyList<string>? y) => x is not null && y is not null && x.SequenceEqual(y);
        public int GetHashCode(IReadOnlyList<string>? obj) => obj is null ? 0 : obj.Aggregate(17, (h, s) => h * 31 + s.GetHashCode());
    }
}

