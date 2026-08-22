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
