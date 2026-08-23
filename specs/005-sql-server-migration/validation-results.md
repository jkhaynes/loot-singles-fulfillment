# Validation Results: SQL Server-Only Persistence

## Phase 3 Red evidence

Command:

```powershell
dotnet test backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj --filter "FullyQualifiedName~MigrationTests|FullyQualifiedName~SqlServerSchemaTests|FullyQualifiedName~SqlServerTransactionTests|FullyQualifiedName~SqlServerConcurrencyTests|FullyQualifiedName~SqlServerQueryTranslationTests" --no-restore
```

Result before production edits: 11 total, 8 passed, 3 failed as expected.

- `Order_repository_orders_offsets_and_ties_in_sql`: the existing client-side tie ordering differed from the new deterministic assertion and the executed SQL had no database ordering.
- `Dashboard_repository_orders_offsets_and_ties_in_sql`: the executed SQL contained no `ORDER BY`; ordering occurred after materialization.
- `Employee_repository_orders_audit_offsets_and_ties_in_sql`: equal timestamps had no deterministic tie-breaker and ordering occurred after materialization.

The migration/model, schema/index, transaction/atomicity, and concurrent SQL Server duplicate tests were already Green. This proved that the checked-in migrations match the current model, so T018 required no forward corrective migration.

## Phase 3 Green evidence

- Phase 3 SQL Server persistence suite: 11 passed, 0 failed. This includes repository query translation, import and employee duplicate translation, migrations, schema constraints/indexes, transactions, atomicity, and concurrency.
- Foundational SQL Server fixture suite: 4 passed, 0 failed.
- Backend unit suite: 68 passed, 0 failed.
- Backend solution build: succeeded with 0 errors.
- CSharpier was applied to all changed C# files and rechecked after formatting.

The integration project still temporarily references SQLite for tests scheduled for conversion in Phase 4 (T025-T033); the package audit warning therefore remains expected until that phase.
