# Data Model: Automated Stage and Production Deployment

**Feature**: 019 | **Date**: 2026-09-22

This feature adds **one table**, changes no existing entity, and adds no product data. Everything
else it introduces is infrastructure, which has no data model.

## New: Data Protection keys

ASP.NET Core's Data Protection system encrypts the session cookie. Its key ring currently lives in
memory, which is why a container restart signs everyone out (research.md §5). Persisting it makes
sessions survive scale-to-zero and releases (FR-026).

| Aspect | Value |
|---|---|
| Table | `DataProtectionKeys` |
| Owner | The framework, via `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` |
| Shape | `Id` (int, identity), `FriendlyName` (string, nullable), `Xml` (string) — defined by the package, not by this project |
| Written by | The framework, when it creates or rotates a key |
| Read by | The framework, at startup and on cookie validation |
| Read by application code | Never |

`LootSinglesDbContext` implements `IDataProtectionKeyContext` and exposes
`DbSet<DataProtectionKey> DataProtectionKeys`. One migration creates the table.

**Rules**

- The `Xml` column holds key material. It is **not** a customer-data column and MUST NOT be logged,
  echoed in diagnostics, or returned by any endpoint (FR-022). Protection at rest is the database's
  own encryption.
- The key ring is scoped by a **fixed application name**. Changing that name invalidates every
  active session, so it is set once and treated as a constant.
- Each environment has its own database and therefore its own key ring. A cookie issued by stage is
  not valid in production, which is the intended isolation (FR-006).
- No retention or cleanup is implemented. The framework manages key lifetime itself.

## Unchanged

No existing entity is modified. `Order`, `OrderLine`, `Employee`, `PickingIssue`, `PackingSlip`,
`PackingSlipAccess`, `EmployeeAuditEvent` and every other table keep their current shape. This
feature changes **where the application runs**, not what it records.

In particular:

- No tenant, environment, site or location column is added anywhere. This remains single-business
  software (constitution Principle X), and environment separation is achieved by separate databases,
  not by a discriminator column.
- No deployment, release or audit entity is introduced. Release history lives in GitHub, which
  already records it; duplicating it into the application database would be a second source of truth
  for something the application never reads.

## Operational entities (no database representation)

These exist per environment in Azure and are configuration, not data. They are listed because the
setup checklist and the contracts refer to them by name.

| Entity | Instances | Notes |
|---|---|---|
| Environment | `stage`, `prod` | A resource group holding everything below. Identical privacy and security configuration in both (FR-029). |
| Application identity | 1 per environment | `db_datareader` + `db_datawriter`. Used by the Container App. |
| Migration identity | 1 per environment | Adds `db_ddladmin`. Used by the migrate and bootstrap jobs only. |
| Release | 1 per built commit | Identified by the commit SHA; addressed as the image tag `sha-<commit>`. The same release may run on stage and later in production, never rebuilt (FR-014). |

## Migration

One new EF Core migration, additive only: it creates `DataProtectionKeys` and touches nothing else.
It satisfies FR-016 trivially — the preceding version of the application ignores a table it does not
know about, so restoring the previous revision after a failed release leaves a working application.
