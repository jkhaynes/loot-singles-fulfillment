import { useState } from 'react'
import { pickingIssueTypes } from './ordersApi'
import type { PickingIssueType, ReportIssueRequest } from './ordersApi'

/**
 * Reporting a picking issue against one line. Extracted from OrderDetailPage in
 * 016-mobile-picking so the list view and the focused view share one form rather than
 * growing two that drift apart.
 */
export function ReportIssueForm({
  lineId,
  isSubmitting,
  onCancel,
  onSubmit,
}: {
  lineId: number
  isSubmitting: boolean
  onCancel: () => void
  onSubmit: (request: ReportIssueRequest) => void
}) {
  const [issueType, setIssueType] = useState<PickingIssueType>(pickingIssueTypes[0].value)
  const [requiredQuantity, setRequiredQuantity] = useState('')
  const [foundQuantity, setFoundQuantity] = useState('')
  const [note, setNote] = useState('')

  function toQuantity(value: string): number | null {
    const trimmed = value.trim()
    return trimmed === '' ? null : Number(trimmed)
  }

  return (
    <form
      className="order-detail-line__issue-form"
      onSubmit={(event) => {
        event.preventDefault()
        onSubmit({
          issueType,
          requiredQuantity: toQuantity(requiredQuantity),
          foundQuantity: toQuantity(foundQuantity),
          note: note.trim() === '' ? null : note.trim(),
        })
      }}
    >
      <div>
        <label htmlFor={`issue-type-${lineId}`}>Issue type</label>
        <select
          id={`issue-type-${lineId}`}
          value={issueType}
          onChange={(event) => setIssueType(event.target.value as PickingIssueType)}
        >
          {pickingIssueTypes.map((type) => (
            <option key={type.value} value={type.value}>
              {type.label}
            </option>
          ))}
        </select>
      </div>
      <div className="order-detail-line__issue-quantities">
        <div>
          <label htmlFor={`required-quantity-${lineId}`}>Quantity required</label>
          <input
            id={`required-quantity-${lineId}`}
            type="number"
            min="0"
            inputMode="numeric"
            value={requiredQuantity}
            onChange={(event) => setRequiredQuantity(event.target.value)}
          />
        </div>
        <div>
          <label htmlFor={`found-quantity-${lineId}`}>Quantity found</label>
          <input
            id={`found-quantity-${lineId}`}
            type="number"
            min="0"
            inputMode="numeric"
            value={foundQuantity}
            onChange={(event) => setFoundQuantity(event.target.value)}
          />
        </div>
      </div>
      <div>
        <label htmlFor={`issue-note-${lineId}`}>Note (optional)</label>
        <textarea
          id={`issue-note-${lineId}`}
          rows={2}
          maxLength={500}
          value={note}
          onChange={(event) => setNote(event.target.value)}
        />
      </div>
      <div className="order-detail-line__issue-actions">
        <button type="submit" disabled={isSubmitting}>
          Submit Issue
        </button>
        <button type="button" onClick={onCancel} disabled={isSubmitting}>
          Cancel
        </button>
      </div>
    </form>
  )
}
