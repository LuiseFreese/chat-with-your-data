using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Northstar.Infrastructure.Local;

namespace Northstar.Web.Services;

/// <summary>
/// Local Demo Mode only: authenticates every request as the currently selected simulated persona so the UI and the
/// API behave identically to Connected Mode. Clearly labelled; never used when Demo:Mode = Connected.
/// </summary>
public sealed class LocalDemoAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, SimulatedUserContextAccessor personas)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public new const string Scheme = "LocalDemo";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var p = personas.Current;
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.Name, p.DisplayName),
            new Claim("preferred_username", p.UserPrincipalName),
            new Claim("simulated", "true"),
        ], Scheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}

/// <summary>Per-circuit chat state so a clarification can resume the original question.</summary>
public sealed class ConversationState
{
    public Northstar.Application.Abstractions.ConversationContext Context { get; set; } = Northstar.Application.Abstractions.ConversationContext.Empty;
    public List<ChatEntry> Entries { get; } = [];
    public bool PrototypeMode { get; set; }
    public void Reset() { Context = Northstar.Application.Abstractions.ConversationContext.Empty; Entries.Clear(); }
}

public sealed record ChatEntry(
    DateTimeOffset At,
    string Question,
    bool Prototype,
    Northstar.Application.Pipeline.QueryResponse? Production,
    Northstar.Application.Services.PrototypeResponse? PrototypeResult,
    string? DefinitionUsed = null,
    string? Error = null);

/// <summary>Presentation toggles shared by all circuits on the demo machine (enlarged inspector etc.).</summary>
public sealed class PresentationState
{
    public bool InspectorEnlarged { get; set; }
    public event Action? Changed;
    public event Action? PersonaChanged;
    public void Toggle() { InspectorEnlarged = !InspectorEnlarged; Changed?.Invoke(); }
    public void NotifyPersonaChanged() => PersonaChanged?.Invoke();
}

