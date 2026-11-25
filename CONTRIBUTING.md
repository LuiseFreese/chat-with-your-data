# Contributing

Thanks for your interest! This repository is an educational sample demonstrating governed conversational BI with
.NET 10 and Microsoft Fabric.

## Getting started

```powershell
dotnet restore
dotnet build
dotnet test          # must stay green; all tests run offline
.\run-local.ps1      # manual check: http://localhost:5130
```

## Ground rules

- **No tenant identifiers or secrets.** Never commit workspace/model/warehouse ids, tenant ids, UPNs, endpoints or
  keys. Generated artifacts under `fabric/` must only contain the `YOUR-…` placeholders (CI checks this).
- **Keep the architecture.** The separation Domain → Application → Infrastructure → Web and the rule
  "the LLM interprets, it never executes" are the point of the sample – PRs that bypass validation or execute
  model-generated SQL/DAX will not be accepted.
- **Determinism.** The synthetic dataset must stay deterministic (same seed ⇒ same numbers). If you change the
  generator, regenerate `fabric/` (`dotnet run --project tools/Northstar.Fabric.Tool -- generate`) and update the
  expected values in tests and docs.
- **Tests.** Add focused tests for behavior you introduce or fix; don't reduce coverage of the validation rules.

## Pull requests

1. Fork, create a topic branch.
2. Make the change, run `dotnet test` (and `dotnet format` if you touched C#).
3. Describe *why* in the PR text; link an issue where applicable.

## Reporting issues

Use GitHub issues for bugs and ideas. For security reports see [SECURITY.md](SECURITY.md).
