import { useMemo, useState } from 'react'
import { advanceFrom, computeProgress, setGroupOf } from './orderGrouping'
import type { SetGroup } from './orderGrouping'
import { OrderFinish } from './OrderFinish'
import { ReportIssueForm } from './ReportIssueForm'
import { pickingIssueTypeLabel } from './ordersApi'
import type { ReportIssueRequest } from './ordersApi'

/**
 * Picking one card at a time (016-mobile-picking, PRD §8, §12, §18).
 *
 * Two rules shape the screen:
 *
 * 1. Moving never records anything (FR-011). Only the record button and the issue form do.
 * 2. Moving is never blocked (FR-019a). Advancing is looking, not deciding, so nothing
 *    outstanding stops it. Everything gets checked once, on the final review.
 *
 * One card, one action, one way out. A box change is a band on the card rather than a screen of
 * its own, because most boxes hold a single card and an interstitial between every card is all
 * ceremony and no content.
 */

export interface FocusedPickViewProps {
  groups: SetGroup[]
  canRecordOutcome: boolean
  /** Why recording is unavailable, shown instead of the action. */
  blockedReason: string | null
  recordingLineId: number | null
  onPicked: (lineId: number) => void
  onReportIssue: (lineId: number, request: ReportIssueRequest) => void
  /** The picker has completed the order from the final review. */
  onCompleted?: () => void
}

/** Minimum horizontal travel before a drag counts as a swipe rather than a tap. */
const SWIPE_THRESHOLD_PX = 50

export function FocusedPickView({
  groups,
  canRecordOutcome,
  blockedReason,
  recordingLineId,
  onPicked,
  onReportIssue,
  onCompleted,
}: FocusedPickViewProps) {
  const orderedLines = useMemo(() => groups.flatMap((group) => group.lines), [groups])

  const [currentLineId, setCurrentLineId] = useState<number | null>(
    () => orderedLines[0]?.id ?? null,
  )
  const [isReviewing, setIsReviewing] = useState(false)
  const [isReportingIssue, setIsReportingIssue] = useState(false)
  const [enteredSet, setEnteredSet] = useState<SetGroup | null>(null)
  const [touchStartX, setTouchStartX] = useState<number | null>(null)

  const currentIndex = orderedLines.findIndex((line) => line.id === currentLineId)
  const line = orderedLines[currentIndex] ?? orderedLines[0]

  if (!line) return null

  const progress = computeProgress(groups, line.id)
  const box = setGroupOf(groups, line.id)

  function goTo(lineId: number, entering: SetGroup | null = null) {
    setCurrentLineId(lineId)
    setEnteredSet(entering)
    setIsReviewing(false)
    setIsReportingIssue(false)
  }

  function goNext() {
    const advance = advanceFrom(groups, line.id)

    // Past the last card: the review, which is the only screen that interrupts.
    if (advance.kind === 'order-end') {
      setIsReportingIssue(false)
      setIsReviewing(true)
      return
    }

    goTo(advance.line.id, advance.enteringSet)
  }

  function goPrevious() {
    const previous = orderedLines[currentIndex - 1]
    if (previous) goTo(previous.id)
  }

  if (isReviewing) {
    return (
      <OrderFinish
        groups={groups}
        onReturnToLine={(lineId) => goTo(lineId)}
        onComplete={() => onCompleted?.()}
        onBackToCards={() => setIsReviewing(false)}
      />
    )
  }

  const isRecording = recordingLineId === line.id
  const isPicked = line.pickOutcome === 'picked'
  // Mirrors the "2 of 3" beside it, so the bar and the number never disagree. It shows where
  // the picker is, not what has been pulled — the review screen is where that gets checked.
  const boxFill = progress.currentSetSize
    ? Math.round((progress.currentSetPosition / progress.currentSetSize) * 100)
    : 0

  return (
    <div
      className="focused-pick"
      onTouchStart={(event) => setTouchStartX(event.touches[0]?.clientX ?? null)}
      onTouchEnd={(event) => {
        if (touchStartX === null) return
        const travel = (event.changedTouches[0]?.clientX ?? touchStartX) - touchStartX
        setTouchStartX(null)
        // Swipe moves between cards and nothing else — it can never record an outcome.
        if (travel <= -SWIPE_THRESHOLD_PX) goNext()
        else if (travel >= SWIPE_THRESHOLD_PX && currentIndex > 0) goPrevious()
      }}
    >
      {/* Where the picker is standing, answered before anything else. */}
      <div className="focused-pick__box">
        <div className="focused-pick__boxRow">
          <p className="focused-pick__boxName">{box?.setName}</p>
          <span className="focused-pick__boxCount">
            {`${progress.currentSetPosition} of ${progress.currentSetSize}`}
          </span>
        </div>
        <div className="focused-pick__boxBar">
          <div className="focused-pick__boxFill" style={{ width: `${boxFill}%` }} />
        </div>
        {enteredSet && (
          <p className="focused-pick__entering">
            {`New box · ${enteredSet.cardCount} ${enteredSet.cardCount === 1 ? 'card' : 'cards'}`}
          </p>
        )}
      </div>

      <article
        className={`focused-pick__card${isPicked ? ' focused-pick__card--picked' : ''}`}
        aria-label={`Product ${line.productName}`}
      >
        {line.imageUrl !== null ? (
          <img className="focused-pick__image" src={line.imageUrl} alt={line.productName} />
        ) : (
          <div className="focused-pick__placeholder">
            <span>No image</span>
          </div>
        )}

        <h2 className="focused-pick__name">{line.productName}</h2>

        {/* The number is how you find it in the box. The variant is the one thing the picture
            cannot tell you — holofoil and non-holo are different cards that look identical. */}
        <p className="focused-pick__chips">
          <span className="focused-pick__number">{line.collectorNumber}</span>
          {line.variant !== null && (
            <span className="focused-pick__variant">{line.variant.toUpperCase()}</span>
          )}
        </p>
        <p className="focused-pick__detail">
          {[line.rarity, line.condition].filter(Boolean).join(' · ')}
        </p>

        {/* The costliest picking error in the shop (PRD §5.3, §15, and the §2 example). */}
        {line.quantity > 1 && (
          <p className="focused-pick__quantity">
            <strong data-emphasis="high">{line.quantity}</strong>
            <span>copies to pull</span>
          </p>
        )}

        {line.currentIssue && (
          <p className="focused-pick__issue">
            {pickingIssueTypeLabel(line.currentIssue.issueType)}
          </p>
        )}
      </article>

      {isReportingIssue && canRecordOutcome && (
        <ReportIssueForm
          lineId={line.id}
          isSubmitting={isRecording}
          onCancel={() => setIsReportingIssue(false)}
          onSubmit={(request) => {
            setIsReportingIssue(false)
            onReportIssue(line.id, request)
          }}
        />
      )}

      {/* Pinned in thumb reach: the record action is never scrolled past. */}
      <div className="focused-pick__dock">
        {canRecordOutcome && !isReportingIssue ? (
          <>
            <button
              type="button"
              className="focused-pick__picked"
              aria-pressed={isPicked}
              disabled={isRecording}
              onClick={() => onPicked(line.id)}
            >
              {isRecording
                ? 'Recording…'
                : isPicked
                  ? 'Picked ✓'
                  : line.quantity > 1
                    ? `Pulled all ${line.quantity}`
                    : 'Picked'}
            </button>
            <button
              type="button"
              className="focused-pick__report"
              disabled={isRecording}
              onClick={() => setIsReportingIssue(true)}
            >
              Report an issue
            </button>
          </>
        ) : (
          !canRecordOutcome &&
          blockedReason && <p className="focused-pick__blocked">{blockedReason}</p>
        )}

        <nav className="focused-pick__navigation" aria-label="Move between cards">
          <button
            type="button"
            aria-label="Previous card"
            onClick={goPrevious}
            disabled={currentIndex <= 0}
          >
            ‹
          </button>
          {/* Labelled: the band above shows a position within the box, and in a single-box
              order the two would otherwise read as the same number twice. */}
          <span className="focused-pick__counter">
            {`Card ${currentIndex + 1} of ${orderedLines.length}`}
          </span>
          <button type="button" aria-label="Next card" onClick={goNext}>
            ›
          </button>
        </nav>
      </div>
    </div>
  )
}
