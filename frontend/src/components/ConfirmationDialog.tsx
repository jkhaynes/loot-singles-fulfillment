import { useId } from 'react'
import type { ReactNode } from 'react'
import './ConfirmationDialog.css'

interface ConfirmationDialogProps {
  title: string
  children: ReactNode
  keepLabel: string
  confirmLabel: string
  onKeep: () => void
  onConfirm: () => void
}

export function ConfirmationDialog({
  title,
  children,
  keepLabel,
  confirmLabel,
  onKeep,
  onConfirm,
}: ConfirmationDialogProps) {
  const titleId = useId()
  return (
    <div
      role="alertdialog"
      aria-modal="true"
      aria-labelledby={titleId}
      className="confirmation-dialog"
    >
      <div className="confirmation-dialog__panel">
        <h2 id={titleId}>{title}</h2>
        {children}
        <div className="confirmation-dialog__actions">
          <button type="button" onClick={onKeep}>
            {keepLabel}
          </button>
          <button type="button" onClick={onConfirm}>
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}
