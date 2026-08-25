-- Clears imported orders (and their order lines) so packing slips can be re-imported for testing.
-- Dev-only utility script - not part of the application, not run automatically by anything.
--
-- Usage:
--   sqlcmd -S <server> -d <database> -i backend/scripts/dev/clear-orders.sql
--   (or open in SSMS / Azure Data Studio and execute)
--
-- OrderLines cascade-deletes automatically with its parent Order (FK_OrderLines_Orders_OrderId
-- is ON DELETE CASCADE), so it needs no separate DELETE.
--
-- ImportOrderResults.ResultingOrderId references Orders but is NOT cascading
-- (FK_ImportOrderResults_Orders_ResultingOrderId has no ON DELETE action, i.e. NO ACTION/Restrict),
-- so it must be nulled out first or DELETE FROM Orders will fail with a foreign key violation.
-- This preserves the import history/audit trail (ImportAttempts, ImportOrderResults) - it just
-- detaches it from the now-deleted order.

BEGIN TRANSACTION;

UPDATE ImportOrderResults
SET ResultingOrderId = NULL
WHERE ResultingOrderId IS NOT NULL;

DELETE FROM Orders;

COMMIT TRANSACTION;

-- Optional: also wipe import history entirely (ImportOrderResults cascade-deletes from
-- ImportAttempts), for a fully clean slate instead of just detaching it. Uncomment if wanted
-- instead of the block above:
--
-- BEGIN TRANSACTION;
-- DELETE FROM ImportAttempts;   -- cascades to ImportOrderResults
-- DELETE FROM Orders;           -- cascades to OrderLines
-- COMMIT TRANSACTION;

-- Optional: reset the Orders/OrderLines identity seeds back to 1 after either block above,
-- if you want freshly-imported orders to start from Id 1 again (not required - TcgplayerOrderId,
-- not Id, is what duplicate-import detection keys on):
--
-- DBCC CHECKIDENT ('Orders', RESEED, 0);
-- DBCC CHECKIDENT ('OrderLines', RESEED, 0);
