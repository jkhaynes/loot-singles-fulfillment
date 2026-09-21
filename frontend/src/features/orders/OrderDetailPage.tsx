import { useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import {
  getOrderDetail,
  releaseOrder,
  getOrderLabel,
  pickNextOrder,
  NoOrdersAvailableError,
  claimOrder,
  OrderAlreadyClaimedError,
  EmployeeHasActiveClaimError,
  forceReleaseOrder,
  recordPicked,
  reportIssue,
  orderStatusLabel,
  pickingIssueTypeLabel,
  OrderNotFoundError,
} from './ordersApi'
import type { OrderDetail, ReportIssueRequest } from './ordersApi'
import { useAuth } from '../auth/AuthContext'
import { computeProgress, groupOrderLines } from './orderGrouping'
import { useIsPhone } from './useIsPhone'
import { FocusedPickView } from './FocusedPickView'
import { PickEnding } from './PickEnding'
import { PrintLabelButton } from '../labels/PrintLabelButton'
import type { LabelContent } from './ordersApi'
import { ReportIssueForm } from './ReportIssueForm'
import './OrderDetailPage.css'

type LoadState = 'loading' | 'loaded' | 'not-found' | 'error'

export function OrderDetailPage() {
  const { orderId } = useParams()
  const navigate = useNavigate()
  const { employee } = useAuth()
  const [order, setOrder] = useState<OrderDetail | null>(null)
  const [loadState, setLoadState] = useState<LoadState>('loading')
  const [actionError, setActionError] = useState<ReactNode | null>(null)
  const [isClaiming, setIsClaiming] = useState(false)
  /** Non-null once the pick has ended; the ending screen replaces the picking view. */
  const [ending, setEnding] = useState<LabelContent | null>(null)
  const isFinishing = useRef(false)
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

  /** Giving up an order without finishing it. Unchanged since feature 013. */
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

  /**
   * Finishing a pick ends on a screen rather than a navigation (PRD §22).
   *
   * Distinct from handleRelease even though both give up the claim: releasing abandons an
   * order, finishing completes one. Only the second produces a sleeve that needs labelling.
   *
   * The label is fetched before the claim is released, so a failure on either side leaves the
   * picker where they were rather than half-finished with nothing to print.
   */
  async function handleFinish() {
    // A ref rather than state: a double tap lands both calls before React has re-rendered, so
    // isReleasing would still read false for the second. Without this the second release
    // answered 409 — the first had already given the claim up — and the screen reported a
    // failure over a finish that had worked.
    if (!order || isFinishing.current) return
    isFinishing.current = true

    setIsReleasing(true)
    setActionError(null)
    try {
      const label = await getOrderLabel(order.orderId)
      await releaseOrder(order.orderId)
      setEnding(label)
      // Left set on success: the picking view is gone, and nothing should finish it again.
    } catch {
      isFinishing.current = false
      setActionError("Couldn't finish this order. Try refreshing the page.")
    } finally {
      setIsReleasing(false)
    }
  }

  /** The fast path off the ending screen: claim and open the next order, as Pick Next does. */
  async function handleNextOrder() {
    setActionError(null)
    try {
      const next = await pickNextOrder()
      setEnding(null)
      navigate(`/orders/${next.orderId}`)
    } catch (error) {
      if (error instanceof NoOrdersAvailableError) {
        navigate('/orders')
        return
      }
      setActionError("Couldn't start the next order. Try the dashboard.")
    }
  }

  /**
   * Claiming is an explicit act on the order, so viewing one is always safe (FR-023, FR-024).
   * The endpoint and its exclusivity have existed since feature 013 — nothing here re-implements
   * the rule, it only surfaces the answer the server gives (Constitution VI).
   */
  async function handleClaim() {
    if (!order) return

    setIsClaiming(true)
    setActionError(null)
    try {
      const claimed = await claimOrder(order.orderId)
      setOrder({
        ...order,
        status: claimed.status,
        claimedByEmployeeId: claimed.claimedByEmployeeId,
        claimedByEmployeeName: claimed.claimedByEmployeeName,
      })
    } catch (error) {
      if (error instanceof OrderAlreadyClaimedError) {
        setActionError(
          error.claimedByEmployeeName !== null
            ? `${error.claimedByEmployeeName} claimed this order first.`
            : 'Someone else claimed this order first.',
        )
      } else if (error instanceof EmployeeHasActiveClaimError) {
        // A dead end otherwise: the picker is told no, with nowhere to go (FR-027).
        setActionError(
          error.claimedOrderId !== null ? (
            <>
              You already have <Link to={`/orders/${error.claimedOrderId}`}>an order claimed</Link>.
              Finish or release it before claiming another.
            </>
          ) : (
            'You already have an order claimed. Finish or release it before claiming another.'
          ),
        )
      } else {
        setActionError("Couldn't claim this order. Try refreshing the page.")
      }
    } finally {
      setIsClaiming(false)
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
  // Offered only when the order is free: claiming one someone else holds is refused by the
  // server anyway, and offering a button known to fail is the dead end FR-027 removes.
  // Whether picking has produced anything to print. Deliberately not "is the order Picked":
  // a held order is NeedsAttention and still has a label (FR-009).
  const hasStartedPicking = order !== null && order.lines.some((line) => line.pickOutcome !== null)

  // A packed order has physically left, so there is nothing a claim could accomplish — unlike
  // a picked one, which stays claimable on purpose so a picker can revise lines before the
  // sleeve is sealed (feature 015). The server enforces this too; hiding the button only keeps
  // the app from offering something that cannot work (FR-045).
  const canClaim =
    order !== null &&
    employee !== null &&
    order.claimedByEmployeeId === null &&
    order.status !== 'packed'
  const confirmedLineCount =
    order?.lines.filter((line) => line.pickOutcome === 'picked').length ?? 0
  // Set-aware picking (PRD §13): one group per storage box, ordered for the walk.
  const setGroups = useMemo(() => groupOrderLines(order?.lines ?? []), [order?.lines])
  const progress = useMemo(() => computeProgress(setGroups, null), [setGroups])
  // A phone gets the card view, a desktop the whole order. Neither offers the other (PRD §8,
  // Product Owner decision 2026-09-21).
  const isPhone = useIsPhone()
  const blockedReason =
    order !== null && !canRecordOutcome
      ? order.claimedByEmployeeName === null
        ? 'This order is not claimed, so picks cannot be recorded.'
        : `${order.claimedByEmployeeName} is picking this order.`
      : null

  // In the focused view the screen is a card and one action (FR-029): the order's own title,
  // status, second progress line and the Release / Browse / Dashboard links took 31% of a
  // 440x956 screen and pushed the record button below the fold.
  const isFocused = isPhone && loadState === 'loaded'

  return (
    <main className={`order-detail-page${isFocused ? ' order-detail-page--focused' : ''}`}>
      {/* Once the pick has ended there is no header of either kind. The claim has been released,
          so a Release button would answer 409 not_your_claim; the progress line describes work
          that is over; and the ending screen has its own print action. An earlier build hid the
          phone header by making isFocused false, which swapped in the desktop header and its
          Release button instead of removing anything (PRD §22). */}
      {ending !== null ? null : isFocused ? (
        <header className="order-detail-bar">
          {/* One exit, on every card. Until this replaced the view toggle a picker was stuck on
              an order until they reached the end of it. */}
          <Link to="/" className="order-detail-bar__out">
            <span aria-hidden="true">‹</span> Dashboard
          </Link>
          <span className="order-detail-bar__code">
            {order ? order.tcgplayerOrderId.split('-').at(-1) : ''}
          </span>
          {/* Labelled, because the band below shows a position and this is a count — two
              "X of Y" numbers on one screen would otherwise read as the same kind of thing. */}
          {/* Claiming lives in the dock beside the card, because on an unclaimed order it is
              THE action. Releasing is the opposite: rare, and reached deliberately, so a small
              control up here is the right weight. Without it a phone had no way to let go of an
              order at all. Progress is not repeated here — the card carries it. */}
          {canRelease && (
            <button
              type="button"
              className="order-detail-bar__release"
              onClick={handleRelease}
              disabled={isReleasing}
            >
              {isReleasing ? 'Releasing…' : 'Release'}
            </button>
          )}

          {/* A label that jammed, misprinted or fell off needs replacing without re-picking
              the order. Offered once picking has produced something to print, held orders
              included — theirs is the label most likely to be needed twice (FR-016). */}
          {hasStartedPicking && (
            <PrintLabelButton orderId={order!.orderId} className="order-detail-bar__release" />
          )}
        </header>
      ) : (
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
            {canClaim && (
              <button
                type="button"
                className="order-detail-navigation__claim"
                onClick={handleClaim}
                disabled={isClaiming}
              >
                {isClaiming ? 'Claiming…' : 'Claim'}
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
      )}

      {actionError && isFocused && (
        <p role="alert" className="order-detail-bar__error">
          {actionError}
        </p>
      )}

      {ending !== null ? (
        // The pick has ended. The picking view is gone deliberately: there is nothing left to
        // record here, and the claim has already been released (PRD §22).
        <PickEnding
          label={ending}
          onNextOrder={handleNextOrder}
          onBackToDashboard={() => navigate('/')}
        />
      ) : loadState === 'loading' ? (
        <p className="order-detail-state">Loading order…</p>
      ) : loadState === 'not-found' ? (
        <p role="alert" className="order-detail-state order-detail-state--error">
          Order not found. It may have been removed or the address may be incorrect.
        </p>
      ) : loadState === 'error' ? (
        <p role="alert" className="order-detail-state order-detail-state--error">
          Couldn't load order. Try refreshing the page.
        </p>
      ) : isPhone ? (
        <FocusedPickView
          groups={setGroups}
          canRecordOutcome={canRecordOutcome}
          blockedReason={blockedReason}
          recordingLineId={recordingLineId}
          onPicked={handlePicked}
          onReportIssue={handleReportIssue}
          // Finishing releases the claim — one claim per employee is enforced server-side, so a
          // picker still holding a finished order could never start another. Someone who never
          // held it is only closing a screen: releasing there asks the server to give up a claim
          // they do not have, which simply fails. Feature 017's label print plugs in here.
          onCompleted={canRelease ? handleFinish : () => navigate('/orders')}
          canClaim={canClaim}
          isClaiming={isClaiming}
          onClaim={handleClaim}
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
