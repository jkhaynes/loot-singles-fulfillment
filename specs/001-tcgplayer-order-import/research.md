# Phase 0 Research: TCGplayer Packing Slip Order Import

## 1. PDF text-extraction library

**Decision**: PdfPig (`UglyToad.PdfPig`)

**Rationale**: Pure managed .NET library (no native/unmanaged dependency to deploy on Azure Container Apps), MIT-licensed (no cost or licensing risk for a low-cost internal tool, Constitution XI), actively maintained, and exposes per-page text with word/line positioning — needed to reliably detect page boundaries (one order per page, per the real example file) and to parse the line-item table's column structure (Quantity | Description | Price | Total Price) rather than treating the page as one flat text blob.

**Alternatives considered**:
- **iText7** — strong extraction quality, but its free tier is AGPL, which requires either open-sourcing this proprietary internal tool or purchasing a commercial license. Rejected on licensing/cost grounds (Constitution XI: prioritize low, predictable cost).
- **PDFsharp / MigraDoc** — primarily a PDF *creation* library; text extraction support is weaker and less actively developed for this use case. Rejected.
- **Shelling out to a native tool (e.g., `pdftotext` from poppler-utils)** — works, but adds a native binary dependency to the container image and an external-process failure mode for no real benefit over a managed library. Rejected.

## 2. Detecting order boundaries and the batch summary page

**Decision**: One page = one order (confirmed against a real example file: `Order Number: <id>` appears once per page, 13 times across 13 pages). The final page is a summary/index: a comma/pipe-separated list of every order identifier in the batch, used for the FR-013 cross-check.

**Rationale**: This matches the real example's structure exactly (see spec Clarifications, session 2026-08-20) and gives the parser an unambiguous per-order split point — extract per-page text via PdfPig, locate the `Order Number:` line as the anchor, and treat everything else on that page (Ship To block, line-item table, footer boilerplate) as belonging to that one order. The last page is detected by the absence of an `Order Number:` anchor and the presence of only order-identifier-shaped tokens, and is parsed separately as the batch manifest for FR-013.

**Alternatives considered**:
- **Splitting on a different anchor (e.g., blank lines or "Total" rows)** — less reliable, since a single order can have many line-item rows and the layout varies by line count. Rejected in favor of the stable `Order Number:` anchor.

## 3. Condition/variant disambiguation (FR-018)

**Decision**: Parse the trailing condition segment of each description with a fixed set of known TCGplayer condition prefixes (`Near Mint`, `Lightly Played`, `Moderately Played`, `Heavily Played`, `Damaged`, and any other condition values observed in real data — final list confirmed against fixtures during implementation) — everything in that segment *after* the matched condition prefix is variant/printing text (e.g., `"Near Mint Holofoil"` → prefix `"Near Mint"` matched, remainder `"Holofoil"` is variant). Parenthetical markers elsewhere in the product name (e.g., `"(Showcase)"`, `"(Surge Foil)"`) are extracted independently and concatenated with any condition-derived variant text, never overwriting it.

**Rationale**: A fixed condition-prefix list is far more reliable than trying to guess where "condition" ends and "variant" begins from formatting alone, and TCGplayer's condition vocabulary is small and stable. If a condition segment doesn't match a known prefix, that Order Line fails validation under `MissingCondition` (FR-005/FR-006) rather than guessing — consistent with the project's safe-failure principle (Constitution V).

**Alternatives considered**:
- **Regex-only heuristic splitting on capitalization/known foil keywords** — more fragile across games (Lorcana/One Piece condition vocabulary not yet confirmed against real fixtures); rejected in favor of an explicit, testable, extensible prefix list.

## 4. Synchronous processing with observable progress (FR-014)

**Decision**: `IPackingSlipImportService` exposes an `IAsyncEnumerable<ImportProgressUpdate>`-shaped method that the caller awaits to completion; each yielded update carries the running counters (orders detected/processed/succeeded/failed) FR-014 requires, and the final state is available via the completed `ImportAttempt`.

**Rationale**: `IAsyncEnumerable<T>` is a standard .NET mechanism for "the caller awaits one call to completion, but observes incremental state along the way" — it satisfies FR-014's synchronous requirement (the call isn't "done" until the enumeration completes) while giving a future caller (an import UI) a natural way to surface live progress, without this feature needing to build any UI itself or introduce a background job/queue.

**Alternatives considered**:
- **Fire-and-forget background job + polling endpoint** — this is the asynchronous/background model the spec's clarification session explicitly rejected in favor of synchronous processing at the observed ~200-order scale. Rejected.
- **A synchronous method with no progress signal, polled via a separate status query** — works, but requires a second call/entity lifecycle for something `IAsyncEnumerable` gives for free within one call. Rejected as unnecessary complexity.

## 5. Per-order atomic persistence (FR-016)

**Decision**: Each order's `Order` + all of its `OrderLine`s are added to a dedicated EF Core `DbContext` instance (or an explicit transaction scope) and saved with a single `SaveChangesAsync()` call per order. A failure during that call rolls back only that order's data; the import loop catches the failure, records an `ImportOrderResult` for that order, and continues to the next order.

**Rationale**: `SaveChangesAsync()` wraps its changes in an implicit database transaction — using one such call per order (rather than one call for the whole batch) gives exactly the atomicity boundary FR-016 requires (all-or-nothing per order) while keeping orders independent of each other, matching FR-005's partial-import requirement.

**Alternatives considered**:
- **One `SaveChangesAsync()` for the entire batch** — would make the whole batch atomic (contradicting FR-005's partial-import requirement: a single order's persistence failure would roll back every other successfully-parsed order in the same file). Rejected.

## 6. Duplicate-order race safety (FR-008)

**Decision**: A unique index on `Order.TcgplayerOrderId` at the database level (EF Core `HasIndex(...).IsUnique()`), plus an application-level pre-check (`AnyAsync` by order id) before attempting to create a new `Order`, for a fast, friendly rejection in the common case. The rare race (two near-simultaneous imports of the same order both passing the pre-check) is caught as a unique-constraint violation (`DbUpdateException`) on `SaveChangesAsync()` and translated into the same `DuplicateOrder` outcome as the pre-check.

**Rationale**: This is exactly the combination the spec's clarification session settled on: friendly UX for the normal case, hard guarantee for the race, at the cost of one small schema decision (Constitution VI: critical business rules enforced server-side/at the data layer, not just in application code).

**Alternatives considered**:
- **Application-level check only (no unique constraint)** — this was the spec's *original* answer before it was revised; rejected in the revision because it leaves a real, if rare, path to duplicate Orders that would create downstream ambiguity for picking and audit history.

## 7. Zero retention of the source PDF (FR-019)

**Decision**: The import service accepts the PDF as an in-memory `Stream`/byte buffer, passed directly to the parser; it is never written to blob storage, disk, or any other persistent store by this feature. If the hosting/upload layer (a future feature) needs a temp file on disk to hand the stream to this service, that temp file's lifecycle is that caller's responsibility and must not outlive the synchronous call — but this feature's own code path never creates a durable copy.

**Rationale**: The simplest way to guarantee "never retained" is to never write it anywhere durable in the first place, rather than writing it and then remembering to delete it (which is one missed code path away from a real PII leak). This directly satisfies FR-019 and the constitution's data-minimization principle (VII).

**Alternatives considered**:
- **Store the PDF temporarily, delete after processing** — rejected as strictly riskier (a crash mid-processing could skip the delete step) for no benefit, since nothing in the spec requires the file to be inspectable after the fact (FR-017 already preserves the per-line raw description text, which is what's needed for later dispute/reparse — not the whole PII-bearing document).

## Outstanding items for implementation (not blocking planning)

- Exact list of TCGplayer condition-prefix strings (§3) should be confirmed/extended against the full set of real fixture files gathered during implementation, beyond the one example already reviewed.
- One Piece Card Game packing-slip line format has not yet been observed in a real example; the parser should be implemented against the confirmed Pokémon/Magic/Lorcana format and treated as needing a fixture-driven check before considering One Piece support verified.
