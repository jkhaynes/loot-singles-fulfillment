import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import {
  getOrders,
  claimOrder,
  OrderAlreadyClaimedError,
  EmployeeHasActiveClaimError,
} from './ordersApi'
import type { OrderListItem } from './ordersApi'
import './OrdersPage.css'

const importTimeFormatter = new Intl.DateTimeFormat('en-US', {
  dateStyle: 'medium',
  timeStyle: 'short',
})

export function OrdersPage() {
  const navigate = useNavigate()
  const [orders, setOrders] = useState<OrderListItem[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [hasError, setHasError] = useState(false)
  const [claimingOrderId, setClaimingOrderId] = useState<number | null>(null)
  const [claimError, setClaimError] = useState<string | null>(null)

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
        setClaimError('You already have an order claimed. Release it before claiming another.')
      } else {
        setClaimError("Couldn't claim this order. Try again.")
      }
      setClaimingOrderId(null)
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
        </div>
        <nav className="orders-navigation" aria-label="Order navigation">
          <Link to="/">Dashboard</Link>
          <Link to="/import" data-emphasis="primary">
            Import Orders
          </Link>
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
                  {order.claimedByEmployeeName
                    ? `In Progress · Picking by ${order.claimedByEmployeeName}`
                    : order.status}
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
              </div>
            </article>
          ))}
        </section>
      )}
    </main>
  )
}
