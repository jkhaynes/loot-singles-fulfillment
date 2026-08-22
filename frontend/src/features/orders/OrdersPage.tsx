import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { getOrders } from './ordersApi'
import type { OrderListItem } from './ordersApi'
import './OrdersPage.css'

const importTimeFormatter = new Intl.DateTimeFormat('en-US', {
  dateStyle: 'medium',
  timeStyle: 'short',
})

export function OrdersPage() {
  const [orders, setOrders] = useState<OrderListItem[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [hasError, setHasError] = useState(false)

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

  return (
    <main className="orders-page">
      <header className="orders-header">
        <div>
          <h1>Browse Orders</h1>
          <p>Review orders that have been imported into the fulfillment queue.</p>
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
          </div>
          {orders.map((order) => (
            <article
              key={order.orderId}
              className="order-list-item"
              aria-label={`Order ${order.tcgplayerOrderId}`}
            >
              <div>
                <span className="order-list-item__label">Order</span>
                <strong>{order.tcgplayerOrderId}</strong>
              </div>
              <div>
                <span className="order-list-item__label">Status</span>
                <span className="order-list-item__status">{order.status}</span>
              </div>
              <div>
                <span className="order-list-item__label">Imported</span>
                <time dateTime={order.importedAt}>
                  {importTimeFormatter.format(new Date(order.importedAt))}
                </time>
              </div>
            </article>
          ))}
        </section>
      )}
    </main>
  )
}
