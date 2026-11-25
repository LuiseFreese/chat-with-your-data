using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Northstar.Application.Abstractions;
using Northstar.Application.Pipeline;
using Northstar.Application.Services;
using Northstar.Domain.Dax;
using Northstar.Domain.Querying;
using Northstar.Domain.Results;
using Northstar.Domain.Security;

namespace Northstar.Infrastructure.Fabric;

public sealed class FabricOptions
{
    public const string Section = "Fabric";
    /// <summary>Fabric workspace that hosts the warehouse and the governed semantic model.</summary>
    public string WorkspaceId { get; set; } = "";
    /// <summary>Semantic model used by Production Mode (deployed by tools/Northstar.Fabric.Tool deploy-model).</summary>
    public string GovernedSemanticModelId { get; set; } = "";
    public string GovernedSemanticModelName { get; set; } = "CWD-GovernedModel";
    public string PowerBiApiBaseUrl { get; set; } = "https://api.powerbi.com/v1.0/myorg/";
    public string[] Scopes { get; set; } = ["https://analysis.windows.net/powerbi/api/Dataset.Read.All"];
}

/// <summary>Supplies a delegated Power BI access token for the signed-in user. Never logged, never cached by callers.</summary>
public interface IPowerBiTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken ct);
}

/// <summary>
/// Production Mode provider: executes the deterministic DAX against the governed semantic model through the
/// documented Power BI Execute Queries REST API using the user's delegated identity, so semantic-model RLS applies.
/// Documented limits: 1 query/call, 1 table/query, 100k rows / 1M values / 15 MB, 120 requests per user per minute,
/// query-level errors may arrive inside an HTTP 200.
/// </summary>
public sealed class FabricExecuteQueriesProvider(
    HttpClient http,
    IPowerBiTokenProvider tokens,
    IOptions<FabricOptions> options,
    ILogger<FabricExecuteQueriesProvider> logger) : IAnalyticsDataProvider, IHealthContributor
{
    private readonly FabricOptions _o = options.Value;

    public string Name => "FabricExecuteQueriesProvider";
    public ExecutionMode Mode => ExecutionMode.Connected;
    public string DataOrigin => $"Fabric semantic model {_o.GovernedSemanticModelName} via Power BI Execute Queries API";
    public string Component => "Fabric semantic model (Execute Queries)";

    public async Task<QueryResult> ExecuteAsync(ValidatedQuery query, DaxQuery dax, UserContext user, CancellationToken ct)
    {
        var rows = await ExecuteDaxAsync(_o.GovernedSemanticModelId, dax.Text, ct);
        return FabricResultMapper.Map(rows, query, dax);
    }

    public async Task<IReadOnlyList<JsonObject>> ExecuteDaxAsync(string datasetId, string daxText, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var url = $"groups/{_o.WorkspaceId}/datasets/{datasetId}/executeQueries";
        var body = new { queries = new[] { new { query = daxText } }, serializerSettings = new { includeNulls = true } };

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new("Bearer", await tokens.GetAccessTokenAsync(ct));

        HttpResponseMessage response;
        try { response = await http.SendAsync(request, ct); }
        catch (HttpRequestException ex) { throw new DataProviderException("FabricUnreachable", "The Fabric service could not be reached.", ex.Message, ex); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw new DataProviderException("FabricTimeout", "The Fabric service did not respond in time.", ex.Message, ex); }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            logger.LogInformation("Execute Queries {Status} in {Ms} ms (dataset {Dataset})", (int)response.StatusCode, sw.ElapsedMilliseconds, datasetId);

            switch (response.StatusCode)
            {
                case HttpStatusCode.TooManyRequests:
                    var retry = response.Headers.RetryAfter?.Delta?.TotalSeconds;
                    throw new DataProviderException("FabricRateLimited", $"Fabric is rate-limiting requests (120/min per user). Retry in {retry ?? 30:0}s.", text);
                case HttpStatusCode.Unauthorized:
                    throw new DataProviderException("FabricUnauthorized", "Your sign-in is not accepted by Power BI. Please sign in again.", Redact(text));
                case HttpStatusCode.Forbidden:
                    throw new DataProviderException("FabricForbidden", "You do not have Build permission on the semantic model.", Redact(text));
                case HttpStatusCode.NotFound:
                    throw new DataProviderException("FabricNotFound", "The semantic model was not found in the workspace.", Redact(text));
            }
            if (!response.IsSuccessStatusCode)
                throw new DataProviderException("FabricHttpError", $"Fabric returned HTTP {(int)response.StatusCode}.", Redact(text));

            JsonNode? root;
            try { root = JsonNode.Parse(text); }
            catch (JsonException ex) { throw new DataProviderException("FabricInvalidResponse", "Fabric returned an unreadable response.", ex.Message, ex); }

            // Query-level errors arrive inside HTTP 200.
            var result = root?["results"]?[0];
            if (result is null) throw new DataProviderException("FabricInvalidResponse", "Fabric returned no result set.", text.Length > 500 ? text[..500] : text);
            if (result["error"] is JsonNode err)
            {
                var code = err["code"]?.GetValue<string>() ?? "DaxError";
                var message = err["message"]?.GetValue<string>() ?? err["details"]?["message"]?.GetValue<string>() ?? err.ToJsonString();
                throw new DataProviderException("FabricQueryError", "The semantic model rejected the query.", $"{code}: {message}");
            }
            var tables = result["tables"]?.AsArray();
            if (tables is null || tables.Count == 0) return [];
            return tables[0]!["rows"]!.AsArray().Select(r => r!.AsObject()).ToList();
        }
    }

    public async Task<ProviderHealth> CheckHealthAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"groups/{_o.WorkspaceId}/datasets/{_o.GovernedSemanticModelId}");
            request.Headers.Authorization = new("Bearer", await tokens.GetAccessTokenAsync(ct));
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new ProviderHealth(false, $"HTTP {(int)response.StatusCode}", sw.Elapsed, "Semantic model metadata not readable");
            var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
            var name = json?["name"]?.GetValue<string>();
            var rls = json?["isEffectiveIdentityRolesRequired"]?.GetValue<bool>() ?? false;
            return new ProviderHealth(true, "Reachable", sw.Elapsed, $"{name}; RLS roles defined: {rls}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ProviderHealth(false, "Unavailable", sw.Elapsed, ex is Microsoft.Identity.Web.MicrosoftIdentityWebChallengeUserException ? "Sign-in / consent required" : ex.GetType().Name);
        }
    }

    Task<ProviderHealth> IHealthContributor.CheckAsync(CancellationToken ct) => CheckHealthAsync(ct);

    private static string Redact(string text) => text.Length > 300 ? text[..300] : text;
}

internal static class FabricResultMapper
{
    public static QueryResult Map(IReadOnlyList<JsonObject> rows, ValidatedQuery query, DaxQuery dax)
    {
        var groupKeys = dax.GroupColumns.Select(c => c.Replace("'", "", StringComparison.Ordinal)).ToList();
        var list = new List<ResultRow>();
        var warnings = new List<string>();
        foreach (var row in rows)
        {
            var keys = groupKeys.Select(k => row[k]?.ToString() ?? "(blank)").ToList();
            list.Add(new ResultRow(keys, ToDecimal(row[dax.ValueColumn]), dax.CompareValueColumn is null ? null : ToDecimal(row[dax.CompareValueColumn])));
        }
        // ROW() with a blank measure returns null – surface as 0 so the UI shows a number.
        if (query.Dimensions.Count == 0 && list.Count == 0) list.Add(new ResultRow([], 0m, query.IsComparison ? 0m : null));
        var truncated = rows.Count >= 100_000;
        if (truncated) warnings.Add("Result reached the Execute Queries row limit and may be incomplete.");
        return new QueryResult(query.Dimensions.Select(d => d.BusinessName).ToList(), list, query.Period.Label, query.ComparePeriod?.Label, truncated, warnings);
    }

    private static decimal? ToDecimal(JsonNode? node) => node switch
    {
        null => null,
        JsonValue v when v.TryGetValue<decimal>(out var d) => d,
        JsonValue v when v.TryGetValue<double>(out var dbl) => (decimal)dbl,
        JsonValue v when v.TryGetValue<long>(out var l) => l,
        JsonValue v when v.TryGetValue<string>(out var s) && decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var p) => p,
        _ => null,
    };
}
