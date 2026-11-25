# Production limitations and open points

This is an educational sample. It is production-*oriented* in its architecture, not production-*ready* or
production-certified. Known gaps:

## Platform / tenancy
- A Fabric **trial capacity** works for the sample but expires; move to an F SKU for anything long-lived.
- The sample isolates its data by schema (`cwd`) and item prefix (`CWD-`), not by dedicated workspace – create a
  dedicated workspace if you want hard isolation.
- The Excel ingestion runs from a console tool. A Dataflow Gen2 over a shared SharePoint library is documented but not
  included.
- The workspace is not Git-connected; TMDL/SQL are versioned in this repo only.

## Identity and security
- In-memory token cache → sign-in required after every app restart. Use a distributed cache (Redis/SQL) in production.
- Client secret in user-secrets; production should use certificate credentials or managed identity + Federated Identity.
- RLS membership is maintained via portal/XMLA and a warehouse table. Production would use Entra security groups as
  role members and keep `RegionAccess` in a governed source.
- Workspace Admins bypass RLS – administrators see everything; business users must be Viewers.
- Impersonation through Execute Queries is not available to every caller; RLS verification is manual (sign in as the Viewer).
- No rate limiting / per-user quotas in the app itself beyond Fabric's 120 req/min; add ASP.NET rate limiting middleware.
- The REST API relies on the same cookie sign-in as the UI. External consumers need a bearer-token (JWT) scheme with
  On-Behalf-Of token exchange to Power BI.

## Data and semantics
- Synthetic data; Q4 2026 exists in the model although the dataset's "as of" date is November 2026.
- Finance adjustments are modelled at (date, region, business unit) grain; a real pipeline would reference invoices/orders.
- Returns and adjustments cannot be split by product/customer (deliberate `GRAIN_MISMATCH`), which also prevents
  Net/Adjusted Revenue per category – a modelling decision that a real finance team would challenge.
- Only two years and six dimensions are modelled; `Period` deliberately rejects years outside 2025–2026.
- Prototype Mode is an intentionally simplified educational example over the same synthetic dataset; its number differs
  from Production because it ignores order status, returns and finance adjustments – by design.

## Query layer
- Deterministic DAX supports: single measure, ≤2 dimensions, 1 compare period, validated member filters, Top-N ≤25.
  No running totals, no YoY %, no multiple metrics per query, no custom date ranges.
- Execute Queries limits (1 query/call, 1 table, 100k rows / 1M values / 15 MB) are respected by construction but
  truncation is only detected heuristically (row count ≥ 100k).
- Azure OpenAI output passes through strict schema validation and the server-side validator, but prompt quality for
  edge cases (relative periods like "last quarter") is untested; such questions fall back to `All` or are rejected.
- Conversation memory is per circuit/in-memory; nothing is persisted.

## Observability
- Audit store is in-memory (lost on restart). Production: persist `QueryExecutionRecord`s (Fabric SQL DB / Cosmos / App Insights).
- OpenTelemetry is configured for tracing but no exporter is wired; add OTLP/Application Insights exporter.
- Health checks ping live services on every page load of System Health – cache them in production.

## Testing
- 58 unit/integration tests (domain, validation, DAX, reconciliation, permissions, tenant-independent configuration,
  placeholder-only artifact generation, mocked Fabric HTTP, API via `WebApplicationFactory`). No browser end-to-end
  tests (Playwright).
- CI (GitHub Actions) builds, tests, smoke-tests Local Demo Mode, scans dependencies and secrets, and verifies that no
  tenant-specific identifiers are present. Connected-mode flows against a real Fabric tenant are **not** covered by CI
  and must be verified manually with the provisioning tool (`validate`, `validate-openai`).
