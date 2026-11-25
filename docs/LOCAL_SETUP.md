# Local setup (Local Demo Mode)

Local Demo Mode is the default and requires **no cloud resources at all** – no Azure subscription, no Microsoft Fabric,
no Entra ID app, no Azure OpenAI key, no outbound network calls.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (the exact version is pinned in `global.json`)

## Run

```powershell
git clone <this-repository> chat-with-your-data
cd chat-with-your-data
dotnet restore
.\run-local.ps1                       # or: run-local.cmd, or plain:
# dotnet run --project src/Northstar.Web --urls http://localhost:5130
```

Open **http://localhost:5130**.

## What you get

| Aspect | Local Demo Mode behaviour |
|---|---|
| Data | Deterministic in-memory synthetic dataset (`SyntheticDataGenerator`, seed `Demo:DataSeed` = 20260325). The same seed always produces the same numbers, on every machine. |
| Intent parsing | Deterministic rule-based parser – no LLM involved |
| Measures | The identical business definitions (`LocalMetricEvaluator`) that the Fabric semantic model publishes in Connected Mode |
| Validation / DAX | The exact same `IntentValidator` and `DaxQueryBuilder` as Connected Mode; the generated DAX is displayed but obviously not executed locally |
| Identity | Simulated personas (switchable in the top bar, clearly labelled "simulated") |
| Authorization | Region restrictions evaluated locally from the persona's regions – a **simulation** of RLS, never presented as real Fabric RLS |

## Guided tour

Open **Example Scenarios** (`/scenarios`) and walk through the six scenarios – each button prefills the chat; nothing
is auto-submitted. See [EXAMPLE_SCENARIOS.md](EXAMPLE_SCENARIOS.md).

## Tests

```powershell
dotnet test
```

All tests run offline; external HTTP is mocked.

## Changing the dataset

- `Demo:DataSeed` (appsettings or `Demo__DataSeed` env var) regenerates the entire dataset deterministically.
- `dotnet run --project tools/Northstar.Fabric.Tool -- stats` prints the headline numbers for the current seed.
- `dotnet run --project tools/Northstar.Fabric.Tool -- generate` regenerates the `fabric/` artifacts (SQL, CSV, Excel,
  TMDL with placeholders) from the same generator – no cloud calls.
