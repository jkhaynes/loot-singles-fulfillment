# UI Contract: A Reported Product, on the Phone's Card and the Desktop List

This feature adds no API. Its interface is what the picker sees and taps, and this is the contract
tests assert against. Names are accessible names (role + name), which is how the RTL and Playwright
suites locate controls.

## Shared: a report's details

Rendered by one component on both views (research.md §8), in this order:

1. the issue type label;
2. "Required R · Found F", only when both counts exist;
3. the note, only when present;
4. the reporter and time, or the time alone.

Nothing reads "pulled" or "short", for any issue type (FR-011).

## Phone

### The chip

- `button`, named by the issue type label, e.g. **Card Not Found**, **Damaged** (FR-001, FR-002).
- Present only when the line is reported, whether or not the picker can record.
- Activating it opens the sheet. Nothing else opens the sheet (FR-009).

### The dock, reported line, picker can record

| Control | Role and name | Behaviour |
|---|---|---|
| Primary | `button` **Next card ›** | Same as the › arrow: next product, or the review from the last (FR-007) |
| Previous | `button` **Previous card** | Unchanged |
| Next | `button` **Next card** | Unchanged |

Absent: any `button` whose name starts with **Picked** or **Pulled all**, and **Report an issue**
(FR-006).

### The sheet

- `dialog` named **Reported issue**, holding the shared details.
- Actions:

| Control | Shown when | Behaviour |
|---|---|---|
| `button` **Resolved** | Picker can record | Records the line as picked. Disabled while recording. The sheet leaves when the line is no longer reported (FR-015, FR-020) |
| `button` **Edit report** | Picker can record | Closes the sheet and opens the issue form, pre-filled from the current report (FR-016) |
| `button` **Close** | Always | Closes the sheet and changes nothing (FR-014) |

## Desktop

### The issue panel, in a reported row

- A region of the row (`role="status"`, as the one-line summary it replaces), holding the shared
  details (FR-022).
- Actions, when the viewer can record (FR-023):

| Control | Behaviour |
|---|---|
| `button` **Resolved** | Records the line as picked. Disabled while recording (FR-026) |
| `button` **Edit report** | Opens the issue form in the row, pre-filled from the current report. The panel's actions hide while it is open |

- When the viewer cannot record: the panel with no actions (FR-025).
- Absent on a reported row: `button` **Picked** and **Report Issue** (FR-024).

## The issue form, from Edit report (both views)

- The same form as a first report, with every field starting from the current report.
- **Submit Issue** records a new report, which becomes current. **Cancel** leaves the report
  unchanged (FR-017).

## Unchanged

The final review, the ending screens and labels, and rows or cards with no outcome or a picked
outcome (FR-008, FR-021, FR-027).
