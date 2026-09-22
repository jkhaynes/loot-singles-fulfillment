import { useEffect, useRef } from 'react'
import JsBarcode from 'jsbarcode'
import qrcode from 'qrcode-generator'
import type { LabelContent } from '../orders/ordersApi'
import { formatContributors } from './contributors'
import './label.css'

/**
 * The printed hand-off label (PRD §22.1).
 *
 * Every value comes from the server (FR-017), so the label cannot disagree with the order it
 * identifies and a reprint matches the original. This component only renders and encodes.
 *
 * It carries two codes because a scanner is a keyboard — it types exactly what is encoded — and
 * the workflow has two destinations. The QR brings a scanner or a phone camera into this
 * application; the Code 128 types the bare TCGplayer identifier into TCGplayer's own search, which
 * is where a tracking number gets recorded. One code cannot do both.
 */
export interface OrderLabelProps {
  label: LabelContent
  /** Absolute base for the QR's link. Defaults to wherever the app is being served from. */
  origin?: string
}

function formatPickedAt(pickedAt: string | null): string {
  if (pickedAt === null) {
    return ''
  }

  return new Date(pickedAt).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}

export function OrderLabel({ label, origin }: OrderLabelProps) {
  const qrRef = useRef<HTMLDivElement>(null)
  const barcodeRef = useRef<SVGSVGElement>(null)

  const base = origin ?? (typeof window === 'undefined' ? '' : window.location.origin)
  // Resolves to where this order gets packed, so a phone camera lands on the action rather than a
  // page the packer has to navigate from.
  const qrTarget = `${base}/packing/${label.orderId}`

  useEffect(() => {
    if (qrRef.current === null) {
      return
    }

    const code = qrcode(0, 'M')
    code.addData(qrTarget)
    code.make()
    qrRef.current.innerHTML = code.createSvgTag({ cellSize: 4, margin: 0, scalable: true })
  }, [qrTarget])

  useEffect(() => {
    if (barcodeRef.current === null) {
      return
    }

    JsBarcode(barcodeRef.current, label.tcgplayerOrderId, {
      format: 'CODE128',
      width: 1,
      height: 26,
      // The printed value IS the human-readable full identifier the label must carry (FR-010,
      // FR-012), so what a person reads and what a scanner types cannot drift apart.
      displayValue: true,
      fontSize: 13,
      textMargin: 0,
      margin: 0,
      font: 'ui-monospace, Consolas, monospace',
    })
  }, [label.tcgplayerOrderId])

  const pickedBy = formatContributors(label.pickedBy.map((person) => person.displayName))
  const pickedAt = formatPickedAt(label.pickedAt)
  const attribution = [pickedBy, pickedAt].filter((part) => part !== '').join(' · ')

  return (
    <div
      className={`order-label${label.isHeld ? ' order-label--held' : ''}`}
      aria-label={label.isHeld ? 'Hold label' : 'Ready to pack label'}
    >
      {label.isHeld && (
        <div className="order-label__band" aria-hidden="true">
          HOLD
        </div>
      )}

      <div className="order-label__qr" ref={qrRef} aria-hidden="true" />

      <div className="order-label__text">
        <p className="order-label__code">ORDER {label.orderId}</p>
        <p className="order-label__counts">
          {label.cardCount} {label.cardCount === 1 ? 'card' : 'cards'}
          {/* Omitted rather than printed as zero: "no cards set aside" on every hold label would
              be worse than saying nothing (FR-046). */}
          {label.setAsideCount !== null && label.setAsideCount > 0 && (
            <> · {label.setAsideCount} set aside</>
          )}
          {/* Nothing sets this in this feature. It exists so adding write-offs later does not
              reopen the layout of a label already in circulation (FR-014). */}
          {label.shipsShort && <span className="order-label__short">SHORT</span>}
        </p>
        {attribution !== '' && <p className="order-label__picker">{attribution}</p>}
      </div>

      <div className="order-label__barcode">
        <svg ref={barcodeRef} />
      </div>
    </div>
  )
}
