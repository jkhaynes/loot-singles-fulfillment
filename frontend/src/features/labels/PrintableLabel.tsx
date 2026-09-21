import { createPortal } from 'react-dom'
import type { LabelContent } from '../orders/ordersApi'
import { OrderLabel } from './OrderLabel'

/**
 * A label in the one position the print stylesheet can isolate: a direct child of `<body>`.
 *
 * Printing a single element out of a full application is harder than it looks. The first version
 * hid everything else with `visibility: hidden`, which keeps an element's layout — so the whole
 * app's height was still there, invisible, and paginated at 1⅛ inches it printed as nine blank
 * pages. The label itself sat inside an off-screen host, so `inset: 0` placed it relative to that
 * host, ten thousand pixels left of the page.
 *
 * Portalling to `<body>` fixes both at once. The print stylesheet removes every other child of
 * `<body>` with `display: none`, which takes their layout with them, and the label is left as the
 * only thing on the page, at its top-left corner. On screen it stays out of view (label.css).
 */
export function PrintableLabel({ label }: { label: LabelContent }) {
  return createPortal(
    <div className="order-label-print">
      <OrderLabel label={label} />
    </div>,
    document.body,
  )
}
