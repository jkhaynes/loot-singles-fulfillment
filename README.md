# Loot Singles Fulfillment

An internal fulfillment tool for **Loot Card Shop**, focused on helping employees accurately pick TCGplayer trading-card singles orders.

## Problem Being Solved

Today, pickers work from printed TCGplayer invoices. This can lead to mistakes such as:

- Wrong card
- Wrong quantity
- Missing item
- Wrong variant
- Same card pulled from the wrong set

Some of these fulfillment issues are only discovered after the order has shipped, when the customer contacts Loot.

## V1

V1 replaces the printed invoice for the **picking** portion of fulfillment with a purpose-built, responsive digital experience. At a high level, V1 provides:

- A responsive picking experience for both desktop and mobile phone
- Employee login (individual accounts)
- An order queue
- **Choose Order** and **Pick Next Order** workflows
- Exclusive order claiming, so multiple employees can pick concurrently without collisions
- Set-aware picking, grouped to match Loot's physical storage organization
- Highly prominent treatment of quantity greater than one
- Prominent variant and card detail information
- Picking issue reporting, so reality is always represented accurately
- Pick completion history
- Card imagery, shown only when confidently matched

> **No image is better than the wrong image.**

## Current Status

The project is currently in product definition and technical discovery. Application implementation has not begun.

## Approved Technology Stack

- Responsive Progressive Web App (PWA)
- React
- TypeScript
- ASP.NET Core Web API
- C#
- Entity Framework Core
- Azure SQL Database
- Azure Static Web Apps
- Azure Container Apps
- xUnit
- Vitest
- React Testing Library
- Playwright

## Development Setup

### Database configuration

Normal manual and hosted development uses an externally managed Azure SQL database named
`loot-singles-dev`. The application has no local database or alternate-provider fallback. Automated
tests use disposable SQL Server containers and never use this development database.

Set the required configuration in ASP.NET Core Secret Manager; replace the placeholder at the
prompt and never place the resulting value in a tracked file:

```powershell
dotnet user-secrets set "ConnectionStrings:LootSingles" "<authorized Azure SQL connection string>" --project backend/src/LootSingles.Api
```

Prefer Microsoft Entra authentication for developers and managed identity for hosted workloads so
the connection contains no password. The same key may be supplied by approved hosted configuration
or as environment variable `ConnectionStrings__LootSingles`. Missing, blank, unauthorized,
firewall-blocked, paused, or otherwise unreachable configuration fails explicitly; startup never
falls back to another database.

Use the configured value with EF Core tooling:

```powershell
dotnet ef database update --project backend/src/LootSingles.Infrastructure --startup-project backend/src/LootSingles.Api
```

### Bootstrap the first Manager/Admin

An empty database has no default credentials. Configure the first manager through temporary Secret
Manager values, run the one-shot command, and then remove the PIN secret:

```powershell
dotnet user-secrets set "BootstrapAdmin:Username" "manager" --project backend/src/LootSingles.Api
dotnet user-secrets set "BootstrapAdmin:DisplayName" "Fulfillment Manager" --project backend/src/LootSingles.Api
dotnet user-secrets set "BootstrapAdmin:Pin" "<4-digit PIN>" --project backend/src/LootSingles.Api

dotnet run --project backend/src/LootSingles.Api -- bootstrap-admin

dotnet user-secrets remove "BootstrapAdmin:Username" --project backend/src/LootSingles.Api
dotnet user-secrets remove "BootstrapAdmin:DisplayName" --project backend/src/LootSingles.Api
dotnet user-secrets remove "BootstrapAdmin:Pin" --project backend/src/LootSingles.Api
```

The command applies pending migrations and creates one active `ManagerAdmin` only when the employee
table is empty. It exits without starting the web server. If any employee already exists, it refuses
without modifying the database; use an authenticated Manager/Admin account and the employee-management
API for all later accounts. Hosted automation may supply the same temporary values as
`BootstrapAdmin__Username`, `BootstrapAdmin__DisplayName`, and `BootstrapAdmin__Pin`, then must remove
them after a successful bootstrap. Never use a shared or well-known PIN.

### Cost-safe Azure SQL development plan

Provisioning is deliberately outside this repository and requires separate approval. Before any
approved provisioning, reverify offer availability in the target subscription and region. As of
2026-08-22, Microsoft's Azure SQL Database free offer documents a monthly allowance per eligible
database of 100,000 serverless vCore seconds, 32 GB of data storage, and 32 GB of backup storage.
Select **Auto-pause the database until next month** when the free limit is reached; do not select
continued paid usage. Monitor the portal's free-amount remaining/consumed metrics. Exhausting the
allowance makes the database unavailable until the next calendar month and is an expected
development state, not a reason to enable billing.

Keep networking default-deny. If public access is separately approved, allow only the developer's
current IP and remove it after use. Do not broadly enable access from Azure services. Prefer a
private endpoint or scoped virtual-network path for hosted access. Clients require outbound TCP
1433. Configure a Microsoft Entra administrator and grant only the required database permissions;
prefer developer Entra identity locally and managed identity when hosted.

References: [Azure SQL free offer](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer?view=azuresql),
[free-offer FAQ](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer-faq?view=azuresql),
[network controls](https://learn.microsoft.com/en-us/azure/azure-sql/database/network-access-controls-overview?view=azuresql),
[Microsoft Entra authentication](https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-aad-overview?view=azuresql),
and [Secret Manager](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0).

Restore the repository-local .NET tools after cloning:

```powershell
dotnet tool restore
```

Format all backend C# code with CSharpier:

```powershell
dotnet csharpier format backend
```

Verify formatting without changing files:

```powershell
dotnet csharpier check backend
```

Format frontend TypeScript, CSS, JSON, and Markdown with Prettier:

```powershell
npm --prefix frontend run format
```

Verify frontend formatting without changing files:

```powershell
npm --prefix frontend run format:check
```

## TCGplayer Integration

The current V1 working approach is defensive parsing of TCGplayer packing-slip PDFs, since they are the only inspected export that preserves the `Order → Product Lines → Quantity` relationship V1 needs. TCGplayer order data is authoritative; the architecture isolates this parsing behind an import boundary so it can be replaced by a future supported TCGplayer API or integration without redesigning the picker workflow.

## Card Catalog Enrichment

Current provider direction, subject to change:

| Game | Provider |
|---|---|
| Magic: The Gathering | Scryfall |
| Pokémon Trading Card Game | Pokémon TCG API |
| Disney Lorcana | Lorcast |
| One Piece Card Game | TBD |

TCGplayer order data remains authoritative. Catalog enrichment is supplemental and must never silently overwrite or override it.

## Product Owner

The Loot Card Shop owner is the Product Owner. Confirmed Product Owner decisions override earlier assumptions, including anything documented here.

## Discovery State

This project has a number of open questions documented in the PRD (see [`docs/prd/Loot_Singles_Fulfillment_PRD_v0.3.md`](docs/prd/Loot_Singles_Fulfillment_PRD_v0.3.md), Sections 41–42). These remain **unresolved** and must not be silently converted into implementation assumptions.

## Documentation

- [Product Requirements Document](docs/prd/Loot_Singles_Fulfillment_PRD_v0.3.md)
- [AI-Assisted Development Workflow](docs/development/ai-assisted-development-workflow.md)
- [`CLAUDE.md`](CLAUDE.md) — project rules for AI-assisted development
