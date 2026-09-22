# Quickstart: Reported Issues on the Phone's Card

Manual validation of the feature end to end. Each scenario names what it proves.

## Prerequisites

- The dev stack running (`scripts/start-dev.ps1`), with at least one imported order that has a
  product of **quantity 4 or more** and another of **quantity 1**.
- A phone, or a desktop browser at phone width (390 × 844).
- Two employee logins, for scenario 5.

## 1. A reported product stops offering to be picked (US1, FR-001–FR-007)

1. Claim the order and open it on the phone. Go to the quantity-4 product.
2. Tap **Report an issue**. Choose **Card Not Found**, required **4**, found **3**, note "Only 3 in
   the binder slot". Tap **Submit Issue**.
3. **Expected**:
   - The card shows an amber chip reading **Card Not Found**.
   - The dock shows **Next card ›** and the ‹ › arrows, and nothing else.
   - Nothing on the screen reads "Pulled all 4" or "Report an issue".
4. Tap **Next card ›**. **Expected**: the next product opens, and nothing about the reported product
   has changed.
5. Tap ‹ to go back. **Expected**: the chip and the issue dock are still there.

## 2. The sheet shows what was reported (US2, FR-009–FR-014)

1. On the reported product, tap the chip.
2. **Expected**: a sheet opens showing:
   - Card Not Found;
   - 3 of 4 pulled, 1 short;
   - the note;
   - your name and the time.
3. Tap **Close**. **Expected**: the sheet closes and the card is unchanged.
4. Report a different product with **no counts and no note**, then open its sheet. **Expected**: no
   quantity line and no note line; the problem, reporter and time only.

## 3. Found it after all (US3, FR-015)

1. On the quantity-4 reported product, open the sheet and tap **I found all 4**.
2. **Expected**:
   - The sheet closes.
   - The card shows its picked look, and the dock shows **Picked ✓**.
   - On the final review, the product counts as pulled.
3. Report the quantity-1 product, then open its sheet. **Expected**: the found action reads **I found
   it**.

## 4. Change the report (US3, FR-016, FR-017)

1. On a reported product, open the sheet and tap **Change report**.
2. **Expected**: the issue form opens with the current type, counts and note already filled in.
3. Change the type to **Wrong Variant** and submit. **Expected**: the chip now reads **Wrong
   Variant**, and the sheet shows the new report.
4. Tap **Change report** again, then **Cancel**. **Expected**: the report is unchanged.

## 5. Viewing without the claim (edge case, FR-018)

1. Log in as the second employee and open the same order without claiming it.
2. Go to a reported product. **Expected**:
   - The chip is there.
   - Tapping it opens the sheet with **Close** only.
   - The dock shows what it shows today for an order you don't hold.

## 6. Unchanged elsewhere (FR-021)

1. Open the same order on a desktop browser. **Expected**: the list view shows the report exactly as
   it did before this feature.
2. Finish the order on the phone. **Expected**: the review and the ending screen behave as in
   feature 017. A reported product still ends the pick held, with a hold label.

## Automated

```sh
npm --prefix frontend test
npm --prefix frontend run build
npm --prefix frontend run lint
npx --prefix frontend playwright test
```
