namespace Northstar.Domain.Data;

public sealed record Customer(int Key, string Name, string Segment, string Region, string ContactEmail);
public sealed record Product(int Key, string Name, string Category, string BusinessUnit, decimal ListPrice);
public sealed record OrderLine(int Key, int OrderId, DateOnly OrderDate, int CustomerKey, int ProductKey, string Region, string BusinessUnit, string Category, string Segment, int Quantity, decimal GrossAmount, decimal DiscountAmount, string OrderStatus);
public sealed record Return(int Key, int OrderId, DateOnly ReturnDate, string Region, string BusinessUnit, decimal Amount, string Reason);
public sealed record FinanceAdjustment(int Key, DateOnly Date, string Region, string BusinessUnit, decimal Amount, string Reason, string Source, string ApprovedBy, string Status, string Comment);

public static class AdjustmentSource
{
    public const string Pipeline = "Pipeline";
    public const string Excel = "Excel";
}

public sealed class SyntheticDataset
{
    public required IReadOnlyList<Customer> Customers { get; init; }
    public required IReadOnlyList<Product> Products { get; init; }
    public required IReadOnlyList<OrderLine> OrderLines { get; init; }
    public required IReadOnlyList<Return> Returns { get; init; }
    public required IReadOnlyList<FinanceAdjustment> Adjustments { get; init; }
    public required int Seed { get; init; }

    public static readonly string[] Regions = ["East", "North", "South", "West"];
    public static readonly string[] BusinessUnits = ["Northstar Bikes", "Northstar Parts", "Northstar Services"];
    public static readonly string[] Segments = ["Business", "Dealer", "Retail"];
}
