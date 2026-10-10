-- DEV ONLY. Deletes every order and everything that hangs off an order. Run it through
-- Clear-DevOrders.ps1 (and its runner, ClearDevOrders.cs), which refuse any database unless "dev"
-- is a whole word of its name or the server is LocalDB. Never run it by hand against stage or
-- production.
--
-- Parameter @Preview (bit), required:
--   1 = run every statement inside the transaction, report the counts, then ROLL BACK (nothing changes)
--   0 = the same, then COMMIT
-- Returns one result set: TableName, [Rows] (rows deleted, or that would be deleted), in delete order.
--
-- Tables come from the EF model (LootSinglesDbContextModelSnapshot). Foreign keys:
--   OrderLines.OrderId                  -> Orders        CASCADE
--   OrderLines.CurrentPickingIssueId    -> PickingIssues NO ACTION  (cycle with the next one)
--   PickingIssues.OrderLineId           -> OrderLines    CASCADE
--   OrderPackingSlips.OrderId           -> Orders        CASCADE
--   PackingSlipAccesses.OrderId         -> Orders        CASCADE
--   ImportOrderResults.ImportAttemptId  -> ImportAttempts CASCADE
--   ImportOrderResults.ResultingOrderId -> Orders        NO ACTION
-- Claims and pack records are columns on Orders; pick outcomes are columns on OrderLines.
-- Deletes run child-first and explicitly, so no cascade is relied on.
--
-- Kept: Employees (and their PIN hashes), EmployeeAuditEvents (no foreign key to any order
-- table), DataProtectionKeys. Orders and lines reference Employees, never the other way round.

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Deleted TABLE (Step int NOT NULL, TableName sysname NOT NULL, [Rows] int NOT NULL);
DECLARE @Rows int;

BEGIN TRANSACTION;

-- Break the OrderLines <-> PickingIssues cycle so PickingIssues can go first.
UPDATE [OrderLines] SET [CurrentPickingIssueId] = NULL WHERE [CurrentPickingIssueId] IS NOT NULL;

DELETE FROM [PickingIssues];
SET @Rows = @@ROWCOUNT;
INSERT INTO @Deleted VALUES (1, N'PickingIssues', @Rows);

DELETE FROM [PackingSlipAccesses];
SET @Rows = @@ROWCOUNT;
INSERT INTO @Deleted VALUES (2, N'PackingSlipAccesses', @Rows);

DELETE FROM [OrderPackingSlips];
SET @Rows = @@ROWCOUNT;
INSERT INTO @Deleted VALUES (3, N'OrderPackingSlips', @Rows);

DELETE FROM [OrderLines];
SET @Rows = @@ROWCOUNT;
INSERT INTO @Deleted VALUES (4, N'OrderLines', @Rows);

DELETE FROM [ImportOrderResults];
SET @Rows = @@ROWCOUNT;
INSERT INTO @Deleted VALUES (5, N'ImportOrderResults', @Rows);

DELETE FROM [ImportAttempts];
SET @Rows = @@ROWCOUNT;
INSERT INTO @Deleted VALUES (6, N'ImportAttempts', @Rows);

DELETE FROM [Orders];
SET @Rows = @@ROWCOUNT;
INSERT INTO @Deleted VALUES (7, N'Orders', @Rows);

-- A table variable keeps its rows through a rollback, so the preview can still report them.
IF @Preview = 1
    ROLLBACK TRANSACTION;
ELSE
    COMMIT TRANSACTION;

SELECT TableName, [Rows] FROM @Deleted ORDER BY Step;
