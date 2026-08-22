import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useBlocker } from 'react-router-dom'
import { importPackingSlip } from './importApi'
import type { ImportSnapshot } from './importApi'
import './ImportPage.css'
export function ImportPage() {
  const [file, setFile] = useState<File | null>(null)
  const [snapshot, setSnapshot] = useState<ImportSnapshot | null>(null)
  const [error, setError] = useState('')
  const [running, setRunning] = useState(false)
  const [cancelConfirmationOpen, setCancelConfirmationOpen] = useState(false)
  const controllerRef = useRef<AbortController | null>(null)
  const mountedRef = useRef(true)
  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) =>
      running && currentLocation.pathname !== nextLocation.pathname,
  )

  useEffect(() => {
    if (!running) return

    const warnBeforeUnload = (event: BeforeUnloadEvent) => {
      event.preventDefault()
      event.returnValue = ''
    }

    window.addEventListener('beforeunload', warnBeforeUnload)
    return () => window.removeEventListener('beforeunload', warnBeforeUnload)
  }, [running])

  useEffect(() => {
    mountedRef.current = true

    return () => {
      mountedRef.current = false
      controllerRef.current?.abort()
    }
  }, [])

  async function submit(event?: FormEvent) {
    event?.preventDefault()
    if (!file) return
    setError('')
    setRunning(true)
    setSnapshot(null)
    const controller = new AbortController()
    controllerRef.current = controller
    try {
      for await (const next of importPackingSlip(file, controller.signal)) setSnapshot(next)
    } catch (caught) {
      if (controller.signal.aborted) {
        if (mountedRef.current) {
          setSnapshot((current) => ({
            ...(current ?? emptySnapshot),
            status: 'cancelled',
          }))
        }
      } else {
        setError(caught instanceof Error ? caught.message : 'The import could not be started.')
      }
    } finally {
      if (controllerRef.current === controller) controllerRef.current = null
      if (mountedRef.current) setRunning(false)
    }
  }

  function confirmCancel() {
    setCancelConfirmationOpen(false)
    controllerRef.current?.abort()
  }

  function confirmNavigation() {
    controllerRef.current?.abort()
    if (blocker.state === 'blocked') blocker.proceed()
  }

  const retry =
    snapshot?.status === 'failed' ||
    snapshot?.status === 'interrupted' ||
    snapshot?.status === 'cancelled'
  return (
    <main className="import-page">
      <Link to="/" className="import-back-action">
        <span aria-hidden="true">←</span> Back to Dashboard
      </Link>
      <section className="import-card">
        <h1>Import packing slip</h1>
        <p>Upload one TCGplayer packing-slip PDF (25 MB maximum).</p>
        <form onSubmit={submit}>
          <label htmlFor="packing-slip">Packing slip PDF</label>
          <input
            id="packing-slip"
            type="file"
            accept="application/pdf,.pdf"
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          />
          <button disabled={!file || running}>{running ? 'Importing…' : 'Import orders'}</button>
          {running && (
            <button
              type="button"
              className="import-cancel-action"
              onClick={() => setCancelConfirmationOpen(true)}
            >
              Cancel Import
            </button>
          )}
        </form>
        {error && (
          <p role="alert" className="import-alert import-alert--error">
            {error}
          </p>
        )}
        {snapshot && (
          <div className="import-results" aria-live="polite">
            <p className="import-progress">
              {snapshot.ordersProcessed} of {snapshot.ordersDetected} orders processed
            </p>
            {snapshot.attemptFailureCode === 'summaryMismatch' && (
              <p role="alert" className="import-alert import-alert--warning">
                Summary mismatch: {snapshot.attemptFailureMessage}
              </p>
            )}
            {snapshot.attemptFailureCode === 'unreadablePdf' && (
              <p role="alert" className="import-alert import-alert--error">
                This PDF could not be read as a packing slip. {snapshot.attemptFailureMessage}
              </p>
            )}
            {snapshot.status === 'failed' && (
              <p role="alert" className="import-alert import-alert--error">
                Import failed. {snapshot.operationFailureMessage} Completed orders remain imported.
              </p>
            )}
            {snapshot.status === 'interrupted' && (
              <p role="alert" className="import-alert import-alert--warning">
                Connection lost. These results are incomplete and potentially stale; some orders may
                already have imported.
              </p>
            )}
            {snapshot.status === 'cancelled' && (
              <p role="alert" className="import-alert import-alert--warning">
                Import cancelled. Completed orders remain imported and remaining processing stopped.
                You can safely retry this PDF.
              </p>
            )}
            <ul className="import-order-list">
              {snapshot.results.map((result, index) => (
                <li
                  key={`${result.sourceOrderIdentifier ?? 'unknown'}-${index}`}
                  data-outcome={result.outcome}
                >
                  <strong>{result.sourceOrderIdentifier ?? 'Unknown order'}</strong>
                  <span>
                    {result.outcome === 'succeeded'
                      ? 'Imported successfully'
                      : (result.failureMessage ?? result.failureCode ?? 'Rejected')}
                  </span>
                </li>
              ))}
            </ul>
            {retry && (
              <button type="button" onClick={() => submit()}>
                Retry import
              </button>
            )}
          </div>
        )}
      </section>
      {cancelConfirmationOpen && (
        <div
          role="alertdialog"
          aria-modal="true"
          aria-labelledby="cancel-import-title"
          className="import-confirmation"
        >
          <div className="import-confirmation__panel">
            <h2 id="cancel-import-title">Stop this import?</h2>
            <p>Completed orders remain imported. Remaining processing will stop.</p>
            <div className="import-confirmation__actions">
              <button type="button" onClick={() => setCancelConfirmationOpen(false)}>
                Keep importing
              </button>
              <button type="button" onClick={confirmCancel}>
                Stop import
              </button>
            </div>
          </div>
        </div>
      )}
      {blocker.state === 'blocked' && (
        <div
          role="alertdialog"
          aria-modal="true"
          aria-labelledby="leave-import-title"
          className="import-confirmation"
        >
          <div className="import-confirmation__panel">
            <h2 id="leave-import-title">Leave and stop this import?</h2>
            <p>Completed orders remain imported. Remaining processing will stop.</p>
            <div className="import-confirmation__actions">
              <button type="button" onClick={() => blocker.reset()}>
                Stay and continue
              </button>
              <button type="button" onClick={confirmNavigation}>
                Leave and stop
              </button>
            </div>
          </div>
        </div>
      )}
    </main>
  )
}

const emptySnapshot: ImportSnapshot = {
  status: 'cancelled',
  ordersDetected: 0,
  ordersProcessed: 0,
  succeededCount: 0,
  failedCount: 0,
  attemptFailureCode: null,
  attemptFailureMessage: null,
  operationFailureMessage: null,
  results: [],
}
