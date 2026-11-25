using System.Security.Claims;
using Azure.Core;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Northstar.Application.Abstractions;
using Northstar.Application.Pipeline;
using Northstar.Application.Services;
using Northstar.Domain.Querying;
using Northstar.Domain.Results;
using Northstar.Domain.Security;

namespace Northstar.Infrastructure.Fabric;

/// <summary>
/// Resolves the signed-in principal both for HTTP requests (API) and inside Blazor Server circuits, where
/// HttpContext is not reliable and the circuit's <see cref="AuthenticationStateProvider"/> is authoritative.
/// </summary>
public sealed class CurrentPrincipalAccessor(IHttpContextAccessor httpContextAccessor, IServiceProvider services)
{
    public ClaimsPrincipal? Get()
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true) return user;
        try
        {
            var provider = services.GetService<AuthenticationStateProvider>();
            if (provider is null) return user;
            var task = provider.GetAuthenticationStateAsync();
            var state = task.IsCompletedSuccessfully ? task.Result : task.GetAwaiter().GetResult();
            return state.User.Identity?.IsAuthenticated == true ? state.User : user;
        }
        catch (InvalidOperationException) { return user; }
    }
}

/// <summary>Delegated token for the signed-in user (authorization code + refresh via Microsoft.Identity.Web).</summary>
public sealed class DelegatedPowerBiTokenProvider(ITokenAcquisition tokenAcquisition, IOptions<FabricOptions> options, CurrentPrincipalAccessor principal) : IPowerBiTokenProvider
{
    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        try
        {
            return await tokenAcquisition.GetAccessTokenForUserAsync(options.Value.Scopes, user: principal.Get());
        }
        catch (MicrosoftIdentityWebChallengeUserException ex)
        {
            throw new DataProviderException("SignInRequired", "Your sign-in needs to be refreshed before Fabric can be queried. Please sign out and sign in again.", ex.Message, ex);
        }
    }
}

/// <summary>Token from an Azure credential (CLI / developer sign-in). Used by the provisioning tool and smoke tests only.</summary>
public sealed class CredentialPowerBiTokenProvider(TokenCredential credential) : IPowerBiTokenProvider
{
    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext(["https://analysis.windows.net/powerbi/api/.default"]), ct);
        return token.Token;
    }
}

/// <summary>Resolves the signed-in Entra user. Region visibility is enforced by Fabric RLS, not by this class.</summary>
public sealed class EntraUserContextAccessor(CurrentPrincipalAccessor principal) : IUserContextAccessor
{
    public UserContext Current
    {
        get
        {
            var user = principal.Get();
            if (user?.Identity?.IsAuthenticated != true)
                return new UserContext("anonymous", "Not signed in", "none", false, []);
            var upn = user.GetLoginHint() ?? user.FindFirst("preferred_username")?.Value ?? user.Identity.Name ?? "unknown";
            var name = user.GetDisplayName() ?? user.FindFirst("name")?.Value ?? upn;
            return new UserContext(upn, name, "Entra ID user", false, null);
        }
    }
}
