import { useEffect, useRef, useState } from 'react'
import { getOrderLabel, OrderNotStartedError } from '../orders/ordersApi'
import type { LabelContent } from '../orders/ordersApi'
import { PrintableLabel } from './PrintableLabel'

/**
 * Reprints an order's label (FR-016).
 *
 * Labels jam, misprint, and fall off sleeves, and the workflow now depends on a sticker — so
 * there has to be a way to produce another without re-picking the order. The same button serves
 * the order page and the packing desk.
 *
 * Nothing is fetched until asked: the label is a print action, not something every order view
 * should be loading in case someone wants it.
 */
export interface PrintLabelButtonProps {
  orderId: number
  className?: string
  children?: string
}

export function PrintLabelButton({ orderId, className, children }: PrintLabelButtonProps) {
  const [label, setLabel] = useState<LabelContent | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(false)
  // Printing has to wait for the label to be in the DOM, which is a render away from the click.
  const shouldPrint = useRef(false)

  useEffect(() => {
    if (label !== null && shouldPrint.current) {
      shouldPrint.current = false
      window.print()
    }
  }, [label])

  async function handleClick() {
    setIsLoading(true)
    setError(null)
    try {
      shouldPrint.current = true
      setLabel(await getOrderLabel(orderId))
    } catch (caught) {
      shouldPrint.current = false
      setError(
        caught instanceof OrderNotStartedError
          ? 'Nothing has been picked on this order yet, so there is no label to print.'
          : "Couldn't load this order's label. Try again.",
      )
    } finally {
      setIsLoading(false)
    }
  }

  return (
    <>
      <button type="button" className={className} onClick={handleClick} disabled={isLoading}>
        {isLoading ? 'Loading…' : (children ?? 'Print label')}
      </button>

      {error !== null && (
        <p role="alert" className="print-label__error">
          {error}
        </p>
      )}

      {/* Out of view on screen and the only thing on the page when printed. */}
      {label !== null && <PrintableLabel label={label} />}
    </>
  )
}
