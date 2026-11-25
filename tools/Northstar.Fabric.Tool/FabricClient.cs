using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;

namespace Northstar.Fabric.Tool;

/// <summary>Thin client for the Fabric REST API (items, definitions, role assignments) and Power BI REST API (dataset users, Execute Queries).</summary>
public sealed class FabricClient(TokenCredential credential, string workspaceId)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    public string WorkspaceId => workspaceId;

    private async Task<HttpRequestMessage> Request(HttpMethod method, string url, string scope, object? body = null)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext([scope]), default);
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private const string FabricScope = "https://api.fabric.microsoft.com/.default";
    private const string PbiScope = "https://analysis.windows.net/powerbi/api/.default";
    private const string GraphScope = "https://graph.microsoft.com/.default";

    public async Task<JsonNode?> FabricAsync(HttpMethod method, string path, object? body = null, bool allowNotFound = false)
    {
        using var req = await Request(method, $"https://api.fabric.microsoft.com/v1/{path}", FabricScope, body);
        using var res = await Http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        if (res.StatusCode == System.Net.HttpStatusCode.Accepted)
        {
            // Long-running operation
            var location = res.Headers.Location!.ToString();
            var retry = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
            while (true)
            {
                await Task.Delay(retry);
                using var poll = await Request(HttpMethod.Get, location, FabricScope);
                using var pr = await Http.SendAsync(poll);
                var pj = JsonNode.Parse(await pr.Content.ReadAsStringAsync());
                var status = pj?["status"]?.GetValue<string>();
                Console.WriteLine($"   LRO status: {status}");
                if (status == "Succeeded")
                {
                    using var result = await Request(HttpMethod.Get, location.TrimEnd('/') + "/result", FabricScope);
                    using var rr = await Http.SendAsync(result);
                    var rt = await rr.Content.ReadAsStringAsync();
                    return string.IsNullOrWhiteSpace(rt) ? null : JsonNode.Parse(rt);
                }
                if (status == "Failed") throw new InvalidOperationException("LRO failed: " + pj?.ToJsonString());
            }
        }
        if (allowNotFound && res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Fabric {method} {path} → {(int)res.StatusCode}: {text}");
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    public async Task<JsonNode?> PowerBiAsync(HttpMethod method, string path, object? body = null)
    {
        using var req = await Request(method, $"https://api.powerbi.com/v1.0/myorg/{path}", PbiScope, body);
        using var res = await Http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"PowerBI {method} {path} → {(int)res.StatusCode}: {text}");
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    public async Task<JsonNode?> GraphAsync(string path)
    {
        using var req = await Request(HttpMethod.Get, $"https://graph.microsoft.com/v1.0/{path}", GraphScope);
        using var res = await Http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Graph GET {path} → {(int)res.StatusCode}: {text}");
        return JsonNode.Parse(text);
    }

    public async Task<(string Id, string Type)?> FindItemAsync(string displayName, string type)
    {
        var items = await FabricAsync(HttpMethod.Get, $"workspaces/{workspaceId}/items?type={type}");
        var hit = items?["value"]?.AsArray().FirstOrDefault(i => i?["displayName"]?.GetValue<string>() == displayName);
        return hit is null ? null : (hit["id"]!.GetValue<string>(), hit["type"]!.GetValue<string>());
    }

    public static object DefinitionParts(Dictionary<string, string> parts) => new
    {
        parts = parts.Select(p => new { path = p.Key, payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(p.Value)), payloadType = "InlineBase64" }).ToArray(),
    };

    public async Task<JsonNode?> ExecuteDaxAsync(string datasetId, string dax, string? impersonate = null)
    {
        object body = impersonate is null
            ? new { queries = new[] { new { query = dax } }, serializerSettings = new { includeNulls = true } }
            : new { queries = new[] { new { query = dax } }, serializerSettings = new { includeNulls = true }, impersonatedUserName = impersonate };
        return await PowerBiAsync(HttpMethod.Post, $"groups/{workspaceId}/datasets/{datasetId}/executeQueries", body);
    }
}
