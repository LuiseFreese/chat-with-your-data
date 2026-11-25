using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Northstar.Application.Abstractions;
using Northstar.Infrastructure;
using Northstar.Infrastructure.Fabric;
using Northstar.Web.Api;
using Northstar.Web.Components;
using Northstar.Web.Services;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o => { o.IncludeScopes = true; o.TimestampFormat = "HH:mm:ss "; });

var demo = builder.Configuration.GetSection(DemoOptions.Section).Get<DemoOptions>() ?? new DemoOptions();
var connected = demo.ExecutionMode == ExecutionMode.Connected;

builder.Services.AddNorthstarCore(builder.Configuration);
builder.Services.AddNorthstarExecutionMode(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ConversationState>();
builder.Services.AddSingleton<PresentationState>();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Northstar.ChatWithData"))
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());

if (connected)
{
    var configErrors = ConnectedModeConfigValidator.Validate(builder.Configuration);
    if (configErrors.Count > 0)
        throw new InvalidOperationException(
            "Connected Mode is not configured. Fix the following settings (appsettings.json, user-secrets or environment variables – see .env.example):\n - "
            + string.Join("\n - ", configErrors));

    // Delegated sign-in; the Power BI token is acquired on behalf of the signed-in user so Fabric RLS applies.
    var fabric = builder.Configuration.GetSection(FabricOptions.Section).Get<FabricOptions>() ?? new FabricOptions();
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
        .EnableTokenAcquisitionToCallDownstreamApi(fabric.Scopes)
        .AddInMemoryTokenCaches();
    builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();
    builder.Services.AddAuthorizationBuilder()
        .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
}
else
{
    builder.Services.AddAuthentication(LocalDemoAuthenticationHandler.Scheme)
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, LocalDemoAuthenticationHandler>(LocalDemoAuthenticationHandler.Scheme, null);
    builder.Services.AddAuthorization();
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapOpenApi();
app.MapAnalyticsApi();
if (connected) app.MapControllers();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Logger.LogInformation("Northstar Chat with Data starting in {Mode} mode (parser: {Parser})", demo.ExecutionMode, connected ? demo.IntentParser : "Deterministic");
app.Run();

public partial class Program;
