# UI Contract: The Reported Product on the Phone's Card

This feature adds no API. Its interface is what the picker sees and taps, and this is the contract
tests assert against. Names are accessible names (role + name), which is how the RTL and Playwright
suites locate controls.

## The chip

- `button`, named by the issue type label, e.g. **Card Not Found** (FR-001, FR-002).
- Present only when the line is reported. Present whether or not the picker can record.
- Activating it opens the sheet. Nothing else opens the sheet (FR-009).

## The dock, reported line, picker can record

| Control | Role and name | Behaviour |
|---|---|---|
| Primary | `button` **Next card ›** | Same as the › arrow: next product, or the review from the last (FR-007) |
| Previous | `button` **Previous card** | Unchanged |
| Next | `button` **Next card** | Unchanged |

Absent: any `button` whose name starts with **Picked** or **Pulled all**, and **Report an issue**
(FR-006).

> The primary and the › arrow both match `/next card/i`. Tests must use exact names
> (research.md §6).

## The sheet

- `dialog` named **Reported issue**.
- Content, in order:
  - the issue type label;
  - when both counts exist, "F of R pulled" and "R − F short";
  - when present, the note;
  - the reporter and time, or the time alone.
- Actions:

| Control | Shown when | Behaviour |
|---|---|---|
| `button` **I found all N** / **I found it** | Picker can record | Records the line as picked. Disabled while recording. The sheet leaves when the line is no longer reported (FR-015, FR-020) |
| `button` **Change report** | Picker can record | Closes the sheet and opens the issue form pre-filled from the current report (FR-016) |
| `button` **Close** | Always | Closes the sheet and changes nothing (FR-014) |

## The issue form, from Change report

- The same form as **Report an issue**, with every field starting from the current report.
- **Submit Issue** records a new report, which becomes current. **Cancel** leaves the report
  unchanged (FR-017).

## Unchanged

The desktop order view, the final review, the ending screens and labels, and the dock for lines
with no outcome or a picked outcome (FR-008, FR-021).
