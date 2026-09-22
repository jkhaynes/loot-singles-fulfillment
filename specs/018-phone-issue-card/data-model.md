# Data Model: Reported Issues on the Card and the List

**No data model changes.** This feature displays data the application already records, on the
phone's card and the desktop list, and reuses the existing ways to change it (research.md §1).

## Read: the current report on a line

`OrderLineDetail.currentIssue` (`PickingIssueDetail`), delivered with every order detail. The same
rules apply on both views, and one component renders them (research.md §8):

| Field | Shown in | Rule |
|---|---|---|
| `issueType` | Phone chip, sheet, desktop panel | Always shown, by its existing label (FR-002, FR-010) |
| `requiredQuantity`, `foundQuantity` | Sheet, desktop panel | "Required R · Found F", only when **both** are present. No difference is computed, and nothing reads "pulled" or "short" (FR-011; research.md §5) |
| `note` | Sheet, desktop panel | Only when present (FR-012) |
| `reportedByEmployeeName` | Sheet, desktop panel | With the time; the time alone when the name is null (FR-013) |
| `reportedAt` | Sheet, desktop panel | Always |

A line is in the **reported** state when `pickOutcome === 'hasIssue'`. That state alone decides the
chip and issue dock on the phone, and the issue panel on the desktop.

## Write: the two corrections (existing calls)

| Action | Call | Effect (feature 015) |
|---|---|---|
| **Resolved** | `recordPicked(orderId, lineId)` | The line becomes `picked`, and the report is superseded but kept |
| **Edit report** → submit | `reportIssue(orderId, lineId, request)` | A new report becomes current, and the old one is superseded but kept |

## Phone card states

| Line state | Card | Dock (picker can record) | Dock (picker cannot record) |
|---|---|---|---|
| No outcome | Unchanged | Picked / Pulled all N, Report an issue, ‹ › | Unchanged (Claim, or the reason) |
| Picked | Unchanged (picked look) | Picked ✓, Report an issue, ‹ › | Unchanged |
| **Reported** | **+ chip** | **Next card ›, ‹ ›** | Unchanged (Claim, or the reason) |

The sheet opens only from the chip. Its **Resolved** and **Edit report** appear only when the picker
can record (FR-018).

## Desktop row states

| Line state | Row (viewer can record) | Row (viewer cannot record) |
|---|---|---|
| No outcome | Unchanged: Picked, Report Issue | Unchanged: no actions |
| Picked | Unchanged: Picked (pressed), Report Issue | Unchanged: no actions |
| **Reported** | **Issue panel with Resolved and Edit report; no Picked, no Report Issue** | **Issue panel, no actions** |
| **Reported, editing** | **The issue form in the row, pre-filled; the panel's actions hidden** | n/a |
