using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Northstar.Application.Abstractions;
using Northstar.Application.Pipeline;
using Northstar.Application.Services;
using Northstar.Domain.Dax;
using Northstar.Domain.Data;
using Northstar.Domain.Metrics;
using Northstar.Domain.Querying;
using Northstar.Infrastructure.Fabric;
using Northstar.Infrastructure.Local;
using Northstar.Infrastructure.OpenAi;

namespace Northstar.Infrastructure;

public sealed class DemoOptions
{
    public const string Section = "Demo";
    /// <summary>"LocalDemo" or "Connected".</summary>
    public string Mode { get; set; } = "LocalDemo";
    /// <summary>In Connected mode: "AzureOpenAI" or "Deterministic". Local mode always uses Deterministic.</summary>
    public string IntentParser { get; set; } = "AzureOpenAI";
    public int DataSeed { get; set; } = SyntheticDataGenerator.DefaultSeed;
    public ExecutionMode ExecutionMode => Mode.Equals("Connected", StringComparison.OrdinalIgnoreCase) ? ExecutionMode.Connected : ExecutionMode.LocalDemo;
}

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the domain core that is identical in both modes: catalog, validator, DAX builder, dataset, pipeline.</summary>
    public static IServiceCollection AddNorthstarCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DemoOptions>(configuration.GetSection(DemoOptions.Section));
        services.Configure<FabricOptions>(configuration.GetSection(FabricOptions.Section));
        services.Configure<AzureOpenAiOptions>(configuration.GetSection(AzureOpenAiOptions.Section));

        var demo = configuration.GetSection(DemoOptions.Section).Get<DemoOptions>() ?? new DemoOptions();

        services.AddSingleton(MetricCatalog.CreateDefault());
        services.AddSingleton<IntentValidator>();
        services.AddSingleton<DaxQueryBuilder>();
        services.AddSingleton(SyntheticDataGenerator.Generate(demo.DataSeed));
        services.AddSingleton<LocalMetricEvaluator>();
        services.AddSingleton<IQueryAuditStore, InMemoryQueryAuditStore>();
        services.AddScoped<QueryPipeline>();
        services.AddScoped<PrototypeQueryService>();
        services.AddScoped<ReconciliationService>();
        services.AddScoped<SystemHealthService>();
        services.AddSingleton<DeterministicDemoQueryIntentParser>();
        services.AddSingleton<LocalDemoDataProvider>();
        services.AddSingleton<SimulatedUserContextAccessor>();
        return services;
    }

    /// <summary>Wires the providers for the configured execution mode. Nothing else in the app knows which mode is active.</summary>
    public static IServiceCollection AddNorthstarExecutionMode(this IServiceCollection services, IConfiguration configuration)
    {
        var demo = configuration.GetSection(DemoOptions.Section).Get<DemoOptions>() ?? new DemoOptions();

        if (demo.ExecutionMode == ExecutionMode.LocalDemo)
        {
            services.AddSingleton<IAnalyticsDataProvider>(sp => sp.GetRequiredService<LocalDemoDataProvider>());
            services.AddSingleton<IHealthContributor>(sp => sp.GetRequiredService<LocalDemoDataProvider>());
            services.AddSingleton<IQueryIntentParser>(sp => sp.GetRequiredService<DeterministicDemoQueryIntentParser>());
            services.AddSingleton<IUserContextAccessor>(sp => sp.GetRequiredService<SimulatedUserContextAccessor>());
            services.AddSingleton<IPrototypeDataSource, LocalPrototypeDataSource>();
            return services;
        }

        services.TryAddSingleton<TokenCredential>(_ => new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeInteractiveBrowserCredential = true }));
        services.AddHttpClient<FabricExecuteQueriesProvider>((sp, http) =>
        {
            var o = configuration.GetSection(FabricOptions.Section).Get<FabricOptions>() ?? new FabricOptions();
            http.BaseAddress = new Uri(o.PowerBiApiBaseUrl);
            http.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddScoped<IAnalyticsDataProvider>(sp => sp.GetRequiredService<FabricExecuteQueriesProvider>());
        services.AddScoped<IHealthContributor>(sp => sp.GetRequiredService<FabricExecuteQueriesProvider>());
        // Prototype Mode always runs on the reproducible synthetic dataset – it is an intentionally simplified
        // educational example and must not depend on any pre-existing semantic model in a specific tenant.
        services.AddSingleton<IPrototypeDataSource, LocalPrototypeDataSource>();
        services.TryAddScoped<CurrentPrincipalAccessor>();
        services.TryAddScoped<IPowerBiTokenProvider, DelegatedPowerBiTokenProvider>();
        services.TryAddScoped<IUserContextAccessor, EntraUserContextAccessor>();

        if (demo.IntentParser.Equals("Deterministic", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IQueryIntentParser>(sp => sp.GetRequiredService<DeterministicDemoQueryIntentParser>());
        }
        else
        {
            services.AddSingleton<AzureOpenAiQueryIntentParser>();
            services.AddSingleton<IQueryIntentParser>(sp => sp.GetRequiredService<AzureOpenAiQueryIntentParser>());
            services.AddSingleton<IHealthContributor>(sp => sp.GetRequiredService<AzureOpenAiQueryIntentParser>());
        }
        return services;
    }
}

