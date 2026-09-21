import { useState } from 'react'
import type { LabelContent } from './ordersApi'
import { OrderLabel } from '../labels/OrderLabel'
import './PickEnding.css'

/**
 * Where every pick ends (PRD §22).
 *
 * Two endings, and the difference between them is not decoration: one sends the sleeve to
 * ready-to-pack, the other to the review area. That instruction is the last thing a picker reads
 * before the bundle leaves their hand, so the screen says it in words.
 *
 * The screen states one count — physical cards. A count of product lines cannot be checked against
 * a sleeve of loose cards, and on a screen whose only job is catching a miscount, a number nobody
 * can verify competes for attention with the one they can (FR-004).
 */
export interface PickEndingProps {
  label: LabelContent
  onNextOrder: () => void
  onBackToDashboard: () => void
}

export function PickEnding({ label, onNextOrder, onBackToDashboard }: PickEndingProps) {
  /**
   * Whether a label has been *requested*, not whether paper came out. The application cannot
   * detect that, so it never records printing as a fact about the order — this only decides which
   * action leads (FR-006).
   */
  const [labelRequested, setLabelRequested] = useState(false)

  function handlePrint() {
    setLabelRequested(true)
    window.print()
  }

  const setAside = label.setAsideCount
  const primary = 'pick-ending__button pick-ending__primary'
  const secondary = 'pick-ending__button pick-ending__secondary'

  return (
    <section className="pick-ending" aria-label={label.isHeld ? 'Pick ended' : 'Pick complete'}>
      <div className={`pick-ending__mark${label.isHeld ? ' pick-ending__mark--held' : ''}`}>
        <span aria-hidden="true">{label.isHeld ? '!' : '✓'}</span>
      </div>

      <h2 className="pick-ending__title">
        {label.isHeld ? 'Pick ended — needs a manager' : 'Pick complete'}
      </h2>

      <p className="pick-ending__count">{label.cardCount}</p>
      <p className="pick-ending__countLabel">
        {label.isHeld
          ? label.cardCount === 1
            ? 'card pulled'
            : 'cards pulled'
          : label.cardCount === 1
            ? 'card in the sleeve'
            : 'cards in the sleeve'}
      </p>

      {label.isHeld && (
        <div className="pick-ending__unresolved">
          <p className="pick-ending__unresolvedTitle">
            {label.unresolvedProducts.length === 1
              ? 'Still unresolved'
              : `${label.unresolvedProducts.length} still unresolved`}
          </p>
          <ul>
            {label.unresolvedProducts.map((product) => (
              <li key={product}>{product}</li>
            ))}
          </ul>
          {setAside !== null && setAside > 0 && (
            <p className="pick-ending__setAside">
              {setAside === 1 ? '1 card set aside' : `${setAside} cards set aside`} with the order
            </p>
          )}
        </div>
      )}

      <p className="pick-ending__destination">
        {label.isHeld
          ? 'Stick the hold label on the sleeve and put it in the review area — not ready-to-pack.'
          : 'Stick the label on the sleeve and put it in the ready-to-pack bin.'}
      </p>

      <div className="pick-ending__actions">
        <button
          type="button"
          className={labelRequested ? secondary : primary}
          onClick={handlePrint}
        >
          {labelRequested ? 'Print again' : label.isHeld ? 'Print hold label' : 'Print label'}
        </button>
        <button
          type="button"
          className={labelRequested ? primary : secondary}
          onClick={onNextOrder}
        >
          Next order
        </button>
        <button type="button" className="pick-ending__quiet" onClick={onBackToDashboard}>
          Back to dashboard
        </button>
      </div>

      {/* Rendered off-screen and revealed only by the print stylesheet, so the picker reads the
          summary while the printer gets the label. */}
      <div className="pick-ending__labelHost">
        <OrderLabel label={label} />
      </div>
    </section>
  )
}
