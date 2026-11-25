namespace Northstar.Domain.Metrics;

/// <summary>
/// Central, server-owned definition of what may be asked. Loaded once; immutable afterwards.
/// </summary>
public sealed class MetricCatalog
{
    private readonly Dictionary<string, MetricDefinition> _metrics;
    private readonly Dictionary<string, DimensionDefinition> _dimensions;
    private readonly List<AmbiguousTerm> _ambiguousTerms;

    public const string SemanticModelName = "CWD-GovernedModel";
    public const string DateTable = "Date";
    public const string DateColumn = "Date";
    public const string YearColumn = "Year";
    public const string QuarterColumn = "Quarter Label";

    public MetricCatalog(IEnumerable<MetricDefinition> metrics, IEnumerable<DimensionDefinition> dimensions, IEnumerable<AmbiguousTerm> ambiguousTerms)
    {
        _metrics = metrics.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        _dimensions = dimensions.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        _ambiguousTerms = ambiguousTerms.ToList();
    }

    public IReadOnlyCollection<MetricDefinition> Metrics => _metrics.Values;
    public IReadOnlyCollection<DimensionDefinition> Dimensions => _dimensions.Values;
    public IReadOnlyList<AmbiguousTerm> AmbiguousTerms => _ambiguousTerms;

    public MetricDefinition? FindMetric(string? id) => id is null ? null : _metrics.GetValueOrDefault(id);
    public DimensionDefinition? FindDimension(string? id) => id is null ? null : _dimensions.GetValueOrDefault(id);
    public AmbiguousTerm? FindAmbiguousTerm(string? term) =>
        term is null ? null : _ambiguousTerms.FirstOrDefault(t =>
            t.Term.Equals(term, StringComparison.OrdinalIgnoreCase) ||
            t.Synonyms.Contains(term, StringComparer.OrdinalIgnoreCase));

    /// <summary>Resolves a free-text business term to a metric id or an ambiguous group. Used by the deterministic parser.</summary>
    public (MetricDefinition? Metric, AmbiguousTerm? Ambiguous) ResolveTerm(string text)
    {
        var lower = text.ToLowerInvariant();
        // Specific metric synonyms win over generic ambiguous terms (e.g. "adjusted net revenue" before "revenue").
        var metric = _metrics.Values
            .SelectMany(m => m.Synonyms.Append(m.BusinessName).Select(s => (m, s: s.ToLowerInvariant())))
            .Where(x => lower.Contains(x.s))
            .OrderByDescending(x => x.s.Length)
            .Select(x => x.m)
            .FirstOrDefault();
        if (metric is not null) return (metric, null);

        var ambiguous = _ambiguousTerms
            .SelectMany(t => t.Synonyms.Append(t.Term).Select(s => (t, s: s.ToLowerInvariant())))
            .Where(x => lower.Contains(x.s))
            .OrderByDescending(x => x.s.Length)
            .Select(x => x.t)
            .FirstOrDefault();
        return (null, ambiguous);
    }

    public static MetricCatalog CreateDefault()
    {
        var metrics = new List<MetricDefinition>
        {
            new("gross_revenue", "Gross Revenue",
                "Order value at list price for all orders that were not cancelled, before discounts, returns and finance corrections. This is the number Sales reports.",
                "SUM(order line quantity × list price) WHERE OrderStatus <> 'Cancelled'",
                "Gross Revenue", "Head of Sales", ["net_revenue", "total_discounts"], true,
                ["gross revenue", "gross sales", "sales revenue", "bookings", "order value", "gross order value"],
                AmbiguityGroup: "revenue",
                ConfusionNote: "Sales calls this 'revenue'. Finance never does.", LineGrain: true),
            new("total_discounts", "Total Discounts",
                "Sum of all discounts granted on order lines (volume, contract and campaign discounts).",
                "SUM(order line discount amount)", "Total Discounts", "Head of Sales", ["gross_revenue", "net_revenue"], true,
                ["discounts", "total discounts", "discount amount", "rebates"], LineGrain: true),
            new("total_returns", "Total Returns",
                "Value of goods returned by customers, booked as credit notes.",
                "SUM(credit note amount)", "Total Returns", "Head of Customer Service", ["net_revenue"], true,
                ["returns", "total returns", "credit notes", "refunds"]),
            new("net_revenue", "Net Revenue",
                "Gross Revenue minus discounts and returns. The number Finance reports in the monthly close – before manual corrections.",
                "[Gross Revenue] − [Total Discounts] − [Total Returns]", "Net Revenue", "Head of Finance",
                ["gross_revenue", "adjusted_net_revenue"], true,
                ["net revenue", "net sales", "revenue after discounts", "invoiced revenue", "finance revenue"],
                AmbiguityGroup: "revenue",
                ConfusionNote: "Operational apps often use 'net' to mean 'after discounts' but forget returns."),
            new("finance_adjustments", "Finance Adjustments",
                "Approved manual corrections booked by Finance (contract true-ups, pricing errors, bad debt). Historically maintained in an Excel workbook outside the pipeline.",
                "SUM(adjustment amount) WHERE Status = 'Approved'", "Finance Adjustments", "Head of Finance",
                ["adjusted_net_revenue", "net_revenue"], true,
                ["finance adjustments", "adjustments", "corrections", "manual corrections", "excel corrections"]),
            new("adjusted_net_revenue", "Adjusted Net Revenue",
                "Net Revenue including approved finance adjustments. The number Management steers the company with.",
                "[Net Revenue] + [Finance Adjustments]", "Adjusted Net Revenue", "CFO",
                ["net_revenue", "finance_adjustments"], true,
                ["adjusted net revenue", "adjusted revenue", "management revenue", "revenue incl. adjustments", "corrected revenue"],
                AmbiguityGroup: "revenue"),
            new("order_count", "Order Count",
                "Number of distinct orders that were not cancelled.",
                "DISTINCTCOUNT(OrderId) WHERE OrderStatus <> 'Cancelled'", "Order Count", "Head of Sales",
                ["average_order_value"], true, ["orders", "order count", "number of orders", "how many orders"], FormatString: "#,##0", LineGrain: true),
            new("average_order_value", "Average Order Value",
                "Net Revenue divided by Order Count.",
                "DIVIDE([Net Revenue], [Order Count])", "Average Order Value", "Head of Sales",
                ["net_revenue", "order_count"], true, ["average order value", "aov", "avg order", "basket size"]),
            new("finance_adjustments_pipeline_only", "Finance Adjustments (pipeline feed only)",
                "Finance adjustments that reached the pipeline before the Excel workbook was reconciled. Used by the Data Reconciliation screen to show the gap; not offered in chat.",
                "SUM(adjustment amount) WHERE Status = 'Approved' AND Source = 'Pipeline'", "Finance Adjustments Pipeline Only", "Head of Finance",
                ["finance_adjustments"], false, []),
            new("customer_contact_details", "Customer Contact Details",
                "Personal contact data of customers. Not a metric – exists in the catalog only to document that it is blocked.",
                "n/a", "n/a", "Data Protection Officer", [], false,
                ["contact details", "email addresses", "phone numbers", "customer records", "personal data", "private contact"]),
        };

        var dimensions = new List<DimensionDefinition>
        {
            new("region", "Region", "Region", "Region", "Sales region of the customer", ["East", "North", "South", "West"], ["region", "regions", "regional", "area"]),
            new("business_unit", "Business Unit", "Business Unit", "Business Unit", "Legal/operational unit that booked the order",
                ["Northstar Bikes", "Northstar Parts", "Northstar Services"], ["business unit", "business units", "bu", "division"]),
            new("product_category", "Product Category", "Product", "Category", "Product category",
                ["City", "E-Bike", "Gravel", "Road", "Urban", "Components", "Accessories", "Service"], ["category", "categories", "product category", "product categories", "product line"], LineGrain: true),
            new("customer_segment", "Customer Segment", "Customer", "Segment", "Commercial segment of the customer",
                ["Business", "Dealer", "Retail"], ["segment", "segments", "customer segment", "customer type"], LineGrain: true),
            new("quarter", "Quarter", DateTable, QuarterColumn, "Calendar quarter", [], ["quarter", "quarters", "quarterly", "per quarter"]),
            new("month", "Month", DateTable, "Month Label", "Calendar month", [], ["month", "months", "monthly", "per month"]),
        };

        var ambiguous = new List<AmbiguousTerm>
        {
            new("revenue", "Which definition of revenue would you like to use?",
                ["gross_revenue", "net_revenue", "adjusted_net_revenue"],
                ["revenue", "turnover", "sales", "umsatz", "how much did we make", "how much did we earn"]),
        };

        return new MetricCatalog(metrics, dimensions, ambiguous);
    }
}

