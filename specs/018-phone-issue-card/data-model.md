# Data Model: Reported Issues on the Phone's Card

**No data model changes.** This feature displays data the application already records and reuses
the existing ways to change it (research.md §1).

## Read: the current report on a line

`OrderLineDetail.currentIssue` (`PickingIssueDetail`), delivered with every order detail:

| Field | Shown in | Rule |
|---|---|---|
| `issueType` | Chip and sheet | Always shown, by its existing label (FR-002, FR-010) |
| `requiredQuantity`, `foundQuantity` | Sheet | "pulled F of R · R − F short", only when **both** are present (FR-011; research.md §5) |
| `note` | Sheet | Only when present (FR-012) |
| `reportedByEmployeeName` | Sheet | With the time; the time alone when the name is null (FR-013) |
| `reportedAt` | Sheet | Always |

A line is in the **reported** state when `pickOutcome === 'hasIssue'`. That state alone decides the
chip, the issue dock and whether the sheet can show.

## Write: the two corrections (existing calls)

| Action | Call | Effect (feature 015) |
|---|---|---|
| **I found all N** / **I found it** | `recordPicked(orderId, lineId)` | The line becomes `picked`, and the report is superseded but kept |
| **Change report** → submit | `reportIssue(orderId, lineId, request)` | A new report becomes current, and the old one is superseded but kept |

## Card states on the phone

| Line state | Card | Dock (picker can record) | Dock (picker cannot record) |
|---|---|---|---|
| No outcome | Unchanged | Picked / Pulled all N, Report an issue, ‹ › | Unchanged (Claim, or the reason) |
| Picked | Unchanged (picked look) | Picked ✓, Report an issue, ‹ › | Unchanged |
| **Reported** | **+ chip** | **Next card ›, ‹ ›** | Unchanged (Claim, or the reason) |

The sheet opens only from the chip, in any state where the chip shows. Its correction actions
appear only when the picker can record (FR-018).
