# Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| App shows **LOCAL DEMO MODE** although you wanted Fabric | `Demo:Mode` is not `Connected` | use `run-connected.ps1` or set env `Demo__Mode=Connected` |
| Redirect loop / AADSTS50011 at sign-in | redirect URI mismatch | app registration must contain `https://localhost:7245/signin-oidc` (and `http://localhost:5130/signin-oidc`) |
| AADSTS7000215 invalid client secret | secret missing/expired in user-secrets | `dotnet user-secrets set "AzureAd:ClientSecret" <value> --project src/Northstar.Web` |
| "Your sign-in needs to be refreshed before Fabric can be queried" | in-memory token cache lost after restart (`MicrosoftIdentityWebChallengeUserException`) | Sign out → Sign in |
| `FabricForbidden` – no Build permission | user lacks Build on `CWD-GovernedModel` | `dotnet run --project tools/Northstar.Fabric.Tool -- grant` or share the model with Build |
| `FabricNotFound` | wrong `Fabric:GovernedSemanticModelId` / user not in workspace | id is in `fabric/governed-model-id.txt`; add user as Viewer |
| `FabricQueryError`: "The dataset has not been refreshed" | Direct Lake model not framed after a definition change | `POST https://api.powerbi.com/v1.0/myorg/groups/{ws}/datasets/{id}/refreshes` `{"type":"full","commitMode":"transactional"}` |
| "A connection could not be made to the data source" (model) | SQL endpoint casing in `expressions.tmdl` or storage mode `Abf` | see FABRIC_SETUP.md "Post-deploy steps" |
| Viewer gets no rows / error although not in a region | user not in RLS role members or missing `cwd.RegionAccess` rows | add the user via the Fabric portal (model → Security) or `rls-members`, and set `NORTHSTAR_RLS_USERS` + re-run `seed` for the `RegionAccess` row |
| Admin sees all regions "despite RLS" | Admins/Members/Contributors bypass RLS | use a Viewer persona |
| `FabricRateLimited` | 120 Execute Queries per user per minute | wait for `Retry-After` |
| Azure OpenAI `Unavailable` in System Health | no Entra token (DefaultAzureCredential) or missing role | `az login --tenant <tenant>`; role *Cognitive Services OpenAI User* on the resource; or switch parser to Deterministic |
| Azure OpenAI 400 "response_format … strict" | model/deployment does not support structured outputs | use gpt-4.1-mini / gpt-4o-2024-08-06+ |
| Intent parse returns `UNKNOWN_METRIC` for a reasonable question (local mode) | deterministic parser didn't match a synonym | add synonym in `MetricCatalog.CreateDefault()` |
| "cannot be broken down by Product Category" (`GRAIN_MISMATCH`) | Net/Adjusted revenue include returns/adjustments booked per order/region | expected governance behaviour; ask for Gross Revenue / Order Count by category |
| HTTPS certificate warning on localhost | dev cert not trusted | `dotnet dev-certs https --trust` |
| Port 5130/7245 in use | previous instance running | `Get-Process -Name Northstar.Web` → `Stop-Process -Id <pid>` |
| Tests fail with `ObjectDisposedException` on HttpContent | reading request content after disposal in a stub | read the body inside the handler (see `IntegrationTests.StubHandler`) |
| `winget`/`dotnet` not found | PATH not refreshed after SDK install | open a new terminal |

## Logs

JSON console lines with `CorrelationId` and `User` scopes. Correlation id is shown in every result's inspector and in
error cards. Nothing sensitive (tokens, secrets, full error bodies) is logged.

## Useful read-only checks

```powershell
# dataset flags (RLS roles active? storage mode?)
$t = az account get-access-token --resource https://analysis.windows.net/powerbi/api --query accessToken -o tsv
Invoke-RestMethod -Headers @{Authorization="Bearer $t"} https://api.powerbi.com/v1.0/myorg/groups/<ws>/datasets/<id> |
  Select-Object name,targetStorageMode,isEffectiveIdentityRequired,isEffectiveIdentityRolesRequired

# warehouse totals vs model
dotnet run --project tools/Northstar.Fabric.Tool -- validate
```
