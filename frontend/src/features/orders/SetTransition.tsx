import type { Advance } from './orderGrouping'

/**
 * The screen between two storage boxes (016-mobile-picking, PRD §13).
 *
 * Two shapes, and the difference between them is the point: a finished box hands over cleanly,
 * while a box that still owes cards refuses to pretend otherwise and makes the picker choose
 * (FR-016 – FR-018). None of these choices records an outcome — leaving is a navigation, never
 * a decision about a card (FR-019).
 */

export type SetTransitionAdvance = Extract<
  Advance,
  { kind: 'set-complete' } | { kind: 'set-incomplete' }
>

export interface SetTransitionProps {
  advance: SetTransitionAdvance
  /** Move on: start the next box, or leave this one with work outstanding. */
  onContinue: () => void
  onReturnToLine: (lineId: number) => void
  onReportMissing: (lineId: number) => void
}

function cardsLabel(count: number): string {
  return `${count} ${count === 1 ? 'card' : 'cards'}`
}

function productsLabel(count: number): string {
  return `${count} ${count === 1 ? 'product' : 'products'}`
}

export function SetTransition({
  advance,
  onContinue,
  onReturnToLine,
  onReportMissing,
}: SetTransitionProps) {
  if (advance.kind === 'set-complete') {
    const { finishedSet, nextSet } = advance

    return (
      <section className="set-transition set-transition--complete" aria-label="Box finished">
        <p className="set-transition__eyebrow">Box finished</p>
        <h2 className="set-transition__set">{finishedSet.setName}</h2>
        <p className="set-transition__summary">
          {`${productsLabel(finishedSet.productCount)} · ${cardsLabel(finishedSet.cardCount)}`}
        </p>

        {nextSet && (
          <div className="set-transition__next">
            <p className="set-transition__eyebrow">Next box</p>
            <h3 className="set-transition__set">{nextSet.setName}</h3>
            <p className="set-transition__summary">
              {`${productsLabel(nextSet.productCount)} · `}
              <strong>{cardsLabel(nextSet.cardCount)}</strong>
            </p>
            <button type="button" className="set-transition__primary" onClick={onContinue}>
              {`Start ${nextSet.setName}`}
            </button>
          </div>
        )}
      </section>
    )
  }

  const { set, unresolvedLines } = advance
  const firstUnresolved = unresolvedLines[0]

  return (
    <section className="set-transition set-transition--incomplete" aria-label="Box not finished">
      {/* An alert, not a notice: walking away from here is how a card gets missed. */}
      <div role="alert" className="set-transition__warning">
        <p className="set-transition__eyebrow">Still in this box</p>
        <h2 className="set-transition__set">{set.setName}</h2>
        <p className="set-transition__summary">{`${unresolvedLines.length} not pulled`}</p>
      </div>

      <ul className="set-transition__outstanding">
        {unresolvedLines.map((line) => (
          <li key={line.id}>
            <span className="set-transition__outstanding-name">{line.productName}</span>
            <span className="set-transition__outstanding-number">{line.collectorNumber}</span>
            {line.quantity > 1 ? (
              <strong data-emphasis="high">{line.quantity}</strong>
            ) : (
              <span>{line.quantity}</span>
            )}
          </li>
        ))}
      </ul>

      {/* Exactly three ways out, and no way past without taking one (FR-017). */}
      <button
        type="button"
        className="set-transition__primary"
        onClick={() => onReturnToLine(firstUnresolved.id)}
      >
        {`Back to ${firstUnresolved.productName}`}
      </button>
      <button
        type="button"
        className="set-transition__secondary"
        onClick={() => onReportMissing(firstUnresolved.id)}
      >
        Report what is missing
      </button>
      <button type="button" className="set-transition__tertiary" onClick={onContinue}>
        Leave the box anyway
      </button>
    </section>
  )
}
