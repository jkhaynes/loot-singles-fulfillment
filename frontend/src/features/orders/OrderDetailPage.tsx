import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { getOrderDetail, OrderNotFoundError } from './ordersApi'
import type { OrderDetail } from './ordersApi'
import './OrderDetailPage.css'

type LoadState = 'loading' | 'loaded' | 'not-found' | 'error'

export function OrderDetailPage() {
  const { orderId } = useParams()
  const [order, setOrder] = useState<OrderDetail | null>(null)
  const [loadState, setLoadState] = useState<LoadState>('loading')

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

  return (
    <main className="order-detail-page">
      <header className="order-detail-header">
        <div>
          <p className="order-detail-header__eyebrow">Order picking detail</p>
          <h1>{order ? `Order ${order.tcgplayerOrderId}` : 'Order detail'}</h1>
        </div>
        <nav className="order-detail-navigation" aria-label="Order detail navigation">
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
          {order?.lines.map((line, index) => (
            <article
              key={`${line.productName}-${line.set}-${line.condition}-${index}`}
              className="order-detail-line"
              aria-label={`Product ${line.productName}`}
            >
              <div className="order-detail-line__placeholder" aria-label="Card image unavailable">
                <span aria-hidden="true">No image</span>
              </div>
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
              </div>
            </article>
          ))}
        </section>
      )}
    </main>
  )
}
