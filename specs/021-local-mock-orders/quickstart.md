# Quickstart: Local Mock Orders and Order Deletion

This guide proves the feature works. No step makes, or needs, a real TCGplayer call.

## A. Automated tests

```powershell
dotnet test backend/tests/LootSingles.UnitTests/LootSingles.UnitTests.csproj
dotnet test backend/tests/LootSingles.IntegrationTests/LootSingles.IntegrationTests.csproj
npm --prefix frontend run test
npm --prefix frontend run test:e2e -- order-deletion tcgplayer-import
```

Each row names a behavior the tests must show, and the requirement it covers:

| Behavior | Covers |
|---|---|
| In Development, **Get new orders** imports the synthetic orders, and every request goes to the stub host, including when real-looking keys and a `BaseUrl` are configured | FR-001, FR-005, SC-003 |
| In Production and Testing, the synthetic handler is not registered and no configuration value turns it on | FR-004, SC-002 |
| A second press in Development reports "N already imported" | FR-007 |
| No embedded synthetic file references a TCGplayer host | R4, SC-003 |
| A picker gets 403 from the summary, the delete and the delete-all endpoints, and the list says `canDelete: false` | FR-010, SC-006 |
| With `AllowTouchedOrders` false, a claimed, released, picked or packed order is refused with `order_claimed_before`, and an untouched one is deleted | FR-013, FR-020, SC-008 |
| An order claimed at the same moment as its delete is either claimed or deleted, never both | R7 |
| A deleted order disappears from the list, detail, Pick Next, dashboard counts and packing lookup, and its lines, issues and claim fields are unchanged in the database | FR-016, SC-009 |
| A deleted order's packing slip row is gone, and its packing-slip access rows remain | FR-016 |
| Re-importing a deleted order, by PDF and by API, creates a new visible order, and the old row is unchanged | FR-017 |
| Delete-all leaves orders imported after `asOf`, and returns the deleted and kept counts | FR-012, Edge Cases |
| Deleting an already-deleted order returns `order_already_deleted` | Edge Cases |
| A picker acting on a deleted order sees "This order no longer exists" | FR-018 |
| The confirmations name the order, say "Delete order" / "Keep order", and Delete all stays disabled until `DELETE` is typed | FR-014, FR-021–FR-023 |

## B. Local run (a person, or an AI tool on its own LocalDB)

1. Start the app as usual (`scripts/start-dev.ps1`), sign in as a Manager/Admin, and open **Import**.
2. Press **Get new orders**. Eight SYN orders import. Press it again: "8 already imported".
3. Open SYN-0002-A1. The quantity-2 line is emphasised. SYN-0009-A1 shows "No number". Images show the placeholder.
4. Pick and pack SYN-0001-A1 end to end.
5. On **Browse Orders**, open the ⋯ menu for SYN-0001-A1 and choose **Delete order…**, then **Delete order**. It's gone. Press **Get new orders**: SYN-0001-A1 imports again as a fresh, unpicked order.
6. Choose **Delete orders…**, type `DELETE`, and confirm. The list is empty.

## C. Production rule, locally

Set `$env:OrderDeletion__AllowTouchedOrders = 'false'`, restart, and claim one order then release it. Its ⋯ menu says it can't be deleted, and **Delete orders…** reports it as kept.

## D. Stage setup (a person)

In the Azure portal, open the stage Container App, then **Application → Containers → Edit and deploy → Environment variables**. Add `OrderDeletion__AllowTouchedOrders` with the value `true`, then **Save → Create**.

Do **not** add it to production. Without it, production allows deleting only orders nobody has claimed.

## E. Production check after deploy (a person)

Sign in to production as a Manager/Admin. A claimed order's ⋯ menu says it can't be deleted. Don't delete anything unless you're cleaning up a bad import.
