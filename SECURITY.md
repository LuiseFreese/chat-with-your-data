# Security Policy

## Supported versions

This repository is an educational sample. There are no supported release branches; fixes land on `main`.

## Reporting a vulnerability

Please report suspected vulnerabilities **privately** via GitHub's
[private vulnerability reporting](https://docs.github.com/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability)
on this repository (Security → Report a vulnerability). Do not open a public issue for security reports.

Please include reproduction steps and the affected file(s). You should receive a reaction within a reasonable time
frame; this is a spare-time maintained sample, not a commercial product with an SLA.

## Scope and threat model

- The sample is designed for **local development use** (`localhost`). It has not been hardened for public hosting.
- Secrets are never committed: Entra client secrets and Azure OpenAI keys belong in `dotnet user-secrets` or
  environment variables (see `.env.example`).
- The LLM never executes queries. All data access goes through server-side validation, a deterministic DAX builder and
  the Power BI Execute Queries API with the signed-in user's delegated token, so Fabric row-level security applies.
- Known, deliberate limitations (in-memory token cache, cookie-authenticated API, no rate limiting in the app itself)
  are documented in [docs/PRODUCTION_LIMITATIONS.md](docs/PRODUCTION_LIMITATIONS.md). Reports about these documented
  limitations are welcome as regular issues/PRs rather than vulnerability reports.
