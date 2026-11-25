# Fabric setup (Connected Mode)

Everything the sample needs in Microsoft Fabric is created **from this repository** with
`tools/Northstar.Fabric.Tool` – you do not need any pre-existing warehouse, dataset or demo content.
All objects the tool creates are recognizable: tables live in schema **`cwd`**, items are prefixed **`CWD-`**.
Nothing in `dbo` or in any item the tool did not create is modified, and destructive commands ask for confirmation
(`--yes` skips the prompt).

> Status note: the provisioning flow was built and verified against one tenant. If a step behaves differently in your
> tenant, please open an issue – see also "Post-deploy steps" below, which capture the known API quirks.

## Prerequisites

- A Fabric workspace on a capacity (a [Fabric trial](https://learn.microsoft.com/fabric/get-started/fabric-trial) works) in **your** tenant
- You are workspace **Admin** (item creation, role assignments)
- Tenant setting **Dataset Execute Queries REST API** enabled (Fabric admin portal)
- Azure CLI signed in to that tenant: `az login --tenant <your-tenant-id>`
- .NET 10 SDK

## Configuration

The tool reads environment variables only – there are no built-in defaults pointing at any real environment
(see [.env.example](../.env.example)):

| Variable | Meaning |
|---|---|
| `NORTHSTAR_WORKSPACE_ID` | id of your Fabric workspace (URL: `…/groups/<id>/…`) |
| `NORTHSTAR_WAREHOUSE_ID` / `NORTHSTAR_WAREHOUSE_NAME` | the warehouse to use (printed by `create-warehouse`) |
| `NORTHSTAR_SQL_ENDPOINT` | the warehouse SQL endpoint hostname (`….datawarehouse.fabric.microsoft.com`) |
| `NORTHSTAR_RLS_USERS` | optional – RLS test users in your tenant: `viewer@yourtenant.example=West;manager@yourtenant.example=North\|South` |

## Provisioning walkthrough

```powershell
cd tools/Northstar.Fabric.Tool

$env:NORTHSTAR_WORKSPACE_ID = "<your-workspace-id>"

dotnet run -- stats              # offline: dataset headline numbers
dotnet run -- generate           # offline: (re)writes fabric/sql, fabric/seed, TMDL (placeholders), metric-catalog.json
dotnet run -- create-warehouse   # creates (or reuses) warehouse CWD-Warehouse and prints ID/NAME/SQL endpoint

$env:NORTHSTAR_WAREHOUSE_ID   = "<printed id>"
$env:NORTHSTAR_WAREHOUSE_NAME = "CWD-Warehouse"
$env:NORTHSTAR_SQL_ENDPOINT   = "<printed endpoint>"
$env:NORTHSTAR_RLS_USERS      = "viewer.user@yourtenant.example=West"   # optional, for RLS demos

dotnet run -- create-schema      # fabric/sql/01_create_schema_cwd.sql → schema cwd + 9 tables
dotnet run -- seed               # DELETE + INSERT of the deterministic synthetic dataset (asks for confirmation)
dotnet run -- ingest-excel       # loads fabric/seed/CWD-FinanceAdjustments.xlsx (Source='Excel' rows)
dotnet run -- deploy-model       # creates/updates semantic model CWD-GovernedModel from TMDL and prints its id
dotnet run -- grant              # workspace Viewer + dataset Read/Build for NORTHSTAR_RLS_USERS (asks for confirmation)
dotnet run -- rls-members        # adds NORTHSTAR_RLS_USERS to the 'Regional Reader' role via XMLA (or use the portal)
dotnet run -- validate           # local evaluator vs warehouse SQL vs Execute Queries must agree
```

Then configure the web app (`Fabric__WorkspaceId`, `Fabric__GovernedSemanticModelId` = output of `deploy-model`,
also written to `fabric/governed-model-id.txt`, which is gitignored).

Expected `validate` output for the default seed, Q3 2026: Gross **5,181,871.00**, Net **4,436,342.30**,
Adjusted Net **4,422,042.30** – identical in all three systems.

### Why the committed TMDL contains placeholders

`fabric/semantic-model/**` is generated with `YOUR-WAREHOUSE-SQL-ENDPOINT…`/`YOUR-WAREHOUSE-ID` placeholders.
`deploy-model` substitutes your real values **in-memory only**, so no environment identifier can ever be committed.

## Post-deploy steps the API does not do for you

1. **Large storage format** – API-created models default to `targetStorageMode = Abf`; Direct Lake needs `PremiumFiles`:
   `PATCH https://api.powerbi.com/v1.0/myorg/groups/{ws}/datasets/{id}` body `{"targetStorageMode":"PremiumFiles"}`.
2. **Exact SQL endpoint casing** in the model's connection: host label upper-case, domain lower-case
   (`ABC123….datawarehouse.fabric.microsoft.com`). The tool emits this casing automatically; a wrong casing yields
   "A connection could not be made to the data source".
3. **Frame the model** after every definition change:
   `POST …/datasets/{id}/refreshes` body `{"type":"full","commitMode":"transactional"}`.

## Semantic model

Tables: `Date` (marked as date table), `Region`, `Business Unit`, `Customer`, `Product`, `Sales`, `Returns`,
`Adjustments`, hidden `Region Access`. Relationships: facts → `Date` on their date columns; `Sales` → `Customer`/
`Product`; all facts → `Region`/`Business Unit`.

| Measure | DAX | Business meaning |
|---|---|---|
| Gross Revenue | `CALCULATE(SUM(Sales[Gross Amount]), Sales[Order Status] <> "Cancelled")` | Order value, non-cancelled orders. Sales' number. |
| Total Discounts | `CALCULATE(SUM(Sales[Discount Amount]), … <> "Cancelled")` | All discounts granted |
| Total Returns | `SUM(Returns[Amount])` | Credit notes for returned goods |
| Net Revenue | `[Gross Revenue] - [Total Discounts] - [Total Returns]` | Finance's monthly-close number, before manual corrections |
| Finance Adjustments | `CALCULATE(SUM(Adjustments[Amount]), Adjustments[Status] = "Approved")` | Approved manual corrections (pipeline + Excel) |
| Finance Adjustments Pipeline Only | … `AND Adjustments[Source] = "Pipeline"` | Reconciliation screen only |
| Adjusted Net Revenue | `[Net Revenue] + [Finance Adjustments]` | Management's number |
| Order Count | `CALCULATE(DISTINCTCOUNT(Sales[Order Id]), … <> "Cancelled")` | Distinct non-cancelled orders |
| Average Order Value | `DIVIDE([Net Revenue], [Order Count])` | |

Every measure carries a `///` description with owner and formula; the same definitions live in
`src/Northstar.Domain/Metrics/MetricCatalog.cs` (the app's single source of queryable metrics) and must be kept in sync
manually – there is no automatic synchronisation.

## Row-level security (data-driven)

```
role 'Regional Reader'  (modelPermission: read)
  Region        : 'Region'[Region] IN SELECTCOLUMNS(FILTER(ALL('Region Access'),
                  'Region Access'[User Principal Name] = USERPRINCIPALNAME()), "Region", 'Region Access'[Region])
  Region Access : 'Region Access'[User Principal Name] = USERPRINCIPALNAME()
```

- The region mapping lives in the warehouse table `cwd.RegionAccess`, filled by `seed` from `NORTHSTAR_RLS_USERS`.
- Role **members** are tenant-specific and are *not* part of the committed TMDL. Assign them in the Fabric portal
  (model → Security) or with `rls-members`.
- To demonstrate RLS you need **two accounts in your tenant**: a workspace **Viewer** with dataset Read+Build, role
  membership and a `RegionAccess` row (sees only their regions), and an unrestricted account (workspace
  Admins/Members/Contributors bypass RLS by design). The sample cannot and does not create users in your tenant.
- Verify by signing into the app as the Viewer: the Query Inspector shows the Fabric-executed DAX and only the mapped
  regions come back. `validate` also attempts an impersonation check if `NORTHSTAR_RLS_USERS` is set (REST
  impersonation is not available to every caller – signing in as the Viewer is the reliable check).

## Excel corrections pipeline

`fabric/seed/CWD-FinanceAdjustments.xlsx` (sheet `Adjustments`) → `ingest-excel` (delete-and-insert by
`Source = 'Excel'`) → `cwd.FactFinanceAdjustment` → measure `[Finance Adjustments]` → `[Adjusted Net Revenue]`.
In a real pipeline a Dataflow Gen2 over a shared SharePoint library would replace the console step
(`Excel.Workbook(Web.Contents(<url>))` → `Table.PromoteHeaders` → `Table.TransformColumnTypes` → destination
`cwd.FactFinanceAdjustment`).

## Clean-up

Drop the nine `cwd.*` tables, delete the items `CWD-GovernedModel` and (if created) `CWD-Warehouse`, and remove the
Viewer role assignments you granted. The tool creates nothing else.
