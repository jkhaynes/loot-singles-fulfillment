import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import {
  getOrderDetail,
  releaseOrder,
  forceReleaseOrder,
  recordPicked,
  reportIssue,
  orderStatusLabel,
  pickingIssueTypes,
  pickingIssueTypeLabel,
  OrderNotFoundError,
} from './ordersApi'
import type { OrderDetail, PickingIssueType, ReportIssueRequest } from './ordersApi'
import { useAuth } from '../auth/AuthContext'
import './OrderDetailPage.css'

type LoadState = 'loading' | 'loaded' | 'not-found' | 'error'

function ReportIssueForm({
  lineId,
  isSubmitting,
  onCancel,
  onSubmit,
}: {
  lineId: number
  isSubmitting: boolean
  onCancel: () => void
  onSubmit: (request: ReportIssueRequest) => void
}) {
  const [issueType, setIssueType] = useState<PickingIssueType>(pickingIssueTypes[0].value)
  const [requiredQuantity, setRequiredQuantity] = useState('')
  const [foundQuantity, setFoundQuantity] = useState('')
  const [note, setNote] = useState('')

  function toQuantity(value: string): number | null {
    const trimmed = value.trim()
    return trimmed === '' ? null : Number(trimmed)
  }

  return (
    <form
      className="order-detail-line__issue-form"
      onSubmit={(event) => {
        event.preventDefault()
        onSubmit({
          issueType,
          requiredQuantity: toQuantity(requiredQuantity),
          foundQuantity: toQuantity(foundQuantity),
          note: note.trim() === '' ? null : note.trim(),
        })
      }}
    >
      <div>
        <label htmlFor={`issue-type-${lineId}`}>Issue type</label>
        <select
          id={`issue-type-${lineId}`}
          value={issueType}
          onChange={(event) => setIssueType(event.target.value as PickingIssueType)}
        >
          {pickingIssueTypes.map((type) => (
            <option key={type.value} value={type.value}>
              {type.label}
            </option>
          ))}
        </select>
      </div>
      <div className="order-detail-line__issue-quantities">
        <div>
          <label htmlFor={`required-quantity-${lineId}`}>Quantity required</label>
          <input
            id={`required-quantity-${lineId}`}
            type="number"
            min="0"
            inputMode="numeric"
            value={requiredQuantity}
            onChange={(event) => setRequiredQuantity(event.target.value)}
          />
        </div>
        <div>
          <label htmlFor={`found-quantity-${lineId}`}>Quantity found</label>
          <input
            id={`found-quantity-${lineId}`}
            type="number"
            min="0"
            inputMode="numeric"
            value={foundQuantity}
            onChange={(event) => setFoundQuantity(event.target.value)}
          />
        </div>
      </div>
      <div>
        <label htmlFor={`issue-note-${lineId}`}>Note (optional)</label>
        <textarea
          id={`issue-note-${lineId}`}
          rows={2}
          maxLength={500}
          value={note}
          onChange={(event) => setNote(event.target.value)}
        />
      </div>
      <div className="order-detail-line__issue-actions">
        <button type="submit" disabled={isSubmitting}>
          Submit Issue
        </button>
        <button type="button" onClick={onCancel} disabled={isSubmitting}>
          Cancel
        </button>
      </div>
    </form>
  )
}

export function OrderDetailPage() {
  const { orderId } = useParams()
  const { employee } = useAuth()
  const [order, setOrder] = useState<OrderDetail | null>(null)
  const [loadState, setLoadState] = useState<LoadState>('loading')
  const [releaseError, setReleaseError] = useState<string | null>(null)
  const [isReleasing, setIsReleasing] = useState(false)
  const [isForceReleasing, setIsForceReleasing] = useState(false)
  const [recordingLineId, setRecordingLineId] = useState<number | null>(null)
  const [issueFormLineId, setIssueFormLineId] = useState<number | null>(null)

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
      setIssueFormLineId(null)
    } catch {
      setReleaseError("Couldn't record that pick. Try refreshing the page.")
    } finally {
      setRecordingLineId(null)
    }
  }

  async function handleReportIssue(lineId: number, request: ReportIssueRequest) {
    if (!order) return

    setRecordingLineId(lineId)
    setReleaseError(null)
    try {
      setOrder(await reportIssue(order.orderId, lineId, request))
      setIssueFormLineId(null)
    } catch {
      setReleaseError("Couldn't report that issue. Try refreshing the page.")
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
                {line.currentIssue && (
                  <p className="order-detail-line__issue" role="status">
                    <strong data-emphasis="high">
                      {pickingIssueTypeLabel(line.currentIssue.issueType)}
                    </strong>
                    {line.currentIssue.requiredQuantity !== null &&
                      line.currentIssue.foundQuantity !== null &&
                      ` · found ${line.currentIssue.foundQuantity} of ${line.currentIssue.requiredQuantity}`}
                    {line.currentIssue.note && ` · ${line.currentIssue.note}`}
                    {line.currentIssue.reportedByEmployeeName &&
                      ` · reported by ${line.currentIssue.reportedByEmployeeName}`}
                  </p>
                )}
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
                    {issueFormLineId !== line.id && (
                      <button
                        type="button"
                        className="order-detail-line__report"
                        disabled={recordingLineId === line.id}
                        onClick={() => setIssueFormLineId(line.id)}
                      >
                        Report Issue
                      </button>
                    )}
                  </div>
                )}
                {canRecordOutcome && issueFormLineId === line.id && (
                  <ReportIssueForm
                    lineId={line.id}
                    isSubmitting={recordingLineId === line.id}
                    onCancel={() => setIssueFormLineId(null)}
                    onSubmit={(request) => handleReportIssue(line.id, request)}
                  />
                )}
              </div>
            </article>
          ))}
        </section>
      )}
    </main>
  )
}
