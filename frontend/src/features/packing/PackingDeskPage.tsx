import { useCallback, useEffect, useState } from 'react'
import { ScanBox } from './ScanBox'
import { PrintLabelButton } from '../labels/PrintLabelButton'
import {
  getAwaitingPacking,
  markPacked,
  packingSlipUrl,
  resolvePackingCode,
  OrderAlreadyPackedError,
  OrderHasUnresolvedIssueError,
  PackingOrderNotFoundError,
} from './packingApi'
import type { PackingView } from './packingApi'
import './PackingDeskPage.css'

/**
 * The packing desk (PRD §22.1, US2).
 *
 * A desktop surface: packing is a seated job at a bench, not one-handed at a storage box. Scanning
 * is the way in; the list underneath exists for a label that fell off and for seeing how much work
 * is waiting, not to be clicked through row by row.
 */
export function PackingDeskPage() {
  const [order, setOrder] = useState<PackingView | null>(null)
  const [awaiting, setAwaiting] = useState<PackingView[]>([])
  const [error, setError] = useState<string | null>(null)
  const [isBusy, setIsBusy] = useState(false)
  const [queueFailed, setQueueFailed] = useState(false)

  const refreshAwaiting = useCallback(async () => {
    try {
      setQueueFailed(false)
      setAwaiting(await getAwaitingPacking())
    } catch {
      // The queue is a convenience and must not take the scan box down with it — but an
      // empty list and a failed load look identical, and "nothing is waiting" is a lie worth
      // avoiding. Say which happened.
      setAwaiting([])
      setQueueFailed(true)
    }
  }, [])

  useEffect(() => {
    void refreshAwaiting()
  }, [refreshAwaiting])

  async function handleScan(code: string) {
    setIsBusy(true)
    setError(null)
    setOrder(null)
    try {
      setOrder(await resolvePackingCode(code))
    } catch (caught) {
      setError(
        caught instanceof PackingOrderNotFoundError
          ? `No order matches "${code}". Check the label, or type the order number.`
          : "Couldn't look that order up. Try again.",
      )
    } finally {
      setIsBusy(false)
    }
  }

  async function handleMarkPacked() {
    if (order === null) return

    setIsBusy(true)
    setError(null)
    try {
      setOrder(await markPacked(order.orderId))
      await refreshAwaiting()
    } catch (caught) {
      if (caught instanceof OrderHasUnresolvedIssueError) {
        setError(
          `This order can't be packed yet: ${caught.unresolvedProducts.join(', ')} still needs a manager. The sleeve belongs in the review area.`,
        )
      } else if (caught instanceof OrderAlreadyPackedError) {
        setError('This order has already been packed.')
      } else {
        setError("Couldn't mark that order packed. Try again.")
      }
    } finally {
      setIsBusy(false)
    }
  }

  return (
    <main className="packing-desk">
      <header className="packing-desk__header">
        <h1>Packing</h1>
        <p>Scan a sleeve's label to print its packing slip and record it as packed.</p>
      </header>

      <ScanBox onSubmit={handleScan} isBusy={isBusy} />

      {error !== null && (
        <p role="alert" className="packing-desk__error">
          {error}
        </p>
      )}

      {order !== null && (
        <section className="packing-desk__order" aria-label={`Order ${order.orderId}`}>
          <div className="packing-desk__identity">
            <h2>Order {order.orderId}</h2>
            <p className="packing-desk__tcg">{order.tcgplayerOrderId}</p>
          </div>

          <dl className="packing-desk__facts">
            <div>
              <dt>Cards</dt>
              <dd className="packing-desk__count">{order.cardCount}</dd>
            </div>
            <div>
              <dt>Picked by</dt>
              {/* Every contributor, not a truncated list — only the label is size-constrained. */}
              <dd>{order.pickedBy.map((person) => person.displayName).join(', ') || '—'}</dd>
            </div>
            <div>
              <dt>Picked</dt>
              <dd>{order.pickedAt === null ? '—' : new Date(order.pickedAt).toLocaleString()}</dd>
            </div>
          </dl>

          {!order.canPack && (
            <div className="packing-desk__blocked" role="status">
              <p>{order.blockedReason}</p>
              {order.unresolvedProducts.length > 0 && (
                <ul>
                  {order.unresolvedProducts.map((product) => (
                    <li key={product}>{product}</li>
                  ))}
                </ul>
              )}
            </div>
          )}

          <div className="packing-desk__actions">
            {order.hasPackingSlip ? (
              <a
                className="packing-desk__primary"
                href={packingSlipUrl(order.orderId)}
                target="_blank"
                rel="noreferrer"
              >
                Print packing slip
              </a>
            ) : (
              // Normal, not an error: every order imported before this feature is like this.
              <p className="packing-desk__noSlip">
                No packing slip is stored for this order. Print it from TCGplayer using the order
                number above.
              </p>
            )}

            {order.canPack && (
              <button
                type="button"
                className="packing-desk__secondary"
                onClick={handleMarkPacked}
                disabled={isBusy}
              >
                Mark packed
              </button>
            )}

            {/* The other half of FR-016: a sleeve can arrive at the bench with its label
                missing, and the desk is where that gets noticed. */}
            <PrintLabelButton orderId={order.orderId} className="packing-desk__secondary">
              Print label again
            </PrintLabelButton>
          </div>
        </section>
      )}

      <section className="packing-desk__queue" aria-label="Awaiting packing">
        <h2>Awaiting packing · {awaiting.length}</h2>
        {queueFailed ? (
          <p className="packing-desk__empty" role="status">
            Couldn't load what's awaiting packing. Scanning still works.
          </p>
        ) : awaiting.length === 0 ? (
          <p className="packing-desk__empty">Nothing is waiting to be packed.</p>
        ) : (
          <ul>
            {awaiting.map((waiting) => (
              <li key={waiting.orderId}>
                <span className="packing-desk__queueCode">Order {waiting.orderId}</span>
                <span className="packing-desk__queueTcg">{waiting.tcgplayerOrderId}</span>
                <span className="packing-desk__queueCount">
                  {waiting.cardCount} {waiting.cardCount === 1 ? 'card' : 'cards'}
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </main>
  )
}
