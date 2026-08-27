import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import type { AuthenticatedEmployee } from '../auth/authApi'
import { getDashboard } from './dashboardApi'
import type { DashboardData } from './dashboardApi'
import {
  pickNextOrder,
  NoOrdersAvailableError,
  EmployeeHasActiveClaimError,
} from '../orders/ordersApi'
import { AlertTriangleIcon, CheckCircleIcon, ClockIcon, ShoppingBagIcon } from './icons'
import './DashboardPage.css'

export interface DashboardPageProps {
  employee: AuthenticatedEmployee
  onLogout: () => void | Promise<void>
}

function greetingForHour(hour: number): string {
  if (hour < 12) return 'Good morning'
  if (hour < 18) return 'Good afternoon'
  return 'Good evening'
}

export function DashboardPage({ employee, onLogout }: DashboardPageProps) {
  const navigate = useNavigate()
  const [data, setData] = useState<DashboardData | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [hasError, setHasError] = useState(false)
  const [isPickingNext, setIsPickingNext] = useState(false)
  const [pickNextError, setPickNextError] = useState<ReactNode | null>(null)

  useEffect(() => {
    let cancelled = false

    getDashboard()
      .then((result) => {
        if (!cancelled) {
          setData(result)
        }
      })
      .catch(() => {
        if (!cancelled) {
          setHasError(true)
        }
      })
      .finally(() => {
        if (!cancelled) {
          setIsLoading(false)
        }
      })

    return () => {
      cancelled = true
    }
  }, [])

  const firstName = employee.displayName.split(' ')[0]
  const readyCount = data?.ready.count ?? 0

  async function handlePickNext() {
    setIsPickingNext(true)
    setPickNextError(null)
    try {
      const claimed = await pickNextOrder()
      navigate(`/orders/${claimed.orderId}`)
    } catch (error) {
      if (error instanceof NoOrdersAvailableError) {
        setPickNextError('No orders are currently available to pick.')
      } else if (error instanceof EmployeeHasActiveClaimError) {
        setPickNextError(
          error.claimedOrderId !== null ? (
            <>
              You already have <Link to={`/orders/${error.claimedOrderId}`}>an order claimed</Link>{' '}
              — release it before picking another.
            </>
          ) : (
            'You already have an order claimed. Release it before picking another.'
          ),
        )
      } else {
        setPickNextError("Couldn't pick the next order. Try again.")
      }
    } finally {
      setIsPickingNext(false)
    }
  }

  return (
    <div className="dashboard-page">
      <header className="dashboard-header">
        <div>
          <h1 className="dashboard-header__greeting">
            {greetingForHour(new Date().getHours())}, {firstName}
          </h1>
          <p className="dashboard-header__subtitle">Ready to pick some orders?</p>
          {pickNextError && (
            <p role="alert" className="dashboard-header__error">
              {pickNextError}
            </p>
          )}
        </div>
        <div className="dashboard-header__actions">
          <button
            type="button"
            className="dashboard-pick-next-action"
            onClick={handlePickNext}
            disabled={isPickingNext}
          >
            {isPickingNext ? 'Picking…' : 'Pick Next Order'}
          </button>
          <Link to="/orders" className="dashboard-orders-action">
            Browse Orders
          </Link>
          <Link to="/import" className="dashboard-import-action">
            Import Orders
          </Link>
          {employee.role === 'ManagerAdmin' && (
            <Link to="/admin" className="dashboard-admin-action">
              Manage Employees
            </Link>
          )}
          <button type="button" className="dashboard-logout" onClick={() => onLogout()}>
            Log out
          </button>
        </div>
      </header>

      <div className="dashboard-stats">
        <article className="dashboard-stat">
          <span className="dashboard-stat__icon" data-status="ready">
            <ShoppingBagIcon />
          </span>
          <div>
            <p className="dashboard-stat__label">Ready to Pick</p>
            <p className="dashboard-stat__value">{isLoading ? '…' : hasError ? '—' : readyCount}</p>
          </div>
        </article>

        <article className="dashboard-stat">
          <span className="dashboard-stat__icon" data-status="unavailable">
            <ClockIcon />
          </span>
          <div>
            <p className="dashboard-stat__label">In Progress</p>
            <p className="dashboard-stat__value dashboard-stat__value--unavailable">
              Not yet available
            </p>
          </div>
        </article>

        <article className="dashboard-stat">
          <span className="dashboard-stat__icon" data-status="unavailable">
            <AlertTriangleIcon />
          </span>
          <div>
            <p className="dashboard-stat__label">Needs Attention</p>
            <p className="dashboard-stat__value dashboard-stat__value--unavailable">
              Not yet available
            </p>
          </div>
        </article>

        <article className="dashboard-stat">
          <span className="dashboard-stat__icon" data-status="unavailable">
            <CheckCircleIcon />
          </span>
          <div>
            <p className="dashboard-stat__label">Picked</p>
            <p className="dashboard-stat__value dashboard-stat__value--unavailable">
              Not yet available
            </p>
          </div>
        </article>
      </div>

      <section className="dashboard-panel">
        <h2 className="dashboard-panel__title">Available Orders</h2>
        {isLoading ? (
          <p className="dashboard-panel__empty">Loading…</p>
        ) : hasError ? (
          <p role="alert" className="dashboard-panel__error">
            Couldn't load orders. Try refreshing the page.
          </p>
        ) : readyCount > 0 ? (
          <div className="dashboard-order-table-wrapper">
            <table className="dashboard-order-table">
              <thead>
                <tr>
                  <th scope="col">Order #</th>
                  <th scope="col">Products</th>
                  <th scope="col">Quantity</th>
                </tr>
              </thead>
              <tbody>
                {data!.ready.orders.map((order) => (
                  <tr key={order.orderId}>
                    <td>
                      <Link className="dashboard-order-table__link" to={`/orders/${order.orderId}`}>
                        {order.tcgplayerOrderId}
                      </Link>
                    </td>
                    <td>{order.productCount}</td>
                    <td data-emphasis={order.totalQuantity > 1 ? 'high' : undefined}>
                      {order.totalQuantity}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p className="dashboard-panel__empty">No orders ready to pick right now.</p>
        )}
      </section>
    </div>
  )
}
