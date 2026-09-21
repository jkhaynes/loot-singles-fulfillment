import type { SetGroup } from './orderGrouping'
import { pickingIssueTypeLabel } from './ordersApi'
import type { OrderLineDetail } from './ordersApi'

/**
 * The final review, before an order is completed (016-mobile-picking, PRD §22).
 *
 * Card by card, a quantity mistake is invisible: the picker reads PULL 3, pulls what they
 * believe is three, and moves on. This is the first time the order is visible as a whole, and
 * deliberately without images — re-showing the artwork invites the same recognition they have
 * already made, while reading a name and a number forces a re-read.
 *
 * The headline is the point. It is the number of cards that should be in the picker's hand, so
 * it can be counted against the sleeve: a verification rather than a confirmation. The list
 * below is how a discrepancy gets found, not something anyone is expected to audit line by line.
 */

export interface OrderFinishProps {
  groups: SetGroup[]
  onReturnToLine: (lineId: number) => void
  /** Complete the order. Enabled whatever is outstanding; finishing short is deliberate. */
  onComplete: () => void
  onBackToCards: () => void
}

/** Physical cards the picker should be holding — what was pulled, not what was ordered. */
function pulledCardCount(groups: SetGroup[]): number {
  return groups
    .flatMap((group) => group.lines)
    .filter((line) => line.pickOutcome === 'picked')
    .reduce((total, line) => total + line.quantity, 0)
}

function statusOf(line: OrderLineDetail): 'picked' | 'issue' | 'open' {
  if (line.pickOutcome === 'picked') return 'picked'
  if (line.pickOutcome === 'hasIssue') return 'issue'
  return 'open'
}

export function OrderFinish({
  groups,
  onReturnToLine,
  onComplete,
  onBackToCards,
}: OrderFinishProps) {
  const lines = groups.flatMap((group) => group.lines)
  const cardsInHand = pulledCardCount(groups)
  const pickedProducts = lines.filter((line) => line.pickOutcome === 'picked').length
  const reported = lines.filter((line) => line.pickOutcome === 'hasIssue').length
  const open = lines.filter((line) => line.pickOutcome === null).length

  return (
    <section className="order-finish" aria-label="Final review">
      <div className="order-finish__headline">
        <p className="order-finish__eyebrow">Count the sleeve</p>
        <p className="order-finish__count">{cardsInHand}</p>
        <p className="order-finish__countLabel">
          {cardsInHand === 1 ? 'card should be in your hand' : 'cards should be in your hand'}
        </p>
        <p className="order-finish__summary">
          {`${pickedProducts} ${pickedProducts === 1 ? 'product' : 'products'} pulled`}
          {reported > 0 && (
            <span className="order-finish__reported">{` · ${reported} reported`}</span>
          )}
          {open > 0 && <span className="order-finish__open">{` · ${open} not looked at`}</span>}
        </p>
      </div>

      <div className="order-finish__boxes">
        {groups.map((group) => (
          <div key={`${group.game}\u0000${group.setName}`} className="order-finish__box">
            <p className="order-finish__boxName">{group.setName}</p>
            <ul className="order-finish__lines">
              {group.lines.map((line) => {
                const status = statusOf(line)
                return (
                  <li key={line.id}>
                    <button
                      type="button"
                      className={`order-finish__line order-finish__line--${status}`}
                      onClick={() => onReturnToLine(line.id)}
                    >
                      <span className="order-finish__lineName">{line.productName}</span>
                      <span className="order-finish__lineMeta">
                        {status === 'issue' && line.currentIssue
                          ? pickingIssueTypeLabel(line.currentIssue.issueType)
                          : status === 'open'
                            ? 'Not looked at'
                            : `${line.collectorNumber}${line.variant ? ` · ${line.variant}` : ''}`}
                      </span>
                      {/* Quantity is where a mistake hides, so it is the loudest thing on the row. */}
                      {line.quantity > 1 && status === 'picked' ? (
                        <strong className="order-finish__qty" data-emphasis="high">
                          {line.quantity}
                        </strong>
                      ) : (
                        <span className="order-finish__qty">{line.quantity}</span>
                      )}
                      <span className="order-finish__status" aria-hidden="true">
                        {status === 'picked' ? '✓' : status === 'issue' ? '!' : '·'}
                      </span>
                    </button>
                  </li>
                )
              })}
            </ul>
          </div>
        ))}
      </div>

      <div className="order-finish__dock">
        <button type="button" className="order-finish__primary" onClick={onComplete}>
          {`Complete — ${cardsInHand} ${cardsInHand === 1 ? 'card' : 'cards'}`}
        </button>
        <button type="button" className="order-finish__secondary" onClick={onBackToCards}>
          Back to the cards
        </button>
      </div>
    </section>
  )
}
