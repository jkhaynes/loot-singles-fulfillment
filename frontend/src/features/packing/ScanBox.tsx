import { useEffect, useRef, useState } from 'react'

/**
 * Where a scanned label or a typed order number lands (FR-023, FR-024).
 *
 * A handheld scanner is a keyboard that types fast and ends with Enter, so this holds focus and
 * submits on Enter — a packer with a sleeve in one hand never has to click first. The same box
 * takes a typed order number, because the scanner is not always the way in.
 */
export interface ScanBoxProps {
  onSubmit: (code: string) => void
  isBusy: boolean
}

export function ScanBox({ onSubmit, isBusy }: ScanBoxProps) {
  const [value, setValue] = useState('')
  const inputRef = useRef<HTMLInputElement>(null)

  // Focus follows every completed lookup, so consecutive scans need no clicking between them.
  useEffect(() => {
    if (!isBusy) {
      inputRef.current?.focus()
    }
  }, [isBusy])

  return (
    <form
      className="packing-scan"
      onSubmit={(event) => {
        event.preventDefault()
        const code = value.trim()

        if (code.length === 0) {
          return
        }

        onSubmit(code)
        setValue('')
      }}
    >
      <label htmlFor="packing-scan-input">Scan a label, or type an order number</label>
      <div className="packing-scan__row">
        <input
          id="packing-scan-input"
          ref={inputRef}
          className="packing-scan__input"
          value={value}
          autoComplete="off"
          // A scanner types the whole code in one burst; autocapitalize and autocorrect would
          // quietly mangle it on a touch device.
          autoCapitalize="off"
          autoCorrect="off"
          spellCheck={false}
          onChange={(event) => setValue(event.target.value)}
        />
        <button type="submit" className="packing-scan__button" disabled={isBusy}>
          {isBusy ? 'Looking up…' : 'Look up'}
        </button>
      </div>
    </form>
  )
}
