import { useEffect, useRef, useState } from 'react'
import type { OrderListItem } from './ordersApi'

interface OrderRowMenuProps {
  order: OrderListItem
  onDelete: () => void
}

const claimedBeforeMessage =
  "Can't delete on production: this order has been claimed, so its pick history is kept."

/** The ⋯ menu on a Browse Orders row, shown only to Manager/Admins (FR-021). */
export function OrderRowMenu({ order, onDelete }: OrderRowMenuProps) {
  const [open, setOpen] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return

    function handlePointerDown(event: MouseEvent) {
      if (!containerRef.current?.contains(event.target as Node)) setOpen(false)
    }
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') setOpen(false)
    }

    document.addEventListener('mousedown', handlePointerDown)
    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('mousedown', handlePointerDown)
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [open])

  return (
    <div className="order-row-menu" ref={containerRef}>
      <button
        type="button"
        className="order-row-menu__toggle"
        aria-label={`Actions for order ${order.tcgplayerOrderId}`}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((isOpen) => !isOpen)}
      >
        ⋯
      </button>
      {open && (
        <div className="order-row-menu__popover" role="menu">
          {order.canDelete ? (
            <button
              type="button"
              role="menuitem"
              className="order-row-menu__item"
              onClick={() => {
                setOpen(false)
                onDelete()
              }}
            >
              Delete order…
            </button>
          ) : order.deleteBlockedReason === 'claimed_before' ? (
            <p className="order-row-menu__reason">{claimedBeforeMessage}</p>
          ) : null}
        </div>
      )}
    </div>
  )
}
