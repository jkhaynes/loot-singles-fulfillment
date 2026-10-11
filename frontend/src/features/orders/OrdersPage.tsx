import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import {
  getOrders,
  claimOrder,
  deleteOrder,
  getDeletionSummary,
  deleteAllOrders,
  orderStatusLabel,
  OrderAlreadyClaimedError,
  OrderAlreadyDeletedError,
  OrderClaimedBeforeError,
  EmployeeHasActiveClaimError,
} from './ordersApi'
import type { DeletionSummary, OrderListItem } from './ordersApi'
import { useAuth } from '../auth/AuthContext'
import { ConfirmationDialog } from '../../components/ConfirmationDialog'
import { OrderRowMenu } from './OrderRowMenu'
import './OrdersPage.css'

const importTimeFormatter = new Intl.DateTimeFormat('en-US', {
  dateStyle: 'medium',
  timeStyle: 'short',
})

function countOf(count: number, singular: string, plural: string): string {
  return `${count} ${count === 1 ? singular : plural}`
}

/** The delete-all warning line: how many deletable orders are claimed or have an open issue (FR-014). */
function deleteAllWarning(summary: DeletionSummary): string {
  const parts: string[] = []
  if (summary.claimedCount > 0) {
    parts.push(`${summary.claimedCount} ${summary.claimedCount === 1 ? 'is' : 'are'} claimed.`)
  }
  if (summary.withUnresolvedIssueCount > 0) {
    parts.push(
      summary.withUnresolvedIssueCount === 1
        ? '1 has an unresolved picking issue.'
        : `${summary.withUnresolvedIssueCount} have unresolved picking issues.`,
    )
  }
  return parts.join(' ')
}

export function OrdersPage() {
  const navigate = useNavigate()
  const [orders, setOrders] = useState<OrderListItem[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [hasError, setHasError] = useState(false)
  const [claimingOrderId, setClaimingOrderId] = useState<number | null>(null)
  const [claimError, setClaimError] = useState<ReactNode | null>(null)
  const { employee } = useAuth()
  const isManagerAdmin = employee?.role === 'ManagerAdmin'
  const [orderToDelete, setOrderToDelete] = useState<OrderListItem | null>(null)
  const [deletionSummary, setDeletionSummary] = useState<DeletionSummary | null>(null)
  const [isDeleting, setIsDeleting] = useState(false)
  const [deletionNotice, setDeletionNotice] = useState<string | null>(null)
  const [deletionError, setDeletionError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false

    getOrders()
      .then((result) => {
        if (!cancelled) setOrders(result)
      })
      .catch(() => {
        if (!cancelled) setHasError(true)
      })
      .finally(() => {
        if (!cancelled) setIsLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [])

  async function handleClaim(orderId: number) {
    setClaimingOrderId(orderId)
    setClaimError(null)
    try {
      await claimOrder(orderId)
      navigate(`/orders/${orderId}`)
    } catch (error) {
      if (error instanceof OrderAlreadyClaimedError) {
        setClaimError(
          error.claimedByEmployeeName
            ? `This order is already claimed by ${error.claimedByEmployeeName}.`
            : 'This order is already claimed.',
        )
      } else if (error instanceof EmployeeHasActiveClaimError) {
        setClaimError(
          error.claimedOrderId !== null ? (
            <>
              You already have <Link to={`/orders/${error.claimedOrderId}`}>an order claimed</Link>{' '}
              — release it before claiming another.
            </>
          ) : (
            'You already have an order claimed. Release it before claiming another.'
          ),
        )
      } else {
        setClaimError("Couldn't claim this order. Try again.")
      }
      setClaimingOrderId(null)
    }
  }

  function removeOrder(orderId: number) {
    setOrders((current) => current.filter((item) => item.orderId !== orderId))
  }

  function clearDeletionMessages() {
    setDeletionNotice(null)
    setDeletionError(null)
  }

  async function confirmDeleteOrder() {
    if (!orderToDelete || isDeleting) return
    const { orderId } = orderToDelete
    setIsDeleting(true)
    try {
      await deleteOrder(orderId)
      removeOrder(orderId)
    } catch (error) {
      if (error instanceof OrderAlreadyDeletedError) {
        removeOrder(orderId)
        setDeletionNotice('That order was already deleted.')
      } else if (error instanceof OrderClaimedBeforeError) {
        setDeletionError("Someone claimed this order, so it can't be deleted.")
      } else {
        setDeletionError("Couldn't delete this order. Try again.")
      }
    } finally {
      setIsDeleting(false)
      setOrderToDelete(null)
    }
  }

  async function startDeleteAll() {
    clearDeletionMessages()
    try {
      setDeletionSummary(await getDeletionSummary())
    } catch {
      setDeletionError("Couldn't check which orders can be deleted. Try again.")
    }
  }

  async function confirmDeleteAll() {
    if (!deletionSummary || isDeleting) return
    setIsDeleting(true)
    try {
      const result = await deleteAllOrders(deletionSummary.asOf)
      const deleted = `${countOf(result.deletedCount, 'order', 'orders')} deleted`
      setDeletionNotice(result.keptCount > 0 ? `${deleted} · ${result.keptCount} kept` : deleted)
      try {
        setOrders(await getOrders())
      } catch {
        setHasError(true)
      }
    } catch {
      setDeletionError("Couldn't delete the orders. Try again.")
    } finally {
      setIsDeleting(false)
      setDeletionSummary(null)
    }
  }

  return (
    <main className="orders-page">
      <header className="orders-header">
        <div>
          <h1>Browse Orders</h1>
          <p>Review orders that have been imported into the fulfillment queue.</p>
          {claimError && (
            <p role="alert" className="orders-header__error">
              {claimError}
            </p>
          )}
          {deletionError && (
            <p role="alert" className="orders-header__error">
              {deletionError}
            </p>
          )}
          {deletionNotice && (
            <p role="status" className="orders-header__notice">
              {deletionNotice}
            </p>
          )}
        </div>
        <nav className="orders-navigation" aria-label="Order navigation">
          <Link to="/">Dashboard</Link>
          <Link to="/import" data-emphasis="primary">
            Import Orders
          </Link>
          {isManagerAdmin && (
            <button type="button" className="orders-navigation__quiet" onClick={startDeleteAll}>
              Delete orders…
            </button>
          )}
        </nav>
      </header>

      {isLoading ? (
        <p className="orders-state">Loading orders…</p>
      ) : hasError ? (
        <p role="alert" className="orders-state orders-state--error">
          Couldn't load imported orders. Try refreshing the page.
        </p>
      ) : orders.length === 0 ? (
        <p className="orders-state">No imported orders yet.</p>
      ) : (
        <section className="orders-list" aria-label="Imported orders">
          <div className="orders-list__head" aria-hidden="true">
            <span>Order</span>
            <span>Status</span>
            <span>Imported</span>
            <span>Actions</span>
          </div>
          {orders.map((order) => (
            <article
              key={order.orderId}
              className="order-list-item"
              aria-label={`Order ${order.tcgplayerOrderId}`}
            >
              <div>
                <span className="order-list-item__label">Order</span>
                <Link className="order-list-item__link" to={`/orders/${order.orderId}`}>
                  <strong>{order.tcgplayerOrderId}</strong>
                </Link>
              </div>
              <div>
                <span className="order-list-item__label">Status</span>
                <span className="order-list-item__status">
                  {/* A claimed order names its claimant whatever its status: a Picked order keeps
                      its claim, so the status alone would hide who is holding it. */}
                  {order.claimedByEmployeeName
                    ? `${orderStatusLabel(order.status)} · Picking by ${order.claimedByEmployeeName}`
                    : orderStatusLabel(order.status)}
                </span>
              </div>
              <div>
                <span className="order-list-item__label">Imported</span>
                <time dateTime={order.importedAt}>
                  {importTimeFormatter.format(new Date(order.importedAt))}
                </time>
              </div>
              <div>
                <span className="order-list-item__label">Actions</span>
                {order.claimedByEmployeeId === null && (
                  <button
                    type="button"
                    className="order-list-item__claim-action"
                    onClick={() => handleClaim(order.orderId)}
                    disabled={claimingOrderId === order.orderId}
                  >
                    {claimingOrderId === order.orderId ? 'Claiming…' : 'Claim'}
                  </button>
                )}
                {isManagerAdmin && (
                  <OrderRowMenu
                    order={order}
                    onDelete={() => {
                      clearDeletionMessages()
                      setOrderToDelete(order)
                    }}
                  />
                )}
              </div>
            </article>
          ))}
        </section>
      )}

      {orderToDelete && (
        <ConfirmationDialog
          title={`Delete order ${orderToDelete.tcgplayerOrderId}?`}
          keepLabel="Keep order"
          confirmLabel="Delete order"
          destructive
          busy={isDeleting}
          onKeep={() => setOrderToDelete(null)}
          onConfirm={confirmDeleteOrder}
        >
          {orderToDelete.claimedByEmployeeName && (
            <p>{orderToDelete.claimedByEmployeeName} is picking this order.</p>
          )}
          {orderToDelete.status === 'needsAttention' && (
            <p>This order has an unresolved picking issue.</p>
          )}
        </ConfirmationDialog>
      )}

      {deletionSummary && (
        <ConfirmationDialog
          title={`Delete ${countOf(deletionSummary.deletableCount, 'order', 'orders')}?`}
          keepLabel="Keep orders"
          confirmLabel="Delete orders"
          destructive
          confirmWord="DELETE"
          busy={isDeleting}
          onKeep={() => setDeletionSummary(null)}
          onConfirm={confirmDeleteAll}
        >
          {deletionSummary.keptCount > 0 && (
            <p>
              {countOf(deletionSummary.keptCount, 'order', 'orders')} will be kept because
              they&apos;ve been claimed.
            </p>
          )}
          {deleteAllWarning(deletionSummary) && <p>{deleteAllWarning(deletionSummary)}</p>}
        </ConfirmationDialog>
      )}
    </main>
  )
}
