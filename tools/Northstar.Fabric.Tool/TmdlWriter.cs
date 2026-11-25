using Northstar.Domain.Metrics;

namespace Northstar.Fabric.Tool;

/// <summary>
/// Emits the TMDL definition of the governed semantic model (Direct Lake on the configured warehouse, schema cwd).
/// Table/column names here are the contract the <see cref="MetricCatalog"/> and the DAX builder rely on.
/// </summary>
public static class TmdlWriter
{
    public const string ModelName = "CWD-GovernedModel";

    public static Dictionary<string, string> Build(string warehouseSqlEndpoint, string warehouseId, MetricCatalog catalog, IReadOnlyList<string>? roleMembers = null)
    {
        var parts = new Dictionary<string, string>
        {
            ["definition.pbism"] = """
                {
                  "$schema": "https://developer.microsoft.com/json-schemas/fabric/item/semanticModel/definitionProperties/1.0.0/schema.json",
                  "version": "4.2",
                  "settings": {}
                }
                """,
            ["definition/database.tmdl"] = "database\n\tcompatibilityLevel: 1606\n",
            ["definition/model.tmdl"] = """
                model Model
                	culture: en-US
                	defaultPowerBIDataSourceVersion: powerBI_V3
                	sourceQueryCulture: en-US
                	directLakeBehavior: directLakeOnly
                	dataAccessOptions
                		legacyRedirects
                		returnErrorValuesAsNull

                annotation __PBI_TimeIntelligenceEnabled = 0
                annotation PBI_QueryOrder = ["DatabaseQuery"]

                ref table Date
                ref table Region
                ref table 'Business Unit'
                ref table Customer
                ref table Product
                ref table Sales
                ref table Returns
                ref table Adjustments
                ref table 'Region Access'

                ref role 'Regional Reader'
                """,
            ["definition/expressions.tmdl"] = $$"""
                expression DatabaseQuery =
                		let
                		    database = Sql.Database("{{warehouseSqlEndpoint}}", "{{warehouseId}}")
                		in
                		    database

                	annotation PBI_IncludeFutureArtifacts = False
                """,
            ["definition/tables/Date.tmdl"] = Table("Date", "DimDate", """
                	dataCategory: Time
                	description: Calendar 2025–2026. All measures are filtered through this table.
                """, [
                Col("Date", "dateTime", "Date", "sourceProviderType: date\n\t\tformatString: yyyy-mm-dd\n\t\tisKey"),
                Col("Year", "int64", "Year", "formatString: 0"),
                Col("Quarter", "int64", "Quarter", "formatString: 0"),
                Col("Quarter Label", "string", "Quarter Label", "sortByColumn: 'Year Month'\n\t\tdescription: Calendar quarter, e.g. Q3 2026"),
                Col("Month", "int64", "Month", "formatString: 0"),
                Col("Month Label", "string", "Month Label", "sortByColumn: 'Year Month'"),
                Col("Month Name", "string", "Month Name", "sortByColumn: Month"),
                Col("Year Month", "int64", "Year Month", "formatString: 0\n\t\tisHidden"),
            ]),
            ["definition/tables/Region.tmdl"] = Table("Region", "DimRegion", "\tdescription: Sales regions. Row-level security is applied on this table.", [
                Col("RegionKey", "int64", "RegionKey", "isHidden"),
                Col("Region", "string", "Region", "description: Sales region of the customer"),
            ]),
            ["definition/tables/Business Unit.tmdl"] = Table("Business Unit", "DimBusinessUnit", "", [
                Col("BusinessUnitKey", "int64", "BusinessUnitKey", "isHidden"),
                Col("Business Unit", "string", "Business Unit", "description: Legal/operational unit that booked the order"),
            ]),
            ["definition/tables/Customer.tmdl"] = Table("Customer", "DimCustomer", "\tdescription: Customers. No personal contact data is modelled – by design.", [
                Col("CustomerKey", "int64", "CustomerKey", "isHidden"),
                Col("Customer Name", "string", "Customer Name", ""),
                Col("Segment", "string", "Segment", "description: Commercial segment (Business, Dealer, Retail)"),
                Col("Region", "string", "Region", "isHidden"),
            ]),
            ["definition/tables/Product.tmdl"] = Table("Product", "DimProduct", "", [
                Col("ProductKey", "int64", "ProductKey", "isHidden"),
                Col("Product Name", "string", "Product Name", ""),
                Col("Category", "string", "Category", "description: Product category"),
                Col("Business Unit", "string", "Business Unit", "isHidden"),
                Col("List Price", "double", "List Price", "formatString: #,##0.00 €\n\t\tsummarizeBy: none"),
            ]),
            ["definition/tables/Sales.tmdl"] = Table("Sales", "FactSalesOrderLine", "\tdescription: Order lines (grain: order line). Home of all approved business measures.", [
                Col("OrderLineKey", "int64", "OrderLineKey", "isHidden"),
                Col("Order Id", "int64", "Order Id", "isHidden"),
                Col("Order Date", "dateTime", "Order Date", "sourceProviderType: date\n\t\tformatString: yyyy-mm-dd\n\t\tisHidden"),
                Col("CustomerKey", "int64", "CustomerKey", "isHidden"),
                Col("ProductKey", "int64", "ProductKey", "isHidden"),
                Col("RegionKey", "int64", "RegionKey", "isHidden"),
                Col("BusinessUnitKey", "int64", "BusinessUnitKey", "isHidden"),
                Col("Quantity", "int64", "Quantity", "formatString: 0\n\t\tsummarizeBy: sum\n\t\tisHidden"),
                Col("Gross Amount", "double", "Gross Amount", "formatString: #,##0.00 €\n\t\tsummarizeBy: sum\n\t\tisHidden"),
                Col("Discount Amount", "double", "Discount Amount", "formatString: #,##0.00 €\n\t\tsummarizeBy: sum\n\t\tisHidden"),
                Col("Order Status", "string", "Order Status", "description: Completed, Pending or Cancelled. Cancelled orders are excluded from every revenue measure."),
            ], Measures(catalog)),
            ["definition/tables/Returns.tmdl"] = Table("Returns", "FactReturn", "\tdescription: Credit notes for returned goods (grain: order).", [
                Col("ReturnKey", "int64", "ReturnKey", "isHidden"),
                Col("Order Id", "int64", "Order Id", "isHidden"),
                Col("Return Date", "dateTime", "Return Date", "sourceProviderType: date\n\t\tformatString: yyyy-mm-dd\n\t\tisHidden"),
                Col("RegionKey", "int64", "RegionKey", "isHidden"),
                Col("BusinessUnitKey", "int64", "BusinessUnitKey", "isHidden"),
                Col("Amount", "double", "Amount", "formatString: #,##0.00 €\n\t\tsummarizeBy: sum\n\t\tisHidden"),
                Col("Reason", "string", "Reason", ""),
            ]),
            ["definition/tables/Adjustments.tmdl"] = Table("Adjustments", "FactFinanceAdjustment", "\tdescription: Manual Finance corrections. Source = Pipeline (legacy feed) or Excel (reconciled workbook). Only Status = Approved counts.", [
                Col("AdjustmentKey", "int64", "AdjustmentKey", "isHidden"),
                Col("Adjustment Date", "dateTime", "Adjustment Date", "sourceProviderType: date\n\t\tformatString: yyyy-mm-dd\n\t\tisHidden"),
                Col("RegionKey", "int64", "RegionKey", "isHidden"),
                Col("BusinessUnitKey", "int64", "BusinessUnitKey", "isHidden"),
                Col("Amount", "double", "Amount", "formatString: #,##0.00 €\n\t\tsummarizeBy: sum\n\t\tisHidden"),
                Col("Reason", "string", "Reason", ""),
                Col("Source", "string", "Source", "description: Pipeline or Excel"),
                Col("Approved By", "string", "Approved By", ""),
                Col("Status", "string", "Status", "description: Approved or Pending"),
                Col("Comment", "string", "Comment", ""),
                Col("Loaded At", "dateTime", "Loaded At", "sourceProviderType: datetime2\n\t\tisHidden"),
            ]),
            ["definition/tables/Region Access.tmdl"] = Table("Region Access", "RegionAccess", "\tisHidden\n\tdescription: Security mapping user → region used by the Regional Reader role.", [
                Col("User Principal Name", "string", "User Principal Name", ""),
                Col("Region", "string", "Region", ""),
            ]),
            ["definition/relationships.tmdl"] = """
                relationship SalesDate
                	fromColumn: Sales.'Order Date'
                	toColumn: Date.Date

                relationship ReturnsDate
                	fromColumn: Returns.'Return Date'
                	toColumn: Date.Date

                relationship AdjustmentsDate
                	fromColumn: Adjustments.'Adjustment Date'
                	toColumn: Date.Date

                relationship SalesCustomer
                	fromColumn: Sales.CustomerKey
                	toColumn: Customer.CustomerKey

                relationship SalesProduct
                	fromColumn: Sales.ProductKey
                	toColumn: Product.ProductKey

                relationship SalesRegion
                	fromColumn: Sales.RegionKey
                	toColumn: Region.RegionKey

                relationship ReturnsRegion
                	fromColumn: Returns.RegionKey
                	toColumn: Region.RegionKey

                relationship AdjustmentsRegion
                	fromColumn: Adjustments.RegionKey
                	toColumn: Region.RegionKey

                relationship SalesBu
                	fromColumn: Sales.BusinessUnitKey
                	toColumn: 'Business Unit'.BusinessUnitKey

                relationship ReturnsBu
                	fromColumn: Returns.BusinessUnitKey
                	toColumn: 'Business Unit'.BusinessUnitKey

                relationship AdjustmentsBu
                	fromColumn: Adjustments.BusinessUnitKey
                	toColumn: 'Business Unit'.BusinessUnitKey
                """,
            ["definition/roles/Regional Reader.tmdl"] = """
                /// Restricts every measure to the regions mapped to the signed-in user in 'Region Access'. Workspace Admins/Members/Contributors are not subject to RLS.
                /// Role members are tenant-specific and are NOT part of this definition: assign them in the Fabric portal
                /// (model → Security) or with 'Northstar.Fabric.Tool rls-members' after configuring NORTHSTAR_RLS_USERS.
                role 'Regional Reader'
                	modelPermission: read

                	tablePermission Region = 'Region'[Region] IN SELECTCOLUMNS(FILTER(ALL('Region Access'), 'Region Access'[User Principal Name] = USERPRINCIPALNAME()), "Region", 'Region Access'[Region])

                	tablePermission 'Region Access' = 'Region Access'[User Principal Name] = USERPRINCIPALNAME()
                """ + MemberLines(roleMembers),
        };
        if (parts["definition/model.tmdl"].Contains("NOROLE")) { parts["definition/model.tmdl"] = parts["definition/model.tmdl"].Replace("NOROLE", ""); parts.Remove("definition/roles/Regional Reader.tmdl"); }
        return parts;
    }

    private static string MemberLines(IReadOnlyList<string>? upns) =>
        upns is not { Count: > 0 }
            ? ""
            : string.Concat(upns.Select(u => $"\n\n\tmember '{u}'\n\t\tidentityProvider: AzureAD"));

    private static string Table(string name, string entity, string header, IEnumerable<string> columns, string? measures = null)
    {
        var n = name.Contains(' ') ? $"'{name}'" : name;
        var sb = new System.Text.StringBuilder();
        var headerLines = header.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        foreach (var d in headerLines.Where(l => l.StartsWith("description:"))) sb.AppendLine("/// " + d["description:".Length..].Trim());
        sb.AppendLine($"table {n}");
        foreach (var l in headerLines.Where(l => !l.StartsWith("description:"))) sb.AppendLine("\t" + l);
        sb.AppendLine($"\tsourceLineageTag: [cwd].[{entity}]");
        sb.AppendLine();
        if (measures is not null) sb.AppendLine(measures);
        foreach (var c in columns) sb.AppendLine(c);
        sb.AppendLine($"\tpartition {n} = entity");
        sb.AppendLine("\t\tmode: directLake");
        sb.AppendLine("\t\tsource");
        sb.AppendLine($"\t\t\tentityName: {entity}");
        sb.AppendLine("\t\t\tschemaName: cwd");
        sb.AppendLine("\t\t\texpressionSource: DatabaseQuery");
        sb.AppendLine();
        return sb.ToString();
    }

    private static string Col(string name, string dataType, string sourceColumn, string extra)
    {
        var n = name.Contains(' ') ? $"'{name}'" : name;
        var sb = new System.Text.StringBuilder();
        var lines = extra.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        foreach (var d in lines.Where(l => l.StartsWith("description:"))) sb.AppendLine("\t/// " + d["description:".Length..].Trim());
        sb.AppendLine($"\tcolumn {n}");
        sb.AppendLine($"\t\tdataType: {dataType}");
        if (!extra.Contains("summarizeBy")) sb.AppendLine("\t\tsummarizeBy: none");
        sb.AppendLine($"\t\tsourceColumn: {sourceColumn}");
        sb.AppendLine($"\t\tsourceLineageTag: {sourceColumn}");
        foreach (var line in lines.Where(l => !l.StartsWith("description:"))) sb.AppendLine("\t\t" + line);
        return sb.ToString();
    }

    private static string Measures(MetricCatalog catalog)
    {
        (string Name, string Dax, string Format)[] defs =
        [
            ("Gross Revenue", "CALCULATE(SUM('Sales'[Gross Amount]), 'Sales'[Order Status] <> \"Cancelled\")", "#,##0 €"),
            ("Total Discounts", "CALCULATE(SUM('Sales'[Discount Amount]), 'Sales'[Order Status] <> \"Cancelled\")", "#,##0 €"),
            ("Total Returns", "SUM('Returns'[Amount])", "#,##0 €"),
            ("Net Revenue", "[Gross Revenue] - [Total Discounts] - [Total Returns]", "#,##0 €"),
            ("Finance Adjustments", "CALCULATE(SUM('Adjustments'[Amount]), 'Adjustments'[Status] = \"Approved\")", "#,##0 €"),
            ("Finance Adjustments Pipeline Only", "CALCULATE(SUM('Adjustments'[Amount]), 'Adjustments'[Status] = \"Approved\", 'Adjustments'[Source] = \"Pipeline\")", "#,##0 €"),
            ("Adjusted Net Revenue", "[Net Revenue] + [Finance Adjustments]", "#,##0 €"),
            ("Order Count", "CALCULATE(DISTINCTCOUNT('Sales'[Order Id]), 'Sales'[Order Status] <> \"Cancelled\")", "#,##0"),
            ("Average Order Value", "DIVIDE([Net Revenue], [Order Count])", "#,##0.00 €"),
        ];
        var sb = new System.Text.StringBuilder();
        foreach (var (name, dax, format) in defs)
        {
            var metric = catalog.Metrics.FirstOrDefault(m => m.SemanticMeasure == name);
            if (metric is not null)
                sb.AppendLine($"\t/// {metric.Description.Replace("\n", " ")} Owner: {metric.BusinessOwner}. Definition: {metric.Formula}");
            sb.AppendLine($"\tmeasure '{name}' = {dax}");
            sb.AppendLine($"\t\tformatString: {format}");
            sb.AppendLine("\t\tdisplayFolder: Approved Business Measures");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}








