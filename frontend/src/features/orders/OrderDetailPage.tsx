import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
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
import { computeProgress, groupOrderLines } from './orderGrouping'
import { useViewPreference } from './useViewPreference'
import { FocusedPickView } from './FocusedPickView'
import { ReportIssueForm } from './ReportIssueForm'
import './OrderDetailPage.css'

type LoadState = 'loading' | 'loaded' | 'not-found' | 'error'

export function OrderDetailPage() {
  const { orderId } = useParams()
  const navigate = useNavigate()
  const { employee } = useAuth()
  const [order, setOrder] = useState<OrderDetail | null>(null)
  const [loadState, setLoadState] = useState<LoadState>('loading')
  const [actionError, setActionError] = useState<string | null>(null)
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
    setActionError(null)
    try {
      await releaseOrder(order.orderId)
      // Back to the list so the picker can claim the next order (PO decision 2026-09-19).
      navigate('/orders')
    } catch {
      setActionError("Couldn't release this order. Try refreshing the page.")
    } finally {
      setIsReleasing(false)
    }
  }

  async function handleForceRelease() {
    if (!order) return

    setIsForceReleasing(true)
    setActionError(null)
    try {
      const updated = await forceReleaseOrder(order.orderId)
      setOrder({
        ...order,
        status: updated.status,
        claimedByEmployeeId: updated.claimedByEmployeeId,
        claimedByEmployeeName: updated.claimedByEmployeeName,
      })
    } catch {
      setActionError("Couldn't force-release this order. Try refreshing the page.")
    } finally {
      setIsForceReleasing(false)
    }
  }

  /**
   * Card images are resolved only when the order is opened — a recorded outcome cannot change
   * them, so the server leaves `imageUrl` null on these responses rather than re-resolving every
   * line against the catalog providers on every tap. Carry over the ones already loaded.
   */
  function withLoadedImages(updated: OrderDetail, previous: OrderDetail): OrderDetail {
    const imageUrlsByLineId = new Map(previous.lines.map((line) => [line.id, line.imageUrl]))
    return {
      ...updated,
      lines: updated.lines.map((line) => ({
        ...line,
        imageUrl: line.imageUrl ?? imageUrlsByLineId.get(line.id) ?? null,
      })),
    }
  }

  async function handlePicked(lineId: number) {
    if (!order) return

    setRecordingLineId(lineId)
    setActionError(null)
    try {
      setOrder(withLoadedImages(await recordPicked(order.orderId, lineId), order))
      setIssueFormLineId(null)
    } catch {
      setActionError("Couldn't record that pick. Try refreshing the page.")
    } finally {
      setRecordingLineId(null)
    }
  }

  async function handleReportIssue(lineId: number, request: ReportIssueRequest) {
    if (!order) return

    setRecordingLineId(lineId)
    setActionError(null)
    try {
      setOrder(withLoadedImages(await reportIssue(order.orderId, lineId, request), order))
      setIssueFormLineId(null)
    } catch {
      setActionError("Couldn't report that issue. Try refreshing the page.")
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
  // Set-aware picking (PRD §13): one group per storage box, ordered for the walk.
  const setGroups = useMemo(() => groupOrderLines(order?.lines ?? []), [order?.lines])
  const progress = useMemo(() => computeProgress(setGroups, null), [setGroups])
  const { view, choose } = useViewPreference()
  const blockedReason =
    order !== null && !canRecordOutcome
      ? order.claimedByEmployeeName === null
        ? 'This order is not claimed, so picks cannot be recorded.'
        : `${order.claimedByEmployeeName} is picking this order.`
      : null

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
          {order && (
            /* Physical cards as well as products: a line of three is three cards to pull,
               not one (PRD §18, FR-021). */
            <p className="order-detail-header__cards">
              {`${progress.accountedCards} of ${progress.totalCards} cards accounted for`}
            </p>
          )}
          {actionError && (
            <p role="alert" className="order-detail-header__error">
              {actionError}
            </p>
          )}
        </div>
        <nav className="order-detail-navigation" aria-label="Order detail navigation">
          {order && (
            <button
              type="button"
              className="order-detail-navigation__view"
              onClick={() => choose(view === 'focused' ? 'list' : 'focused')}
            >
              {/* Deliberately avoids the words pick/claim/complete: this switches how the order
                  is displayed and records nothing, so it must not read like an action. */}
              {view === 'focused' ? 'Whole order' : 'One card at a time'}
            </button>
          )}
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
      ) : view === 'focused' ? (
        <FocusedPickView
          groups={setGroups}
          canRecordOutcome={canRecordOutcome}
          blockedReason={blockedReason}
          recordingLineId={recordingLineId}
          onPicked={handlePicked}
          onReportIssue={handleReportIssue}
          // Feature 017's pick completion screen plugs in here. Until it exists, running off
          // the end of the order shows the whole order rather than a dead end.
          onOrderEnd={() => choose('list')}
        />
      ) : (
        <section className="order-detail-lines" aria-label="Products to pick">
          {setGroups.map((group) => (
            <div
              key={`${group.game}\u0000${group.setName}`}
              className="order-detail-set"
              role="group"
              aria-label={`${group.game} · ${group.setName}`}
            >
              <header className="order-detail-set__header">
                <p className="order-detail-set__game">{group.game}</p>
                <h2 className="order-detail-set__name">{group.setName}</h2>
                <p className="order-detail-set__counts">
                  {`${group.productCount} ${group.productCount === 1 ? 'product' : 'products'}`}
                  {' · '}
                  <strong data-emphasis={group.cardCount > group.productCount ? 'high' : undefined}>
                    {`${group.cardCount} ${group.cardCount === 1 ? 'card' : 'cards'}`}
                  </strong>
                </p>
              </header>
              {group.lines.map((line) => (
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
                    <div
                      className="order-detail-line__placeholder"
                      aria-label="Card image unavailable"
                    >
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
            </div>
          ))}
        </section>
      )}
    </main>
  )
}
