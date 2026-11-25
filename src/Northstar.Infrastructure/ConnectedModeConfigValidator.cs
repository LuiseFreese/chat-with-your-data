using Microsoft.Extensions.Configuration;

namespace Northstar.Infrastructure;

/// <summary>
/// Validates that Connected Mode has a complete, tenant-independent configuration before the host starts.
/// Local Demo Mode never requires any of these settings.
/// </summary>
public static class ConnectedModeConfigValidator
{
    /// <summary>Returns one actionable message per missing/invalid setting. Empty = configuration is complete.</summary>
    public static IReadOnlyList<string> Validate(IConfiguration configuration)
    {
        var errors = new List<string>();

        void RequireGuid(string key, string hint)
        {
            var value = configuration[key];
            if (string.IsNullOrWhiteSpace(value))
                errors.Add($"'{key}' is missing. {hint}");
            else if (!Guid.TryParse(value, out _))
                errors.Add($"'{key}' is not a valid GUID. {hint}");
        }

        void Require(string key, string hint)
        {
            if (string.IsNullOrWhiteSpace(configuration[key]))
                errors.Add($"'{key}' is missing. {hint}");
        }

        RequireGuid("Fabric:WorkspaceId", "Set it to your Fabric workspace id (see docs/FABRIC_SETUP.md).");
        RequireGuid("Fabric:GovernedSemanticModelId", "Deploy the model with 'dotnet run --project tools/Northstar.Fabric.Tool -- deploy-model' and use the id it prints (see docs/FABRIC_SETUP.md).");
        RequireGuid("AzureAd:TenantId", "Set it to your Entra tenant id (see docs/ENTRA_SETUP.md).");
        RequireGuid("AzureAd:ClientId", "Set it to your app registration's client id (see docs/ENTRA_SETUP.md).");
        Require("AzureAd:ClientSecret", "Store it with 'dotnet user-secrets set \"AzureAd:ClientSecret\" \"<secret>\" --project src/Northstar.Web' (see docs/ENTRA_SETUP.md).");

        var parser = configuration["Demo:IntentParser"];
        if (!string.Equals(parser, "Deterministic", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = configuration["AzureOpenAI:Endpoint"];
            if (string.IsNullOrWhiteSpace(endpoint))
                errors.Add("'AzureOpenAI:Endpoint' is missing. Set it to your Azure OpenAI endpoint (see docs/AZURE_OPENAI_SETUP.md) or start with Demo:IntentParser=Deterministic.");
            else if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                errors.Add("'AzureOpenAI:Endpoint' must be an absolute https URL (see docs/AZURE_OPENAI_SETUP.md).");
            Require("AzureOpenAI:Deployment", "Set it to the name of your chat model deployment (see docs/AZURE_OPENAI_SETUP.md).");
        }

        return errors;
    }
}
