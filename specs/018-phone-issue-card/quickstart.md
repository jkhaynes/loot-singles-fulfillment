# Quickstart: Reported Issues on the Card and the List

Manual validation of the feature end to end. Each scenario names what it proves.

## Prerequisites

- The dev stack running (`scripts/start-dev.ps1`), with at least one imported order that has a
  product of **quantity 4 or more** and at least two other products.
- A phone, or a desktop browser at phone width (390 × 844), and a desktop browser.
- Two employee logins, for scenarios 5 and 8.

## Phone

### 1. A reported product stops offering to be picked (US1, FR-001–FR-007)

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

### 2. The sheet shows what was reported, for any issue type (US2, FR-009–FR-014)

1. On the reported product, tap the chip.
2. **Expected**: a sheet showing Card Not Found, **Required 4 · Found 3**, the note, and your name
   and the time. Nothing reads "pulled" or "short".
3. Tap **Close**. **Expected**: the sheet closes and the card is unchanged.
4. Report another product as **Damaged** with a note and **no counts**, then open its sheet.
   **Expected**: the problem, the note, and the reporter and time; no counts line.

### 3. Resolved (US3, FR-015)

1. On the quantity-4 reported product, open the sheet and tap **Resolved**.
2. **Expected**:
   - The sheet closes.
   - The card shows its picked look, and the dock shows **Picked ✓**.
   - On the final review, the product counts as pulled.

### 4. Edit the report (US3, FR-016, FR-017)

1. On a reported product, open the sheet and tap **Edit report**.
2. **Expected**: the issue form opens with the current type, counts and note already filled in.
3. Change the type to **Wrong Variant** and submit. **Expected**: the chip now reads **Wrong
   Variant**, and the sheet shows the new report.
4. Tap **Edit report** again, then **Cancel**. **Expected**: the report is unchanged.

### 5. Viewing without the claim (FR-018)

1. Log in as the second employee and open the same order on a phone without claiming it.
2. Go to a reported product. **Expected**:
   - The chip is there.
   - Its sheet offers **Close** only.
   - The dock shows what it shows today for an order you don't hold.

## Desktop

### 6. The issue panel (US4, FR-022, FR-024)

1. Open an order with a reported product on a desktop browser, holding the claim.
2. **Expected**:
   - The reported row shows an amber issue panel with the issue type, the reporter and time, and
     the counts and note when they were recorded.
   - The row offers **Resolved** and **Edit report**, and no **Picked** or **Report Issue**.
   - Rows without a report look and behave exactly as before.

### 7. Resolved and Edit report on the desktop (US4, FR-023)

1. On the reported row, tap **Edit report**. **Expected**: the issue form opens in the row with the
   current report filled in, and the panel's buttons are hidden while it's open.
2. Change the note and submit. **Expected**: the panel shows the new note.
3. Tap **Resolved**. **Expected**: the row returns to its picked state, with **Picked** pressed.

### 8. Viewing without the claim, on the desktop (FR-025)

1. As the second employee, open the same order on a desktop without claiming it.
2. **Expected**: the reported row shows the issue panel with no **Resolved** or **Edit report**.

## Unchanged

### 9. Review, endings and labels (FR-021)

1. Finish an order with a reported product on the phone. **Expected**: the review and the ending
   screen behave as in feature 017. A reported product still ends the pick held, with a hold label.

## Automated

```sh
npm --prefix frontend test
npm --prefix frontend run build
npm --prefix frontend run lint
npx --prefix frontend playwright test
```
