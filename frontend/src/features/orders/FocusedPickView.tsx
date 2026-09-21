import { useMemo, useState } from 'react'
import { advanceFrom, computeProgress } from './orderGrouping'
import type { SetGroup } from './orderGrouping'
import { SetTransition } from './SetTransition'
import type { SetTransitionAdvance } from './SetTransition'
import { ReportIssueForm } from './ReportIssueForm'
import { pickingIssueTypeLabel } from './ordersApi'
import type { ReportIssueRequest } from './ordersApi'

/**
 * Picking one product at a time (016-mobile-picking, PRD §8, §12, §18).
 *
 * The rule this component exists to keep: **moving never records anything**. Navigation changes
 * which product is on screen and nothing else; only the Picked button and the issue form record
 * an outcome (FR-011, PRD §23). Swipe is layered on top of on-screen controls rather than
 * replacing them, so every product stays reachable without a gesture (FR-013).
 */

export interface FocusedPickViewProps {
  groups: SetGroup[]
  canRecordOutcome: boolean
  /** Why recording is unavailable, shown instead of the actions. */
  blockedReason: string | null
  recordingLineId: number | null
  onPicked: (lineId: number) => void
  onReportIssue: (lineId: number, request: ReportIssueRequest) => void
  /** Called when the picker moves past the end of the order. */
  onOrderEnd?: () => void
}

/** Minimum horizontal travel before a drag counts as a swipe rather than a tap. */
const SWIPE_THRESHOLD_PX = 60

export function FocusedPickView({
  groups,
  canRecordOutcome,
  blockedReason,
  recordingLineId,
  onPicked,
  onReportIssue,
  onOrderEnd,
}: FocusedPickViewProps) {
  const orderedLines = useMemo(() => groups.flatMap((group) => group.lines), [groups])

  const [currentLineId, setCurrentLineId] = useState<number | null>(
    () => orderedLines[0]?.id ?? null,
  )
  const [transition, setTransition] = useState<SetTransitionAdvance | null>(null)
  const [isReportingIssue, setIsReportingIssue] = useState(false)
  const [touchStartX, setTouchStartX] = useState<number | null>(null)

  const currentIndex = orderedLines.findIndex((line) => line.id === currentLineId)
  const line = orderedLines[currentIndex] ?? orderedLines[0]
  const progress = computeProgress(groups, line?.id ?? null)

  if (!line) return null

  function goTo(lineId: number) {
    // Navigation only. Nothing here touches an outcome.
    setCurrentLineId(lineId)
    setTransition(null)
    setIsReportingIssue(false)
  }

  function goNext() {
    const advance = advanceFrom(groups, line.id)

    if (advance.kind === 'line') {
      goTo(advance.line.id)
      return
    }

    if (advance.kind === 'order-end') {
      onOrderEnd?.()
      return
    }

    setIsReportingIssue(false)
    setTransition(advance)
  }

  function goPrevious() {
    if (transition) {
      // Step back out of a transition to the product that led into it.
      setTransition(null)
      return
    }

    const previous = orderedLines[currentIndex - 1]
    if (previous) goTo(previous.id)
  }

  /** Leaving a transition: start the next box, or walk away from an unfinished one. */
  function continuePastTransition() {
    const nextSet = transition?.nextSet ?? null
    const firstOfNextSet = nextSet?.lines[0]

    if (firstOfNextSet) {
      goTo(firstOfNextSet.id)
      return
    }

    setTransition(null)
    onOrderEnd?.()
  }

  if (transition) {
    return (
      <div className="focused-pick">
        <SetTransition
          advance={transition}
          onContinue={continuePastTransition}
          onReturnToLine={goTo}
          onReportMissing={(lineId) => {
            goTo(lineId)
            setIsReportingIssue(true)
          }}
        />
        <button type="button" className="focused-pick__back" onClick={goPrevious}>
          Back
        </button>
      </div>
    )
  }

  const isFirst = currentIndex <= 0
  const isRecording = recordingLineId === line.id

  return (
    <div
      className="focused-pick"
      // Swipe is an addition to the buttons below, never a replacement — and it is bound to
      // navigation only, never to recording a pick.
      onTouchStart={(event) => setTouchStartX(event.touches[0]?.clientX ?? null)}
      onTouchEnd={(event) => {
        if (touchStartX === null) return
        const travel = (event.changedTouches[0]?.clientX ?? touchStartX) - touchStartX
        setTouchStartX(null)
        if (travel <= -SWIPE_THRESHOLD_PX) goNext()
        else if (travel >= SWIPE_THRESHOLD_PX && !isFirst) goPrevious()
      }}
    >
      <p className="focused-pick__progress">
        <span className="focused-pick__set">{progress.currentSetName}</span>
        {` · ${progress.currentSetPosition} of ${progress.currentSetSize} in this box`}
      </p>
      <p className="focused-pick__order-progress">
        {`${progress.accountedCards} of ${progress.totalCards} cards accounted for`}
      </p>

      <article className="focused-pick__card" aria-label={`Product ${line.productName}`}>
        {line.imageUrl !== null ? (
          <img className="focused-pick__image" src={line.imageUrl} alt={line.productName} />
        ) : (
          <div className="focused-pick__placeholder" aria-label="Card image unavailable">
            <span aria-hidden="true">No image</span>
          </div>
        )}

        <h2 className="focused-pick__name">{line.productName}</h2>

        <p className="focused-pick__identity">
          <span>{line.collectorNumber}</span>
          {line.variant !== null && <span>{line.variant}</span>}
          <span>{line.condition}</span>
        </p>

        <p className="focused-pick__quantity">
          {line.quantity > 1 ? (
            <>
              {/* Spelled out as well as shown: a bare numeral is easy to skim past on a phone
                  held in one hand, and a missed multiple is the costliest picking error. */}
              <strong data-emphasis="high">{line.quantity}</strong>
              <span className="focused-pick__quantity-words">{`Pull ${line.quantity} copies`}</span>
            </>
          ) : (
            <span>{line.quantity}</span>
          )}
        </p>

        {line.currentIssue && (
          <p className="focused-pick__issue" role="status">
            <strong data-emphasis="high">
              {pickingIssueTypeLabel(line.currentIssue.issueType)}
            </strong>
          </p>
        )}
      </article>

      {canRecordOutcome ? (
        <div className="focused-pick__actions">
          <button
            type="button"
            className="focused-pick__picked"
            aria-pressed={line.pickOutcome === 'picked'}
            disabled={isRecording}
            onClick={() => onPicked(line.id)}
          >
            Picked
          </button>
          {!isReportingIssue && (
            <button
              type="button"
              className="focused-pick__report"
              disabled={isRecording}
              onClick={() => setIsReportingIssue(true)}
            >
              Report Issue
            </button>
          )}
        </div>
      ) : (
        blockedReason && (
          <p className="focused-pick__blocked" role="status">
            {blockedReason}
          </p>
        )
      )}

      {canRecordOutcome && isReportingIssue && (
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

      <nav className="focused-pick__navigation" aria-label="Move between products">
        <button type="button" onClick={goPrevious} disabled={isFirst}>
          Previous
        </button>
        <button type="button" onClick={goNext}>
          Next
        </button>
      </nav>
    </div>
  )
}
