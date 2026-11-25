using System.Globalization;
using System.Text;
using Northstar.Domain.Data;

namespace Northstar.Fabric.Tool;

/// <summary>Turns the deterministic dataset into T-SQL INSERT batches for the Fabric Warehouse and CSV seed files.</summary>
public static class SqlSeedWriter
{
    private const int BatchSize = 1000;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static int RegionKey(string region) => Array.IndexOf(SyntheticDataset.Regions, region) + 1;
    public static int BusinessUnitKey(string bu) => Array.IndexOf(SyntheticDataset.BusinessUnits, bu) + 1;

    public static IEnumerable<(string Name, string Sql)> Scripts(SyntheticDataset d, bool includeExcelAdjustments)
    {
        yield return ("02_seed_dimensions", Dimensions(d));
        yield return ("03_seed_fact_sales", Batches("cwd.FactSalesOrderLine", "[OrderLineKey],[Order Id],[Order Date],[CustomerKey],[ProductKey],[RegionKey],[BusinessUnitKey],[Quantity],[Gross Amount],[Discount Amount],[Order Status]",
            d.OrderLines.Select(l => $"({l.Key},{l.OrderId},{D(l.OrderDate)},{l.CustomerKey},{l.ProductKey},{RegionKey(l.Region)},{BusinessUnitKey(l.BusinessUnit)},{l.Quantity},{M(l.GrossAmount)},{M(l.DiscountAmount)},{S(l.OrderStatus)})")));
        yield return ("04_seed_fact_returns", Batches("cwd.FactReturn", "[ReturnKey],[Order Id],[Return Date],[RegionKey],[BusinessUnitKey],[Amount],[Reason]",
            d.Returns.Select(r => $"({r.Key},{r.OrderId},{D(r.ReturnDate)},{RegionKey(r.Region)},{BusinessUnitKey(r.BusinessUnit)},{M(r.Amount)},{S(r.Reason)})")));
        var adj = d.Adjustments.Where(a => includeExcelAdjustments || a.Source == AdjustmentSource.Pipeline);
        yield return ("05_seed_fact_adjustments_pipeline", Batches("cwd.FactFinanceAdjustment", "[AdjustmentKey],[Adjustment Date],[RegionKey],[BusinessUnitKey],[Amount],[Reason],[Source],[Approved By],[Status],[Comment],[Loaded At]",
            adj.Select(a => $"({a.Key},{D(a.Date)},{RegionKey(a.Region)},{BusinessUnitKey(a.BusinessUnit)},{M(a.Amount)},{S(a.Reason)},{S(a.Source)},{S(a.ApprovedBy)},{S(a.Status)},{S(a.Comment)},SYSUTCDATETIME())")));
    }

    private static string Dimensions(SyntheticDataset d)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- Date dimension 2025-01-01 .. 2026-12-31");
        var dates = new List<string>();
        for (var day = new DateOnly(2025, 1, 1); day <= new DateOnly(2026, 12, 31); day = day.AddDays(1))
        {
            var q = (day.Month - 1) / 3 + 1;
            dates.Add($"({D(day)},{day.Year},{q},'Q{q} {day.Year}',{day.Month},'{day:yyyy-MM}',{S(day.ToString("MMMM", Inv))},{day.Year * 100 + day.Month})");
        }
        sb.AppendLine(Batches("cwd.DimDate", "[Date],[Year],[Quarter],[Quarter Label],[Month],[Month Label],[Month Name],[Year Month]", dates));
        sb.AppendLine(Batches("cwd.DimRegion", "[RegionKey],[Region]", SyntheticDataset.Regions.Select(r => $"({RegionKey(r)},{S(r)})")));
        sb.AppendLine(Batches("cwd.DimBusinessUnit", "[BusinessUnitKey],[Business Unit]", SyntheticDataset.BusinessUnits.Select(b => $"({BusinessUnitKey(b)},{S(b)})")));
        sb.AppendLine(Batches("cwd.DimCustomer", "[CustomerKey],[Customer Name],[Segment],[Region]", d.Customers.Select(c => $"({c.Key},{S(c.Name)},{S(c.Segment)},{S(c.Region)})")));
        sb.AppendLine(Batches("cwd.DimProduct", "[ProductKey],[Product Name],[Category],[Business Unit],[List Price]", d.Products.Select(p => $"({p.Key},{S(p.Name)},{S(p.Category)},{S(p.BusinessUnit)},{M(p.ListPrice)})")));
        return sb.ToString();
    }

    public static string Batches(string table, string columns, IEnumerable<string> rows)
    {
        var sb = new StringBuilder();
        foreach (var chunk in rows.Chunk(BatchSize))
        {
            sb.Append("INSERT INTO ").Append(table).Append(" (").Append(columns).AppendLine(") VALUES");
            sb.AppendLine(string.Join(",\n", chunk)).AppendLine(";");
        }
        return sb.ToString();
    }

    public static string D(DateOnly d) => $"'{d:yyyy-MM-dd}'";
    public static string M(decimal m) => m.ToString("0.00", Inv);
    public static string S(string s) => "'" + s.Replace("'", "''") + "'";

    public static string Csv<T>(IEnumerable<T> rows, string header, Func<T, string> line) =>
        header + "\n" + string.Join("\n", rows.Select(line)) + "\n";
}
