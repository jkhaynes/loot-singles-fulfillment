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
-- Order ids keep the shape of a real TCGplayer id (they have been picked so a glance at the
-- dashboard still reads as an order number) while naming what each one is for.
--
-- Card names, sets and rarities are real. Collector numbers are accurate where known and
-- plausible otherwise - a wrong number means the catalog lookup fails to confirm the card and
-- the app shows no image, which is the intended safe behaviour (PRD §17), not a broken order.
--
-- Field formats mirror what the packing-slip parser actually produces, sampled from imported
-- orders: Magic rarities are single letters, Pokemon rarities are words, One Piece collector
-- numbers look like #OP01-003, Lorcana product names carry their subtitle.

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
-- 1. All four games, two sets each.
--    Games group and sort alphabetically: Lorcana TCG, Magic, One Piece Card
--    Game, Pokemon. Crossing between them is the expensive walk.
-- ============================================================================
INSERT INTO Orders (TcgplayerOrderId, Status, ImportedAt)
VALUES ('F8433182-TESTA1-4GAMES', 0, @now);
SET @orderId = SCOPE_IDENTITY();

INSERT INTO OrderLines
  (OrderId, RawDescription, ProductLine, ProductName, [Set], CollectorNumber, Rarity, Condition, Variant, Quantity)
VALUES
  -- Deliberately listed out of grouping order so the screen has real work to do.
  (@orderId, 'Pokemon - SV07: Stellar Crown: Terapagos ex - 128/142 - #128/142 - Double Rare - Near Mint Holofoil',
   'Pokemon', 'Terapagos ex', 'SV07: Stellar Crown', '#128/142', 'Double Rare', 'Near Mint', 'Holofoil', 1),
  (@orderId, 'Magic - Dominaria United: Sheoldred, the Apocalypse - #107 - M - Near Mint',
   'Magic', 'Sheoldred, the Apocalypse', 'Dominaria United', '#107', 'M', 'Near Mint', NULL, 1),
  (@orderId, 'Lorcana TCG - The First Chapter - Elsa - Snow Queen - #41 - Rare - Near Mint Holofoil',
   'Lorcana TCG', 'Elsa - Snow Queen', 'The First Chapter', '#41', 'Rare', 'Near Mint', 'Holofoil', 1),
  (@orderId, 'One Piece Card Game - Romance Dawn: Monkey.D.Luffy - #OP01-003 - L - Near Mint Foil',
   'One Piece Card Game', 'Monkey.D.Luffy', 'Romance Dawn', '#OP01-003', 'L', 'Near Mint', 'Foil', 1),
  (@orderId, 'Pokemon - SWSH07: Evolving Skies: Umbreon VMAX - 095/203 - #095/203 - Ultra Rare - Lightly Played Holofoil',
   'Pokemon', 'Umbreon VMAX', 'SWSH07: Evolving Skies', '#095/203', 'Ultra Rare', 'Lightly Played', 'Holofoil', 1),
  (@orderId, 'Magic - Bloomburrow: Mabel, Heir to Cragflame - #213 - M - Near Mint Foil',
   'Magic', 'Mabel, Heir to Cragflame', 'Bloomburrow', '#213', 'M', 'Near Mint', 'Foil', 1),
  (@orderId, 'One Piece Card Game - Paramount War: Portgas.D.Ace - #OP02-013 - L - Near Mint Foil',
   'One Piece Card Game', 'Portgas.D.Ace', 'Paramount War', '#OP02-013', 'L', 'Near Mint', 'Foil', 1),
  (@orderId, 'Lorcana TCG - Rise of the Floodborn - Belle - Strange but Special - #6 - Legendary - Near Mint Holofoil',
   'Lorcana TCG', 'Belle - Strange but Special', 'Rise of the Floodborn', '#6', 'Legendary', 'Near Mint', 'Holofoil', 1);


-- ============================================================================
-- 2. Quantity emphasis. Several lines ask for more than one physical card, so
--    product count and card count diverge sharply: 6 products, 20 cards.
--    A missed multiple is the costliest picking error (PRD §5.3, §15).
-- ============================================================================
INSERT INTO Orders (TcgplayerOrderId, Status, ImportedAt)
VALUES ('F8433182-TESTB2-BIGQTY', 0, @now);
SET @orderId = SCOPE_IDENTITY();

INSERT INTO OrderLines
  (OrderId, RawDescription, ProductLine, ProductName, [Set], CollectorNumber, Rarity, Condition, Variant, Quantity)
VALUES
  (@orderId, 'Magic - Foundations: Llanowar Elves - #197 - C - Near Mint',
   'Magic', 'Llanowar Elves', 'Foundations', '#197', 'C', 'Near Mint', NULL, 9),
  (@orderId, 'Magic - Foundations: Lightning Bolt - #143 - U - Near Mint',
   'Magic', 'Lightning Bolt', 'Foundations', '#143', 'U', 'Near Mint', NULL, 4),
  (@orderId, 'Pokemon - SV03: Obsidian Flames: Charizard ex - 125/197 - #125/197 - Double Rare - Near Mint Holofoil',
   'Pokemon', 'Charizard ex', 'SV03: Obsidian Flames', '#125/197', 'Double Rare', 'Near Mint', 'Holofoil', 3),
  (@orderId, 'One Piece Card Game - Romance Dawn: Roronoa Zoro - #OP01-025 - SR - Near Mint Foil',
   'One Piece Card Game', 'Roronoa Zoro', 'Romance Dawn', '#OP01-025', 'SR', 'Near Mint', 'Foil', 2),
  (@orderId, 'Lorcana TCG - The First Chapter - Mickey Mouse - Brave Little Tailor - #115 - Legendary - Near Mint Holofoil',
   'Lorcana TCG', 'Mickey Mouse - Brave Little Tailor', 'The First Chapter', '#115', 'Legendary', 'Near Mint', 'Holofoil', 1),
  (@orderId, 'Magic - Bloomburrow: Valley Floodcaller - #75 - R - Near Mint',
   'Magic', 'Valley Floodcaller', 'Bloomburrow', '#75', 'R', 'Near Mint', NULL, 1);


-- ============================================================================
-- 3. One game, one set. No transitions at all - the picker opens one box and
--    never leaves it. The case that must not show empty or redundant grouping.
-- ============================================================================
INSERT INTO Orders (TcgplayerOrderId, Status, ImportedAt)
VALUES ('F8433182-TESTC3-ONEBOX', 0, @now);
SET @orderId = SCOPE_IDENTITY();

INSERT INTO OrderLines
  (OrderId, RawDescription, ProductLine, ProductName, [Set], CollectorNumber, Rarity, Condition, Variant, Quantity)
VALUES
  (@orderId, 'Pokemon - SV03: Obsidian Flames: Pidgeot ex - 164/197 - #164/197 - Ultra Rare - Near Mint Holofoil',
   'Pokemon', 'Pidgeot ex', 'SV03: Obsidian Flames', '#164/197', 'Ultra Rare', 'Near Mint', 'Holofoil', 1),
  (@orderId, 'Pokemon - SV03: Obsidian Flames: Tyranitar ex - 136/197 - #136/197 - Double Rare - Near Mint Holofoil',
   'Pokemon', 'Tyranitar ex', 'SV03: Obsidian Flames', '#136/197', 'Double Rare', 'Near Mint', 'Holofoil', 2),
  (@orderId, 'Pokemon - SV03: Obsidian Flames: Victini - 023/197 - #023/197 - Illustration Rare - Near Mint Holofoil',
   'Pokemon', 'Victini', 'SV03: Obsidian Flames', '#023/197', 'Illustration Rare', 'Near Mint', 'Holofoil', 1),
  (@orderId, 'Pokemon - SV03: Obsidian Flames: Ninetales - 029/197 - #029/197 - Rare - Moderately Played',
   'Pokemon', 'Ninetales', 'SV03: Obsidian Flames', '#029/197', 'Rare', 'Moderately Played', NULL, 1),
  (@orderId, 'Pokemon - SV03: Obsidian Flames: Professor Sada''s Vitality - 170/197 - #170/197 - Ultra Rare - Near Mint Holofoil',
   'Pokemon', 'Professor Sada''s Vitality', 'SV03: Obsidian Flames', '#170/197', 'Ultra Rare', 'Near Mint', 'Holofoil', 1);


-- ============================================================================
-- 4. Many boxes, one card in each. The opposite extreme: eight sets, eight
--    single cards, so the picker crosses a box boundary on every single step.
--    Grouping saves nothing here - this is the order that shows what ordering
--    and the transition screens are worth on their own.
-- ============================================================================
INSERT INTO Orders (TcgplayerOrderId, Status, ImportedAt)
VALUES ('F8433182-TESTD4-MANYBOX', 0, @now);
SET @orderId = SCOPE_IDENTITY();

INSERT INTO OrderLines
  (OrderId, RawDescription, ProductLine, ProductName, [Set], CollectorNumber, Rarity, Condition, Variant, Quantity)
VALUES
  (@orderId, 'Magic - Kaldheim: Goldspan Dragon - #139 - M - Near Mint',
   'Magic', 'Goldspan Dragon', 'Kaldheim', '#139', 'M', 'Near Mint', NULL, 1),
  (@orderId, 'Magic - Murders at Karlov Manor: Case of the Uneaten Feast - #7 - R - Near Mint',
   'Magic', 'Case of the Uneaten Feast', 'Murders at Karlov Manor', '#7', 'R', 'Near Mint', NULL, 1),
  (@orderId, 'Magic - The Brothers'' War: Urza, Chief Artificer - #250 - M - Near Mint Foil',
   'Magic', 'Urza, Chief Artificer', 'The Brothers'' War', '#250', 'M', 'Near Mint', 'Foil', 1),
  (@orderId, 'Magic - Kamigawa: Neon Dynasty: The Wandering Emperor - #42 - M - Near Mint',
   'Magic', 'The Wandering Emperor', 'Kamigawa: Neon Dynasty', '#42', 'M', 'Near Mint', NULL, 1),
  (@orderId, 'Magic - Outlaws of Thunder Junction: Slickshot Show-Off - #137 - R - Near Mint',
   'Magic', 'Slickshot Show-Off', 'Outlaws of Thunder Junction', '#137', 'R', 'Near Mint', NULL, 1),
  (@orderId, 'Magic - Strixhaven: School of Mages: Elite Spellbinder - #7 - R - Near Mint',
   'Magic', 'Elite Spellbinder', 'Strixhaven: School of Mages', '#7', 'R', 'Near Mint', NULL, 1),
  (@orderId, 'Magic - Ravnica Allegiance: Hydroid Krasis - #183 - M - Lightly Played',
   'Magic', 'Hydroid Krasis', 'Ravnica Allegiance', '#183', 'M', 'Lightly Played', NULL, 1),
  (@orderId, 'Magic - The Lost Caverns of Ixalan: Cavern of Souls - #269 - M - Near Mint',
   'Magic', 'Cavern of Souls', 'The Lost Caverns of Ixalan', '#269', 'M', 'Near Mint', NULL, 1);


-- ============================================================================
-- 5. Half already picked. Claim it and the picker resumes mid-order: progress
--    shows 3 of 6 products and 4 of 9 cards, and one box is already finished
--    while another still owes cards - which is what trips the unfinished-box
--    guard on the way out.
-- ============================================================================
INSERT INTO Orders (TcgplayerOrderId, Status, ImportedAt)
VALUES ('F8433182-TESTE5-RESUME', 0, @now);
SET @orderId = SCOPE_IDENTITY();

INSERT INTO OrderLines
  (OrderId, RawDescription, ProductLine, ProductName, [Set], CollectorNumber, Rarity, Condition, Variant, Quantity, PickOutcome, PickOutcomeRecordedAt)
VALUES
  -- SV05: Temporal Forces is finished.
  (@orderId, 'Pokemon - SV05: Temporal Forces: Iron Crown ex - 081/162 - #081/162 - Double Rare - Near Mint Holofoil',
   'Pokemon', 'Iron Crown ex', 'SV05: Temporal Forces', '#081/162', 'Double Rare', 'Near Mint', 'Holofoil', 1, 0, @now),
  (@orderId, 'Pokemon - SV05: Temporal Forces: Walking Wake ex - 050/162 - #050/162 - Double Rare - Near Mint Holofoil',
   'Pokemon', 'Walking Wake ex', 'SV05: Temporal Forces', '#050/162', 'Double Rare', 'Near Mint', 'Holofoil', 1, 0, @now),
  -- SWSH11: Lost Origin is half done - Giratina is still outstanding, with two copies owed.
  (@orderId, 'Pokemon - SWSH11: Lost Origin: Aerodactyl VSTAR - 093/196 - #093/196 - Ultra Rare - Near Mint Holofoil',
   'Pokemon', 'Aerodactyl VSTAR', 'SWSH11: Lost Origin', '#093/196', 'Ultra Rare', 'Near Mint', 'Holofoil', 2, 0, @now),
  (@orderId, 'Pokemon - SWSH11: Lost Origin: Giratina VSTAR - 131/196 - #131/196 - Ultra Rare - Near Mint Holofoil',
   'Pokemon', 'Giratina VSTAR', 'SWSH11: Lost Origin', '#131/196', 'Ultra Rare', 'Near Mint', 'Holofoil', 2, NULL, NULL),
  -- Celebrations has not been started.
  (@orderId, 'Pokemon - Celebrations: Mew - 011/025 - #011/025 - Rare - Near Mint Holofoil',
   'Pokemon', 'Mew', 'Celebrations', '#011/025', 'Rare', 'Near Mint', 'Holofoil', 2, NULL, NULL),
  (@orderId, 'Pokemon - Celebrations: Zekrom - 013/025 - #013/025 - Rare - Near Mint Holofoil',
   'Pokemon', 'Zekrom', 'Celebrations', '#013/025', 'Rare', 'Near Mint', 'Holofoil', 1, NULL, NULL);


-- ============================================================================
-- 6. Awkward data, on purpose. Every line here must still be visible and
--    pickable - losing one would hide work the picker has to do.
--
--      * a line with no recorded set, which must get its own labelled group
--        sorted last within its game rather than being grouped out of
--        existence (016 FR-005, Constitution V)
--      * a card that does not exist, so no catalog lookup can confirm it and
--        the app must show no image rather than a plausible wrong one
--        (PRD §17: no image is better than the wrong image)
--      * a heavily played card and an unusual variant, so condition and
--        variant are not always the comfortable default
-- ============================================================================
INSERT INTO Orders (TcgplayerOrderId, Status, ImportedAt)
VALUES ('F8433182-TESTF6-ODDDATA', 0, @now);
SET @orderId = SCOPE_IDENTITY();

INSERT INTO OrderLines
  (OrderId, RawDescription, ProductLine, ProductName, [Set], CollectorNumber, Rarity, Condition, Variant, Quantity)
VALUES
  (@orderId, 'Pokemon - SV01: Scarlet & Violet Base Set: Miraidon ex - 081/198 - #081/198 - Double Rare - Near Mint Holofoil',
   'Pokemon', 'Miraidon ex', 'SV01: Scarlet & Violet Base Set', '#081/198', 'Double Rare', 'Near Mint', 'Holofoil', 1),
  -- No set recorded. Must remain visible, in its own labelled group, last within Pokemon.
  (@orderId, 'Pokemon - Pikachu - #UNKNOWN - Promo - Near Mint',
   'Pokemon', 'Pikachu', '', '#UNKNOWN', 'Promo', 'Near Mint', NULL, 1),
  -- Not a real card: the catalog cannot confirm it, so no image should appear.
  (@orderId, 'Magic - Foundations: Nonexistent Test Card - #999 - R - Near Mint',
   'Magic', 'Nonexistent Test Card', 'Foundations', '#999', 'R', 'Near Mint', NULL, 1),
  (@orderId, 'Magic - Commander Legends: Jeweled Lotus - #319 - M - Heavily Played Foil',
   'Magic', 'Jeweled Lotus', 'Commander Legends', '#319', 'M', 'Heavily Played', 'Foil', 1),
  (@orderId, 'Magic - Innistrad Remastered: Emrakul, the Promised End (Retro Frame) - #330 - M - Near Mint',
   'Magic', 'Emrakul, the Promised End (Retro Frame)', 'Innistrad Remastered', '#330', 'M', 'Near Mint', '(Retro Frame)', 1);


COMMIT TRANSACTION;

-- What was created.
SELECT
  o.TcgplayerOrderId,
  COUNT(*)                                            AS Products,
  SUM(ol.Quantity)                                    AS Cards,
  COUNT(DISTINCT ol.ProductLine)                      AS Games,
  COUNT(DISTINCT CONCAT(ol.ProductLine, '|', ol.[Set])) AS Boxes,
  SUM(CASE WHEN ol.PickOutcome IS NOT NULL THEN 1 ELSE 0 END) AS AlreadyResolved
FROM Orders o
JOIN OrderLines ol ON ol.OrderId = o.Id
WHERE o.TcgplayerOrderId LIKE 'F8433182-TEST%'
GROUP BY o.TcgplayerOrderId
ORDER BY o.TcgplayerOrderId;
