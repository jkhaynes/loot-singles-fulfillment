import { useId, useState } from 'react'
import type { ReactNode } from 'react'
import './ConfirmationDialog.css'

interface ConfirmationDialogProps {
  title: string
  children: ReactNode
  keepLabel: string
  confirmLabel: string
  onKeep: () => void
  onConfirm: () => void
  destructive?: boolean
  confirmWord?: string
  busy?: boolean
}

export function ConfirmationDialog({
  title,
  children,
  keepLabel,
  confirmLabel,
  onKeep,
  onConfirm,
  destructive = false,
  confirmWord,
  busy = false,
}: ConfirmationDialogProps) {
  const titleId = useId()
  const wordInputId = useId()
  const [typedWord, setTypedWord] = useState('')
  const wordMissing = confirmWord !== undefined && typedWord !== confirmWord
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
        {confirmWord !== undefined && (
          <div className="confirmation-dialog__word">
            <label htmlFor={wordInputId}>Type {confirmWord} to confirm</label>
            <input
              id={wordInputId}
              type="text"
              value={typedWord}
              autoComplete="off"
              disabled={busy}
              onChange={(e) => setTypedWord(e.target.value)}
            />
          </div>
        )}
        <div className="confirmation-dialog__actions">
          <button type="button" autoFocus disabled={busy} onClick={onKeep}>
            {keepLabel}
          </button>
          <button
            type="button"
            className={destructive ? 'confirmation-dialog__confirm--destructive' : undefined}
            disabled={busy || wordMissing}
            onClick={onConfirm}
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}
