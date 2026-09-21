-- Seeds hand-built test orders covering picking behaviours that are awkward to produce by
-- importing a real packing slip. Dev-only utility script - not part of the application, not run
-- automatically by anything.
--
-- Usage:
--   sqlcmd -S <server> -d <database> -i backend/scripts/dev/seed-test-orders.sql
--   (or open in SSMS / Azure Data Studio and execute)
--
-- Re-runnable: it deletes any order whose TcgplayerOrderId starts with 'F8433182-TEST' before
-- inserting, so running it twice leaves one copy of each, not two. It touches nothing else.
--
-- To remove them again:
--   UPDATE ImportOrderResults SET ResultingOrderId = NULL
--   WHERE ResultingOrderId IN (SELECT Id FROM Orders WHERE TcgplayerOrderId LIKE 'F8433182-TEST%');
--   DELETE FROM Orders WHERE TcgplayerOrderId LIKE 'F8433182-TEST%';
--
-- ---------------------------------------------------------------------------------------------
-- CARD DATA IS VERIFIED, NOT GUESSED.
--
-- Every card name, set and collector number below was checked against the same APIs the
-- application resolves images through - Scryfall for Magic, TCGdex for Pokemon, Lorcast for
-- Lorcana - so these orders show real artwork. An earlier version of this file guessed the
-- numbers and 8 of 15 Magic lines pointed at a different card than intended, which resolves to
-- no image at all (the name check fails, and the app correctly refuses to show a wrong picture).
--
-- If you add cards, verify them the same way. A plausible-looking collector number is usually
-- wrong, and the symptom is a missing image rather than an error.
--
-- ONE PIECE HAS NO IMAGE PROVIDER. The application registers providers for Pokemon, Magic and
-- Lorcana only; sourcing a One Piece catalog is still open (PRD §42, Priority 1). One Piece
-- lines therefore can never show an image, which is why they all live in the deliberately
-- image-free order below rather than being sprinkled through the others.
--
-- Field formats mirror what the packing-slip parser actually produces, sampled from imported
-- orders: Magic rarities are single letters, Pokemon rarities are words, Pokemon collector
-- numbers carry the set total, and Lorcana product names carry their subtitle.

SET NOCOUNT ON;

BEGIN TRANSACTION;

-- Clean out previous runs.
UPDATE ImportOrderResults
SET ResultingOrderId = NULL
WHERE ResultingOrderId IN (SELECT Id FROM Orders WHERE TcgplayerOrderId LIKE 'F8433182-TEST%');

DELETE FROM Orders WHERE TcgplayerOrderId LIKE 'F8433182-TEST%';

DECLARE @orderId INT;
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();


-- ============================================================================
-- 1. Every game that can show an image, two sets each.
--    Games group and sort alphabetically: Lorcana TCG, Magic, Pokemon.
--    Crossing between them is the expensive walk.
-- ============================================================================
INSERT INTO Orders (TcgplayerOrderId, Status, ImportedAt)
VALUES ('F8433182-TESTA1-3GAMES', 0, @now);
SET @orderId = SCOPE_IDENTITY();

INSERT INTO OrderLines
  (OrderId, RawDescription, ProductLine, ProductName, [Set], CollectorNumber, Rarity, Condition, Variant, Quantity)
VALUES
  -- Deliberately listed out of grouping order so the screen has real work to do.
  (@orderId, 'Pokemon - SV07: Stellar Crown: Venusaur ex - 001/142 - #001/142 - Double Rare - Near Mint Holofoil',
   'Pokemon', 'Venusaur ex', 'SV07: Stellar Crown', '#001/142', 'Double Rare', 'Near Mint', 'Holofoil', 1),
  (@orderId, 'Magic - Dominaria United: Sheoldred, the Apocalypse - #107 - M - Near Mint',
   'Magic', 'Sheoldred, the Apocalypse', 'Dominaria United', '#107', 'M', 'Near Mint', NULL, 1),
  (@orderId, 'Lorcana TCG - The First Chapter - Ariel - Spectacular Singer - #2 - Super Rare - Near Mint Holofoil',
   'Lorcana TCG', 'Ariel - Spectacular Singer', 'The First Chapter', '#2', 'Super Rare', 'Near Mint', 'Holofoil', 1),
  (@orderId, 'Magic - Bloomburrow: Mabel, Heir to Cragflame - #224 - R - Near Mint Foil',
   'Magic', 'Mabel, Heir to Cragflame', 'Bloomburrow', '#224', 'R', 'Near Mint', 'Foil', 1),
  (@orderId, 'Pokemon - SWSH07: Evolving Skies: Leafeon VMAX - 8/203 - #8/203 - Ultra Rare - Lightly Played Holofoil',
   'Pokemon', 'Leafeon VMAX', 'SWSH07: Evolving Skies', '#8/203', 'Ultra Rare', 'Lightly Played', 'Holofoil', 1),
  (@orderId, 'Lorcana TCG - Rise of the Floodborn - Cinderella - Ballroom Sensation - #3 - Rare - Near Mint Holofoil',
   'Lorcana TCG', 'Cinderella - Ballroom Sensation', 'Rise of the Floodborn', '#3', 'Rare', 'Near Mint', 'Holofoil', 1);


-- ============================================================================
-- 2. Quantity emphasis. 5 products, 19 physical cards, so the two counts
--    diverge sharply. A missed multiple is the costliest picking error
--    (PRD §5.3, §15).
-- ============================================================================
INSERT INTO Orders (TcgplayerOrderId, Status, ImportedAt)
VALUES ('F8433182-TESTB2-BIGQTY', 0, @now);
SET @orderId = SCOPE_IDENTITY();

INSERT INTO OrderLines
  (OrderId, RawDescription, ProductLine, ProductName, [Set], CollectorNumber, Rarity, Condition, Variant, Quantity)
VALUES
  (@orderId, 'Magic - Foundations: Llanowar Elves - #227 - C - Near Mint',
   'Magic', 'Llanowar Elves', 'Foundations', '#227', 'C', 'Near Mint', NULL, 9),
  (@orderId, 'Magic - Foundations: Aegis Turtle - #150 - C - Near Mint',
   'Magic', 'Aegis Turtle', 'Foundations', '#150', 'C', 'Near Mint', NULL, 4),
  (@orderId, 'Pokemon - SV03: Obsidian Flames: Decidueye ex - 015/197 - #015/197 - Double Rare - Near Mint Holofoil',
   'Pokemon', 'Decidueye ex', 'SV03: Obsidian Flames', '#015/197', 'Double Rare', 'Near Mint', 'Holofoil', 3),
  (@orderId, 'Lorcana TCG - The First Chapter - Goofy - Musketeer - #4 - Uncommon - Near Mint',
   'Lorcana TCG', 'Goofy - Musketeer', 'The First Chapter', '#4', 'Uncommon', 'Near Mint', NULL, 2),
  (@orderId, 'Magic - Bloomburrow: Valley Floodcaller - #79 - R - Near Mint',
   'Magic', 'Valley Floodcaller', 'Bloomburrow', '#79', 'R', 'Near Mint', NULL, 1);


-- ============================================================================
-- 3. One game, one set. No box transitions at all - the picker opens one box
--    and never leaves it.
-- ============================================================================
INSERT INTO Orders (TcgplayerOrderId, Status, ImportedAt)
VALUES ('F8433182-TESTC3-ONEBOX', 0, @now);
SET @orderId = SCOPE_IDENTITY();

INSERT INTO OrderLines
  (OrderId, RawDescription, ProductLine, ProductName, [Set], CollectorNumber, Rarity, Condition, Variant, Quantity)
VALUES
  (@orderId, 'Pokemon - SV03: Obsidian Flames: Decidueye ex - 015/197 - #015/197 - Double Rare - Near Mint Holofoil',
   'Pokemon', 'Decidueye ex', 'SV03: Obsidian Flames', '#015/197', 'Double Rare', 'Near Mint', 'Holofoil', 1),
  (@orderId, 'Pokemon - SV03: Obsidian Flames: Toedscruel ex - 022/197 - #022/197 - Double Rare - Near Mint Holofoil',
   'Pokemon', 'Toedscruel ex', 'SV03: Obsidian Flames', '#022/197', 'Double Rare', 'Near Mint', 'Holofoil', 2),
  (@orderId, 'Pokemon - SV03: Obsidian Flames: Victini ex - 033/197 - #033/197 - Double Rare - Moderately Played Holofoil',
   'Pokemon', 'Victini ex', 'SV03: Obsidian Flames', '#033/197', 'Double Rare', 'Moderately Played', 'Holofoil', 1);


-- ============================================================================
-- 4. Many boxes, one card in each. Eight sets, eight single cards, so the
--    picker crosses a box boundary on every step. Grouping saves nothing here
--    - this is the order that shows what ordering is worth on its own.
-- ============================================================================
INSERT INTO Orders (TcgplayerOrderId, Status, ImportedAt)
VALUES ('F8433182-TESTD4-MANYBOX', 0, @now);
SET @orderId = SCOPE_IDENTITY();

INSERT INTO OrderLines
  (OrderId, RawDescription, ProductLine, ProductName, [Set], CollectorNumber, Rarity, Condition, Variant, Quantity)
VALUES
  (@orderId, 'Magic - Kaldheim: Goldspan Dragon - #139 - M - Near Mint',
   'Magic', 'Goldspan Dragon', 'Kaldheim', '#139', 'M', 'Near Mint', NULL, 1),
  (@orderId, 'Magic - Murders at Karlov Manor: Case of the Uneaten Feast - #10 - R - Near Mint',
   'Magic', 'Case of the Uneaten Feast', 'Murders at Karlov Manor', '#10', 'R', 'Near Mint', NULL, 1),
  (@orderId, 'Magic - The Brothers'' War: Mishra, Claimed by Gix - #216 - M - Near Mint Foil',
   'Magic', 'Mishra, Claimed by Gix', 'The Brothers'' War', '#216', 'M', 'Near Mint', 'Foil', 1),
  (@orderId, 'Magic - Kamigawa: Neon Dynasty: The Wandering Emperor - #42 - M - Near Mint',
   'Magic', 'The Wandering Emperor', 'Kamigawa: Neon Dynasty', '#42', 'M', 'Near Mint', NULL, 1),
  (@orderId, 'Magic - Outlaws of Thunder Junction: Slickshot Show-Off - #146 - R - Near Mint',
   'Magic', 'Slickshot Show-Off', 'Outlaws of Thunder Junction', '#146', 'R', 'Near Mint', NULL, 1),
  (@orderId, 'Magic - Strixhaven: School of Mages: Elite Spellbinder - #17 - R - Near Mint',
   'Magic', 'Elite Spellbinder', 'Strixhaven: School of Mages', '#17', 'R', 'Near Mint', NULL, 1),
  (@orderId, 'Magic - Ravnica Allegiance: Hydroid Krasis - #183 - M - Lightly Played',
   'Magic', 'Hydroid Krasis', 'Ravnica Allegiance', '#183', 'M', 'Lightly Played', NULL, 1),
  (@orderId, 'Magic - The Lost Caverns of Ixalan: Cavern of Souls - #269 - M - Near Mint',
   'Magic', 'Cavern of Souls', 'The Lost Caverns of Ixalan', '#269', 'M', 'Near Mint', NULL, 1);


-- ============================================================================
-- 5. Half already picked. Claim it and the picker resumes mid-order: one box
--    is finished, another still owes cards, and a third has not been started.
-- ============================================================================
INSERT INTO Orders (TcgplayerOrderId, Status, ImportedAt)
VALUES ('F8433182-TESTE5-RESUME', 0, @now);
SET @orderId = SCOPE_IDENTITY();

INSERT INTO OrderLines
  (OrderId, RawDescription, ProductLine, ProductName, [Set], CollectorNumber, Rarity, Condition, Variant, Quantity, PickOutcome, PickOutcomeRecordedAt)
VALUES
  -- SV05: Temporal Forces is finished.
  (@orderId, 'Pokemon - SV05: Temporal Forces: Torterra ex - 012/162 - #012/162 - Double Rare - Near Mint Holofoil',
   'Pokemon', 'Torterra ex', 'SV05: Temporal Forces', '#012/162', 'Double Rare', 'Near Mint', 'Holofoil', 1, 0, @now),
  (@orderId, 'Pokemon - SV05: Temporal Forces: Iron Leaves ex - 025/162 - #025/162 - Double Rare - Near Mint Holofoil',
   'Pokemon', 'Iron Leaves ex', 'SV05: Temporal Forces', '#025/162', 'Double Rare', 'Near Mint', 'Holofoil', 1, 0, @now),
  -- SWSH11: Lost Origin is half done - Magnezone is still outstanding, two copies owed.
  (@orderId, 'Pokemon - SWSH11: Lost Origin: Kyurem VMAX - 049/196 - #049/196 - Ultra Rare - Near Mint Holofoil',
   'Pokemon', 'Kyurem VMAX', 'SWSH11: Lost Origin', '#049/196', 'Ultra Rare', 'Near Mint', 'Holofoil', 2, 0, @now),
  (@orderId, 'Pokemon - SWSH11: Lost Origin: Magnezone VSTAR - 057/196 - #057/196 - Ultra Rare - Near Mint Holofoil',
   'Pokemon', 'Magnezone VSTAR', 'SWSH11: Lost Origin', '#057/196', 'Ultra Rare', 'Near Mint', 'Holofoil', 2, NULL, NULL),
  -- Celebrations has not been started.
  (@orderId, 'Pokemon - Celebrations: Pikachu - 5/25 - #5/25 - Rare - Near Mint Holofoil',
   'Pokemon', 'Pikachu', 'Celebrations', '#5/25', 'Rare', 'Near Mint', 'Holofoil', 2, NULL, NULL),
  (@orderId, 'Pokemon - Celebrations: Flying Pikachu VMAX - 7/25 - #7/25 - Ultra Rare - Near Mint Holofoil',
   'Pokemon', 'Flying Pikachu VMAX', 'Celebrations', '#7/25', 'Ultra Rare', 'Near Mint', 'Holofoil', 1, NULL, NULL);


-- ============================================================================
-- 6. NO IMAGES, ON PURPOSE. Every line here fails to resolve artwork, for a
--    different real reason, and every one must still be visible and pickable.
--    "No image is better than the wrong image" (PRD §17) is what this exercises.
--
--      * One Piece has no catalog provider at all (PRD §42, Priority 1), so
--        these are genuinely unresolvable rather than contrived.
--      * A card that does not exist: the set and number resolve, the name
--        check fails, and the app must show nothing rather than the card that
--        does live at that number.
--      * A line with no recorded set, which must keep its own labelled group
--        rather than being grouped out of existence (016 FR-005).
--      * A real set with an out-of-range collector number.
-- ============================================================================
INSERT INTO Orders (TcgplayerOrderId, Status, ImportedAt)
VALUES ('F8433182-TESTF6-NOIMAGES', 0, @now);
SET @orderId = SCOPE_IDENTITY();

INSERT INTO OrderLines
  (OrderId, RawDescription, ProductLine, ProductName, [Set], CollectorNumber, Rarity, Condition, Variant, Quantity)
VALUES
  (@orderId, 'One Piece Card Game - Romance Dawn: Monkey.D.Luffy - #OP01-003 - L - Near Mint Foil',
   'One Piece Card Game', 'Monkey.D.Luffy', 'Romance Dawn', '#OP01-003', 'L', 'Near Mint', 'Foil', 1),
  (@orderId, 'One Piece Card Game - Paramount War: Portgas.D.Ace - #OP02-013 - L - Near Mint Foil',
   'One Piece Card Game', 'Portgas.D.Ace', 'Paramount War', '#OP02-013', 'L', 'Near Mint', 'Foil', 2),
  (@orderId, 'Magic - Foundations: Nonexistent Test Card - #227 - R - Near Mint',
   'Magic', 'Nonexistent Test Card', 'Foundations', '#227', 'R', 'Near Mint', NULL, 1),
  (@orderId, 'Pokemon - SV01: Scarlet & Violet Base Set: Out Of Range Card - 999/198 - #999/198 - Rare - Heavily Played',
   'Pokemon', 'Out Of Range Card', 'SV01: Scarlet & Violet Base Set', '#999/198', 'Rare', 'Heavily Played', NULL, 1),
  -- No set recorded. Must remain visible, in its own labelled group, last within Pokemon.
  (@orderId, 'Pokemon - Mystery Promo - #UNKNOWN - Promo - Near Mint',
   'Pokemon', 'Mystery Promo', '', '#UNKNOWN', 'Promo', 'Near Mint', NULL, 1);


COMMIT TRANSACTION;

-- What was created. Id is included because re-running this script deletes and re-inserts, so
-- every order gets a NEW Id — a /orders/<id> link from a previous run will not point at the
-- same order afterwards. Use the Ids printed here.
SELECT
  o.Id,
  o.TcgplayerOrderId,
  COUNT(*)                                                    AS Products,
  SUM(ol.Quantity)                                            AS Cards,
  COUNT(DISTINCT ol.ProductLine)                              AS Games,
  COUNT(DISTINCT CONCAT(ol.ProductLine, '|', ol.[Set]))       AS Boxes,
  SUM(CASE WHEN ol.PickOutcome IS NOT NULL THEN 1 ELSE 0 END) AS AlreadyResolved
FROM Orders o
JOIN OrderLines ol ON ol.OrderId = o.Id
WHERE o.TcgplayerOrderId LIKE 'F8433182-TEST%'
GROUP BY o.Id, o.TcgplayerOrderId
ORDER BY o.Id;
