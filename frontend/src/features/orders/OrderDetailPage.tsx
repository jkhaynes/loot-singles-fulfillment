import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import {
  getOrderDetail,
  releaseOrder,
  forceReleaseOrder,
  recordPicked,
  orderStatusLabel,
  OrderNotFoundError,
} from './ordersApi'
import type { OrderDetail } from './ordersApi'
import { useAuth } from '../auth/AuthContext'
import './OrderDetailPage.css'

type LoadState = 'loading' | 'loaded' | 'not-found' | 'error'

export function OrderDetailPage() {
  const { orderId } = useParams()
  const { employee } = useAuth()
  const [order, setOrder] = useState<OrderDetail | null>(null)
  const [loadState, setLoadState] = useState<LoadState>('loading')
  const [releaseError, setReleaseError] = useState<string | null>(null)
  const [isReleasing, setIsReleasing] = useState(false)
  const [isForceReleasing, setIsForceReleasing] = useState(false)
  const [recordingLineId, setRecordingLineId] = useState<number | null>(null)

  useEffect(() => {
    let cancelled = false

    getOrderDetail(Number(orderId))
      .then((result) => {
        if (!cancelled) {
          setOrder(result)
          setLoadState('loaded')
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setLoadState(error instanceof OrderNotFoundError ? 'not-found' : 'error')
        }
      })

    return () => {
      cancelled = true
    }
  }, [orderId])

  async function handleRelease() {
    if (!order) return

    setIsReleasing(true)
    setReleaseError(null)
    try {
      const updated = await releaseOrder(order.orderId)
      setOrder({
        ...order,
        status: updated.status,
        claimedByEmployeeId: updated.claimedByEmployeeId,
        claimedByEmployeeName: updated.claimedByEmployeeName,
      })
    } catch {
      setReleaseError("Couldn't release this order. Try refreshing the page.")
    } finally {
      setIsReleasing(false)
    }
  }

  async function handleForceRelease() {
    if (!order) return

    setIsForceReleasing(true)
    setReleaseError(null)
    try {
      const updated = await forceReleaseOrder(order.orderId)
      setOrder({
        ...order,
        status: updated.status,
        claimedByEmployeeId: updated.claimedByEmployeeId,
        claimedByEmployeeName: updated.claimedByEmployeeName,
      })
    } catch {
      setReleaseError("Couldn't force-release this order. Try refreshing the page.")
    } finally {
      setIsForceReleasing(false)
    }
  }

  async function handlePicked(lineId: number) {
    if (!order) return

    setRecordingLineId(lineId)
    setReleaseError(null)
    try {
      setOrder(await recordPicked(order.orderId, lineId))
    } catch {
      setReleaseError("Couldn't record that pick. Try refreshing the page.")
    } finally {
      setRecordingLineId(null)
    }
  }

  const canRelease =
    order !== null && employee !== null && order.claimedByEmployeeId === employee.employeeId
  const canForceRelease =
    order !== null &&
    employee !== null &&
    employee.role === 'ManagerAdmin' &&
    order.claimedByEmployeeId !== null &&
    order.claimedByEmployeeId !== employee.employeeId
  const canRecordOutcome = canRelease
  const confirmedLineCount =
    order?.lines.filter((line) => line.pickOutcome === 'picked').length ?? 0

  return (
    <main className="order-detail-page">
      <header className="order-detail-header">
        <div>
          <p className="order-detail-header__eyebrow">Order picking detail</p>
          <h1>{order ? `Order ${order.tcgplayerOrderId}` : 'Order detail'}</h1>
          {order && (
            <p
              className="order-detail-header__status"
              aria-label={`Order status: ${orderStatusLabel(order.status)}`}
            >
              {order.claimedByEmployeeName
                ? `${orderStatusLabel(order.status)} · Picking by ${order.claimedByEmployeeName}`
                : orderStatusLabel(order.status)}
            </p>
          )}
          {order && (
            <p className="order-detail-header__progress">
              {`${confirmedLineCount} of ${order.lines.length} lines confirmed`}
            </p>
          )}
          {releaseError && (
            <p role="alert" className="order-detail-header__error">
              {releaseError}
            </p>
          )}
        </div>
        <nav className="order-detail-navigation" aria-label="Order detail navigation">
          {canRelease && (
            <button type="button" onClick={handleRelease} disabled={isReleasing}>
              {isReleasing ? 'Releasing…' : 'Release'}
            </button>
          )}
          {canForceRelease && (
            <button type="button" onClick={handleForceRelease} disabled={isForceReleasing}>
              {isForceReleasing ? 'Force-releasing…' : 'Force-Release'}
            </button>
          )}
          <Link to="/orders">Browse Orders</Link>
          <Link to="/">Dashboard</Link>
        </nav>
      </header>

      {loadState === 'loading' ? (
        <p className="order-detail-state">Loading order…</p>
      ) : loadState === 'not-found' ? (
        <p role="alert" className="order-detail-state order-detail-state--error">
          Order not found. It may have been removed or the address may be incorrect.
        </p>
      ) : loadState === 'error' ? (
        <p role="alert" className="order-detail-state order-detail-state--error">
          Couldn't load order. Try refreshing the page.
        </p>
      ) : (
        <section className="order-detail-lines" aria-label="Products to pick">
          {order?.lines.map((line) => (
            <article
              key={line.id}
              className="order-detail-line"
              aria-label={`Product ${line.productName}`}
            >
              {line.imageUrl !== null ? (
                <img
                  className="order-detail-line__image"
                  src={line.imageUrl}
                  alt={line.productName}
                />
              ) : (
                <div className="order-detail-line__placeholder" aria-label="Card image unavailable">
                  <span aria-hidden="true">No image</span>
                </div>
              )}
              <div className="order-detail-line__identity">
                <h2>{line.productName}</h2>
                <dl className="order-detail-line__attributes">
                  <div>
                    <dt>Product Line</dt>
                    <dd>{line.productLine}</dd>
                  </div>
                  <div>
                    <dt>Set</dt>
                    <dd>{line.set}</dd>
                  </div>
                  <div>
                    <dt>Collector Number</dt>
                    <dd>{line.collectorNumber}</dd>
                  </div>
                  {line.rarity !== null && (
                    <div>
                      <dt>Rarity</dt>
                      <dd>{line.rarity}</dd>
                    </div>
                  )}
                  {line.variant !== null && (
                    <div>
                      <dt>Variant</dt>
                      <dd>{line.variant}</dd>
                    </div>
                  )}
                  <div>
                    <dt>Condition</dt>
                    <dd>{line.condition}</dd>
                  </div>
                  <div className="order-detail-line__quantity">
                    <dt>Quantity</dt>
                    <dd>
                      {line.quantity > 1 ? (
                        <strong data-emphasis="high">{line.quantity}</strong>
                      ) : (
                        <span>{line.quantity}</span>
                      )}
                    </dd>
                  </div>
                </dl>
                {canRecordOutcome && (
                  <div className="order-detail-line__actions">
                    <button
                      type="button"
                      className="order-detail-line__picked"
                      aria-pressed={line.pickOutcome === 'picked'}
                      disabled={recordingLineId === line.id}
                      onClick={() => handlePicked(line.id)}
                    >
                      Picked
                    </button>
                  </div>
                )}
              </div>
            </article>
          ))}
        </section>
      )}
    </main>
  )
}
