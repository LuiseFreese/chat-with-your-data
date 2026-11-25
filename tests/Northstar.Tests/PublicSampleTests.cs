using Microsoft.Extensions.Configuration;
using Northstar.Domain.Data;
using Northstar.Domain.Metrics;
using Northstar.Fabric.Tool;
using Northstar.Infrastructure;

namespace Northstar.Tests;

/// <summary>Guards that make the repository safe to publish: tenant-independent configuration, deterministic data, placeholder-only generated artifacts.</summary>
public class PublicSampleTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Fact]
    public void Connected_mode_with_empty_configuration_names_every_required_setting()
    {
        var errors = ConnectedModeConfigValidator.Validate(Config());

        foreach (var key in new[] { "Fabric:WorkspaceId", "Fabric:GovernedSemanticModelId", "AzureAd:TenantId", "AzureAd:ClientId", "AzureAd:ClientSecret", "AzureOpenAI:Endpoint" })
            Assert.Contains(errors, e => e.Contains($"'{key}'"));
    }

    [Fact]
    public void Connected_mode_rejects_non_guid_ids()
    {
        var errors = ConnectedModeConfigValidator.Validate(Config(("Fabric:WorkspaceId", "<your-fabric-workspace-id>")));
        Assert.Contains(errors, e => e.Contains("'Fabric:WorkspaceId'") && e.Contains("not a valid GUID"));
    }

    [Fact]
    public void Connected_mode_with_complete_configuration_passes()
    {
        var errors = ConnectedModeConfigValidator.Validate(Config(
            ("Fabric:WorkspaceId", Guid.NewGuid().ToString()),
            ("Fabric:GovernedSemanticModelId", Guid.NewGuid().ToString()),
            ("AzureAd:TenantId", Guid.NewGuid().ToString()),
            ("AzureAd:ClientId", Guid.NewGuid().ToString()),
            ("AzureAd:ClientSecret", "from-user-secrets"),
            ("AzureOpenAI:Endpoint", "https://example-openai.cognitiveservices.azure.com/"),
            ("AzureOpenAI:Deployment", "gpt-4.1-mini")));

        Assert.Empty(errors);
    }

    [Fact]
    public void Deterministic_parser_mode_does_not_require_azure_openai()
    {
        var errors = ConnectedModeConfigValidator.Validate(Config(
            ("Demo:IntentParser", "Deterministic"),
            ("Fabric:WorkspaceId", Guid.NewGuid().ToString()),
            ("Fabric:GovernedSemanticModelId", Guid.NewGuid().ToString()),
            ("AzureAd:TenantId", Guid.NewGuid().ToString()),
            ("AzureAd:ClientId", Guid.NewGuid().ToString()),
            ("AzureAd:ClientSecret", "from-user-secrets")));

        Assert.Empty(errors);
    }

    [Fact]
    public void Same_seed_produces_identical_dataset()
    {
        var a = SyntheticDataGenerator.Generate(20260325);
        var b = SyntheticDataGenerator.Generate(20260325);

        Assert.Equal(a.OrderLines.Count, b.OrderLines.Count);
        Assert.Equal(a.OrderLines.Sum(l => l.GrossAmount), b.OrderLines.Sum(l => l.GrossAmount));
        Assert.Equal(a.Returns.Sum(r => r.Amount), b.Returns.Sum(r => r.Amount));
        Assert.Equal(a.Adjustments.Sum(x => x.Amount), b.Adjustments.Sum(x => x.Amount));
        Assert.Equal(a.Customers.Select(c => c.Name), b.Customers.Select(c => c.Name));
    }

    [Fact]
    public void Synthetic_dataset_contains_no_real_identities()
    {
        var data = SyntheticDataGenerator.Generate();
        foreach (var email in data.Customers.Select(c => c.ContactEmail))
            Assert.EndsWith(".example", email);
    }

    [Fact]
    public void Generated_tmdl_contains_only_placeholders_and_no_role_members()
    {
        var parts = TmdlWriter.Build("YOUR-WAREHOUSE-SQL-ENDPOINT.datawarehouse.fabric.microsoft.com", "YOUR-WAREHOUSE-ID", MetricCatalog.CreateDefault());

        var all = string.Join("\n", parts.Values);
        Assert.DoesNotContain("member '", all);                         // no tenant-specific role members
        Assert.DoesNotContain("onmicrosoft" + ".com", all);
        Assert.DoesNotMatch(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", all); // no real GUIDs
        Assert.Contains("YOUR-WAREHOUSE-SQL-ENDPOINT.datawarehouse.fabric.microsoft.com", parts["definition/expressions.tmdl"]);
    }

    [Fact]
    public void Generated_tmdl_emits_configured_role_members()
    {
        var parts = TmdlWriter.Build("HOST.datawarehouse.fabric.microsoft.com", "id", MetricCatalog.CreateDefault(),
            ["viewer.user@yourtenant.example"]);

        Assert.Contains("member 'viewer.user@yourtenant.example'", parts["definition/roles/Regional Reader.tmdl"]);
    }
}
