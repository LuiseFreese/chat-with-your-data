using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Northstar.Web.Services;

/// <summary>Consistent, culture-independent number formatting for the UI (big-screen friendly).</summary>
public static class Format
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Currency(decimal? value) => value is null ? "–" : value.Value.ToString("N0", Inv) + " €";
    public static string Number(decimal? value) => value is null ? "–" : value.Value.ToString("N0", Inv);
    public static string Value(decimal? value, bool currency) => currency ? Currency(value) : Number(value);

    public static string Signed(decimal value, bool currency = true) =>
        (value < 0 ? "− " : "+ ") + Value(Math.Abs(value), currency);

    public static string DeltaPercent(decimal? current, decimal? compare)
    {
        if (current is null || compare is null || compare == 0) return "–";
        var pct = (current.Value - compare.Value) / Math.Abs(compare.Value) * 100m;
        return (pct < 0 ? "−" : "+") + Math.Abs(pct).ToString("0.0", Inv) + " %";
    }

    public static string Ms(TimeSpan t) => t.TotalMilliseconds.ToString("0", Inv) + " ms";
    public static string Ms(double ms) => ms.ToString("0", Inv) + " ms";
    public static string Time(DateTimeOffset t) => t.ToLocalTime().ToString("HH:mm:ss", Inv);
    public static string DateTime(DateTimeOffset t) => t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", Inv);
    public static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);
    public static string Pct(double fraction) => (fraction * 100).ToString("0.#", Inv);
    public static string Svg(double v) => v.ToString("0.##", Inv);

    public static string Json(object? value) => value is null ? "null" : JsonSerializer.Serialize(value, value.GetType(), JsonOptions);

    public static string Initials(string displayName)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "?" : string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }
}

/// <summary>One bar / point of the inline SVG chart.</summary>
public sealed record ChartRow(string Label, decimal? Value, decimal? CompareValue = null);
