using System.Globalization;
using ClosedXML.Excel;
using Northstar.Domain.Data;

namespace Northstar.Fabric.Tool;

/// <summary>The "Finance keeps corrections in Excel" workbook: written from the synthetic dataset and read back by the ingestion step.</summary>
public static class ExcelAdjustments
{
    public const string Sheet = "Adjustments";
    private static readonly string[] Headers = ["AdjustmentId", "AdjustmentDate", "Region", "BusinessUnit", "Amount", "Reason", "ApprovedBy", "Status", "Comment"];

    public static void Write(SyntheticDataset data, string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(Sheet);
        for (var i = 0; i < Headers.Length; i++) ws.Cell(1, i + 1).Value = Headers[i];
        ws.Row(1).Style.Font.Bold = true;
        ws.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1F2A5A");
        ws.Row(1).Style.Font.FontColor = XLColor.White;

        var row = 2;
        foreach (var a in data.Adjustments.Where(a => a.Source == AdjustmentSource.Excel))
        {
            ws.Cell(row, 1).Value = a.Key;
            ws.Cell(row, 2).Value = a.Date.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, 2).Style.DateFormat.Format = "yyyy-mm-dd";
            ws.Cell(row, 3).Value = a.Region;
            ws.Cell(row, 4).Value = a.BusinessUnit;
            ws.Cell(row, 5).Value = a.Amount;
            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00 €";
            ws.Cell(row, 6).Value = a.Reason;
            ws.Cell(row, 7).Value = a.ApprovedBy;
            ws.Cell(row, 8).Value = a.Status;
            ws.Cell(row, 9).Value = a.Comment;
            row++;
        }
        ws.Columns().AdjustToContents();
        var readme = wb.Worksheets.Add("README");
        readme.Cell(1, 1).Value = "Northstar Distribution – Finance manual corrections (synthetic demo data)";
        readme.Cell(2, 1).Value = "Maintained by Finance outside the reporting pipeline. Only rows with Status = Approved count towards Adjusted Net Revenue.";
        readme.Cell(3, 1).Value = $"Generated deterministically from seed {data.Seed}.";
        wb.SaveAs(path);
    }

    public static List<FinanceAdjustment> Read(string path)
    {
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet(Sheet);
        var list = new List<FinanceAdjustment>();
        foreach (var r in ws.RowsUsed().Skip(1))
        {
            var date = r.Cell(2).DataType == XLDataType.DateTime ? DateOnly.FromDateTime(r.Cell(2).GetDateTime()) : DateOnly.Parse(r.Cell(2).GetString(), CultureInfo.InvariantCulture);
            list.Add(new FinanceAdjustment(
                r.Cell(1).GetValue<int>(), date, r.Cell(3).GetString(), r.Cell(4).GetString(), r.Cell(5).GetValue<decimal>(),
                r.Cell(6).GetString(), AdjustmentSource.Excel, r.Cell(7).GetString(), r.Cell(8).GetString(), r.Cell(9).GetString()));
        }
        return list;
    }
}
