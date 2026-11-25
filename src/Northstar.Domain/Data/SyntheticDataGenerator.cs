namespace Northstar.Domain.Data;

/// <summary>
/// Deterministic, platform-independent generator (SplitMix64) – the same seed always yields the same dataset,
/// which is what the tests, the Local Demo Mode and the Fabric seed scripts rely on.
/// </summary>
public sealed class SyntheticDataGenerator
{
    public const int DefaultSeed = 20260325;

    private ulong _state;

    private SyntheticDataGenerator(int seed) { _state = (ulong)seed * 0x9E3779B97F4A7C15UL + 0xD1B54A32D192ED03UL; }

    public static SyntheticDataset Generate(int seed = DefaultSeed)
    {
        var g = new SyntheticDataGenerator(seed);
        var customers = g.Customers();
        var products = Products();
        var (lines, returns) = g.Orders(customers, products);
        var adjustments = g.Adjustments();
        return new SyntheticDataset { Customers = customers, Products = products, OrderLines = lines, Returns = returns, Adjustments = adjustments, Seed = seed };
    }

    // ---- PRNG -------------------------------------------------------------------------------------------
    private ulong NextU64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
    private double NextDouble() => (NextU64() >> 11) * (1.0 / (1UL << 53));
    private int NextInt(int minInclusive, int maxExclusive) => minInclusive + (int)(NextDouble() * (maxExclusive - minInclusive));
    private T Pick<T>(IReadOnlyList<T> items) => items[NextInt(0, items.Count)];
    private T PickWeighted<T>(IReadOnlyList<(T Item, double Weight)> items)
    {
        var total = items.Sum(i => i.Weight);
        var r = NextDouble() * total;
        foreach (var (item, w) in items) { if (r < w) return item; r -= w; }
        return items[^1].Item;
    }

    // ---- Dimensions -------------------------------------------------------------------------------------
    private static readonly string[] CompanyA = ["Alpen", "Rhein", "Nord", "Hanse", "Donau", "Elbe", "Main", "Baltic", "Ruhr", "Tauern", "Weser", "Spree", "Isar", "Neckar", "Mosel"];
    private static readonly string[] CompanyB = ["Radsport", "Bikes", "Cycles", "Mobility", "Velo", "Cycling", "Rad", "Zweirad", "Sports", "Outdoor"];
    private static readonly string[] CompanyC = ["GmbH", "AG", "KG", "e.K.", "& Co.", "SE", "B.V.", "S.à r.l."];

    private List<Customer> Customers()
    {
        var list = new List<Customer>();
        var used = new HashSet<string>();
        for (var i = 1; i <= 72; i++)
        {
            string name;
            do { name = $"{Pick(CompanyA)} {Pick(CompanyB)} {Pick(CompanyC)}"; } while (!used.Add(name));
            var region = SyntheticDataset.Regions[(i - 1) % 4];
            var segment = PickWeighted<string>([("Dealer", 0.45), ("Business", 0.3), ("Retail", 0.25)]);
            var email = $"contact{i:000}@{name.Split(' ')[0].ToLowerInvariant()}-{name.Split(' ')[1].ToLowerInvariant()}.example";
            list.Add(new Customer(1000 + i, name, segment, region, email));
        }
        return list;
    }

    private static List<Product> Products() =>
    [
        new(1, "Northstar City 3", "City", "Northstar Bikes", 899m),
        new(2, "Northstar City 7 Belt", "City", "Northstar Bikes", 1299m),
        new(3, "Northstar Urban Flat", "Urban", "Northstar Bikes", 1099m),
        new(4, "Northstar Urban Pro", "Urban", "Northstar Bikes", 1599m),
        new(5, "Northstar Gravel AL", "Gravel", "Northstar Bikes", 1899m),
        new(6, "Northstar Gravel Carbon", "Gravel", "Northstar Bikes", 3499m),
        new(7, "Northstar Road Aero", "Road", "Northstar Bikes", 4299m),
        new(8, "Northstar Road Endurance", "Road", "Northstar Bikes", 2799m),
        new(9, "Northstar E-City 500", "E-Bike", "Northstar Bikes", 2999m),
        new(10, "Northstar E-Trekking 625", "E-Bike", "Northstar Bikes", 3799m),
        new(11, "Northstar E-Cargo Duo", "E-Bike", "Northstar Bikes", 5499m),
        new(12, "Drivetrain Kit 12s", "Components", "Northstar Parts", 649m),
        new(13, "Hydraulic Brake Set", "Components", "Northstar Parts", 289m),
        new(14, "Carbon Wheelset 45", "Components", "Northstar Parts", 1399m),
        new(15, "E-Bike Battery 625Wh", "Components", "Northstar Parts", 799m),
        new(16, "Helmet Aero", "Accessories", "Northstar Parts", 149m),
        new(17, "Pannier Set Waterproof", "Accessories", "Northstar Parts", 119m),
        new(18, "Smart Lock", "Accessories", "Northstar Parts", 179m),
        new(19, "Annual Service Plan", "Service", "Northstar Services", 249m),
        new(20, "Fleet Maintenance Contract", "Service", "Northstar Services", 1890m),
        new(21, "Extended Warranty 3y", "Service", "Northstar Services", 199m),
    ];

    // ---- Facts ------------------------------------------------------------------------------------------
    private (List<OrderLine> Lines, List<Return> Returns) Orders(IReadOnlyList<Customer> customers, IReadOnlyList<Product> products)
    {
        var lines = new List<OrderLine>();
        var returns = new List<Return>();
        var lineKey = 1; var orderId = 500000; var returnKey = 1;
        var start = new DateOnly(2025, 1, 1);
        var end = new DateOnly(2026, 12, 31);
        var asOf = new DateOnly(2026, 11, 1);

        for (var day = start; day <= end; day = day.AddDays(1))
        {
            // Seasonality (bikes peak Mar–Aug) and year-over-year growth of ~9 %.
            var season = 1.0 + 0.45 * Math.Sin((day.DayOfYear - 80) / 365.0 * 2 * Math.PI);
            var growth = day.Year == 2026 ? 1.09 : 1.0;
            var ordersToday = (int)Math.Round(4.2 * season * growth + NextDouble() * 2);

            for (var o = 0; o < ordersToday; o++)
            {
                orderId++;
                var customer = Pick(customers);
                var status = day > asOf.AddDays(-30)
                    ? PickWeighted<string>([("Completed", 0.55), ("Pending", 0.40), ("Cancelled", 0.05)])
                    : PickWeighted<string>([("Completed", 0.925), ("Pending", 0.005), ("Cancelled", 0.07)]);
                var lineCount = PickWeighted<int>([(1, 0.45), (2, 0.3), (3, 0.17), (4, 0.08)]);
                var orderGross = 0m;

                for (var l = 0; l < lineCount; l++)
                {
                    var product = PickWeighted(products.Select(p => (p, p.BusinessUnit switch { "Northstar Bikes" => 1.0, "Northstar Parts" => 0.9, _ => 0.6 })).ToList());
                    var qty = product.Category is "Service" ? NextInt(1, 4) : customer.Segment == "Dealer" ? NextInt(2, 9) : NextInt(1, 3);
                    var gross = Math.Round(product.ListPrice * qty, 2);
                    var discountRate = customer.Segment switch
                    {
                        "Dealer" => 0.12 + NextDouble() * 0.10,
                        "Business" => 0.05 + NextDouble() * 0.07,
                        _ => NextDouble() < 0.3 ? 0.05 : 0.0,
                    };
                    var discount = Math.Round(gross * (decimal)discountRate, 2);
                    orderGross += gross;
                    lines.Add(new OrderLine(lineKey++, orderId, day, customer.Key, product.Key, customer.Region, product.BusinessUnit, product.Category, customer.Segment, qty, gross, discount, status));
                }

                if (status == "Completed" && NextDouble() < 0.045)
                {
                    var retDate = day.AddDays(NextInt(5, 40));
                    if (retDate <= end)
                    {
                        var amount = Math.Round(orderGross * (decimal)(0.2 + NextDouble() * 0.6), 2);
                        var reason = Pick(["Damaged in transit", "Wrong specification", "Customer withdrawal", "Warranty replacement"]);
                        returns.Add(new Return(returnKey++, orderId, retDate, customer.Region, lines[^1].BusinessUnit, amount, reason));
                    }
                }
            }
        }
        return (lines, returns);
    }

    private List<FinanceAdjustment> Adjustments()
    {
        var list = new List<FinanceAdjustment>();
        var key = 1;

        // Corrections that were already part of the pipeline: many, small, badly described – mirrors the existing feed.
        for (var i = 0; i < 64; i++)
        {
            var date = new DateOnly(2025, 1, 1).AddDays(NextInt(0, 730));
            list.Add(new FinanceAdjustment(key++, date, Pick(SyntheticDataset.Regions), Pick(SyntheticDataset.BusinessUnits),
                -NextInt(80, 900), "Manual Finance correction", AdjustmentSource.Pipeline, "finance-bot", "Approved", ""));
        }

        // Corrections Finance kept in Excel: fewer, material, with real business reasons. Mostly approved, some pending.
        (string Reason, string Comment, int Min, int Max, bool Positive)[] kinds =
        [
            ("Contract true-up", "Annual dealer volume rebate settled after year end", 12000, 48000, false),
            ("Pricing error correction", "List price applied instead of contract price", 3000, 18000, false),
            ("Bad debt write-off", "Customer insolvency – receivable derecognised", 8000, 60000, false),
            ("Rebate accrual reversal", "Accrued rebate not claimed by dealer", 4000, 15000, true),
            ("Intercompany reclass", "Order booked in wrong business unit", 5000, 25000, true),
            ("Warranty provision", "Provision for E-Bike battery recall", 10000, 35000, false),
        ];
        var approvers = new[] { "Henrietta Mueller", "Lee Gu", "Patti Fernandez" };
        for (var i = 0; i < 42; i++)
        {
            var kind = Pick(kinds);
            var date = new DateOnly(2025, 1, 15).AddDays(NextInt(0, 700));
            var amount = (decimal)NextInt(kind.Min, kind.Max);
            if (!kind.Positive) amount = -amount;
            var status = NextDouble() < 0.86 ? "Approved" : "Pending";
            list.Add(new FinanceAdjustment(key++, date, Pick(SyntheticDataset.Regions), Pick(SyntheticDataset.BusinessUnits),
                amount, kind.Reason, AdjustmentSource.Excel, Pick(approvers), status, kind.Comment));
        }
        return list.OrderBy(a => a.Date).ThenBy(a => a.Key).ToList();
    }
}
