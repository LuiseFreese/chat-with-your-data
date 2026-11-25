# Microsoft Entra ID setup (Connected Mode)

Connected Mode signs users in with Entra ID and acquires a **delegated** Power BI token for the signed-in user. Fabric
row-level security therefore applies to the real person – no service principal, no client-supplied identity.

## Create the app registration (in your tenant)

| Setting | Value |
|---|---|
| Display name | e.g. `Northstar Chat with Data (Sample)` |
| Sign-in audience | Single tenant |
| Web redirect URIs | `https://localhost:7245/signin-oidc`, `http://localhost:5130/signin-oidc` |
| Front-channel logout | `https://localhost:7245/signout-oidc` |
| ID tokens | enabled |
| Delegated permissions | Power BI Service: `Dataset.Read.All`, `Workspace.Read.All` · Microsoft Graph: `User.Read` |
| Admin consent | grant for your tenant |
| Client secret | create one; store it **only** in `dotnet user-secrets` |

With Azure CLI:

```powershell
$manifest = '[{"resourceAppId":"00000009-0000-0000-c000-000000000000","resourceAccess":[{"id":"7f33e027-4039-419b-938e-2f8ca153e68e","type":"Scope"},{"id":"b2f1b2fa-f35c-407c-979c-a858a808ba85","type":"Scope"}]},{"resourceAppId":"00000003-0000-0000-c000-000000000000","resourceAccess":[{"id":"e1fe6dd8-ba31-4d61-89e7-88639da4683d","type":"Scope"}]}]'
Set-Content manifest.json $manifest
$app = az ad app create --display-name "Northstar Chat with Data (Sample)" --sign-in-audience AzureADMyOrg `
  --web-redirect-uris "https://localhost:7245/signin-oidc" "http://localhost:5130/signin-oidc" `
  --enable-id-token-issuance true --required-resource-accesses "@manifest.json" -o json | ConvertFrom-Json
az ad sp create --id $app.appId
az ad app permission admin-consent --id $app.appId
$secret = az ad app credential reset --id $app.appId --display-name local-dev --years 1 --query password -o tsv

cd src/Northstar.Web
dotnet user-secrets set "AzureAd:ClientId" $app.appId
dotnet user-secrets set "AzureAd:TenantId" "<your-tenant-id>"
dotnet user-secrets set "AzureAd:ClientSecret" $secret
```

The GUIDs in the manifest are **Microsoft's well-known permission ids**, identical in every tenant:
`7f33e027-…` = Power BI `Dataset.Read.All`, `b2f1b2fa-…` = Power BI `Workspace.Read.All`
(Power BI Service app `00000009-…`), `e1fe6dd8-…` = Graph `User.Read` (Graph app `00000003-…`).

## Application configuration

`appsettings.json` → `AzureAd` section (Instance, CallbackPath) + user-secrets for TenantId/ClientId/ClientSecret
(template: [appsettings.example.json](../appsettings.example.json)). `Program.cs` uses
`AddMicrosoftIdentityWebApp(...).EnableTokenAcquisitionToCallDownstreamApi(Fabric:Scopes).AddInMemoryTokenCaches()`
and a fallback authorization policy – every page and API endpoint requires a signed-in user in Connected Mode.
The app validates this configuration at startup and names any missing key.

The Power BI token is requested with scope `https://analysis.windows.net/powerbi/api/Dataset.Read.All` through
`ITokenAcquisition.GetAccessTokenForUserAsync(scopes, user: <circuit principal>)` (`DelegatedPowerBiTokenProvider`).
Inside Blazor Server circuits the principal comes from `AuthenticationStateProvider`, for API calls from `HttpContext`.

## Tenant / workspace prerequisites

| Requirement | Why |
|---|---|
| Fabric tenant setting **Dataset Execute Queries REST API** = enabled | the only query path the app uses |
| User has workspace access (Viewer is enough) | Execute Queries requires workspace access |
| User has **Build** permission on `CWD-GovernedModel` | Execute Queries requires dataset Read + Build |
| RLS-restricted user is a member of role `Regional Reader` *and* has rows in `cwd.RegionAccess` | otherwise Fabric returns no rows for Viewers |
| Workspace Admins/Members/Contributors | bypass RLS by design – use Viewer accounts to demonstrate restrictions |

## Test accounts for RLS

To show row-level security you need **two real users in your tenant** (the sample cannot create them):

| Account | Setup | Expected result |
|---|---|---|
| Viewer (e.g. `viewer.user@yourtenant.example`) | workspace Viewer, dataset Read+Build, member of `Regional Reader`, row in `cwd.RegionAccess` (e.g. `West`) | sees only their regions |
| Admin (you) | workspace Admin | sees all regions (not subject to RLS) |

Configure the Viewer in `NORTHSTAR_RLS_USERS` and run `grant`, `rls-members` and `seed`
(see [FABRIC_SETUP.md](FABRIC_SETUP.md)). Sign out (`/MicrosoftIdentity/Account/SignOut`) and use an
InPrivate/second browser profile for the second identity.

## Token-cache caveat

The in-memory token cache is lost on restart. If the app restarts while the auth cookie is still valid, the first
Fabric call throws `MicrosoftIdentityWebChallengeUserException`; the app maps it to "Your sign-in needs to be
refreshed – sign out and sign in again" (`SignInRequired`).

## Security notes

- No tokens or secrets are logged; HTTP error bodies are truncated before they reach logs/UI.
- The app never accepts a user or region parameter to widen data access; RLS is evaluated by Fabric for the token's subject.
- Local Demo Mode uses simulated personas and says so on every screen; it is never presented as Fabric RLS.
