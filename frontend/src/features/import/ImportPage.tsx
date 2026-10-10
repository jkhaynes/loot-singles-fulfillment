import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useBlocker } from 'react-router-dom'
import { getNewOrdersFromTcgplayer, importPackingSlip } from './importApi'
import type { ImportSnapshot } from './importApi'
import './ImportPage.css'
type ImportSource = 'tcgplayer' | 'pdf'

export function ImportPage() {
  const [file, setFile] = useState<File | null>(null)
  const [snapshot, setSnapshot] = useState<ImportSnapshot | null>(null)
  const [error, setError] = useState('')
  const [running, setRunning] = useState(false)
  const [source, setSource] = useState<ImportSource>('pdf')
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

  async function run(next: ImportSource) {
    if (next === 'pdf' && !file) return
    setError('')
    setSource(next)
    setRunning(true)
    setSnapshot(null)
    const controller = new AbortController()
    controllerRef.current = controller
    try {
      const stream =
        next === 'tcgplayer'
          ? getNewOrdersFromTcgplayer(controller.signal)
          : importPackingSlip(file as File, controller.signal)
      for await (const update of stream) setSnapshot(update)
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

  function submit(event: FormEvent) {
    event.preventDefault()
    void run('pdf')
  }

  function confirmCancel() {
    setCancelConfirmationOpen(false)
    controllerRef.current?.abort()
  }

  function confirmNavigation() {
    controllerRef.current?.abort()
    if (blocker.state === 'blocked') blocker.proceed()
  }

  const attemptFailed = snapshot?.attemptFailureCode?.startsWith('tcgplayer') ?? false
  const noNewOrders =
    source === 'tcgplayer' &&
    snapshot?.status === 'completed' &&
    !snapshot.attemptFailureCode &&
    snapshot.results.every((result) => result.failureCode === 'duplicateOrder')
  // On an API press an already-imported order is a normal outcome (FR-011): count them in one line
  // instead of listing each. PDF imports keep one row per result.
  const isAlreadyImported = (result: ImportSnapshot['results'][number]) =>
    result.failureCode === 'duplicateOrder'
  const alreadyImportedCount =
    source === 'tcgplayer' ? (snapshot?.results.filter(isAlreadyImported).length ?? 0) : 0
  const listedResults =
    source === 'tcgplayer'
      ? (snapshot?.results.filter((result) => !isAlreadyImported(result)) ?? [])
      : (snapshot?.results ?? [])
  // Pressing Get new orders again is the API retry, so only a PDF import offers a Retry button.
  const retry =
    source === 'pdf' &&
    (snapshot?.status === 'failed' ||
      snapshot?.status === 'interrupted' ||
      snapshot?.status === 'cancelled')
  // A count of 0 of 0 says nothing when the attempt failed before finding any orders.
  const showProgress = !(snapshot?.attemptFailureCode && snapshot.ordersDetected === 0)

  // Progress, results and banners for the latest run. They render under the control that started
  // it: the API results under Get new orders, the PDF results in the packing-slip section.
  function renderStatus(placement: ImportSource) {
    if (placement !== source) return null
    return (
      <>
        {error && (
          <p role="alert" className="import-alert import-alert--error">
            {error}
          </p>
        )}
        {snapshot && (
          <div className="import-results" aria-live="polite">
            {showProgress && (
              <p className="import-progress">
                {snapshot.ordersProcessed} of {snapshot.ordersDetected} orders processed
              </p>
            )}
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
            {attemptFailed && (
              <div role="alert" className="import-alert import-alert--error">
                {snapshot.attemptFailureCode === 'tcgplayerResponseInvalid' ? (
                  <>
                    <p>
                      TCGplayer sent a reply the app didn't understand. A manager should check the
                      TCGplayer setup.
                    </p>
                    <p className="import-alert__detail">{snapshot.attemptFailureMessage}</p>
                  </>
                ) : (
                  <p>{snapshot.attemptFailureMessage}</p>
                )}
                <p>You can still upload a packing slip below.</p>
              </div>
            )}
            {noNewOrders && <p className="import-alert import-alert--info">No new orders.</p>}
            {alreadyImportedCount > 0 && (
              <p className="import-alert import-alert--info">
                {alreadyImportedCount} already imported
              </p>
            )}
            {snapshot.status === 'failed' && !attemptFailed && (
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
                You can safely retry.
              </p>
            )}
            <ul className="import-order-list">
              {listedResults.map((result, index) => (
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
              <button type="button" onClick={() => void run(source)}>
                Retry import
              </button>
            )}
          </div>
        )}
      </>
    )
  }

  return (
    <main className="import-page">
      <Link to="/" className="import-back-action">
        <span aria-hidden="true">←</span> Back to Dashboard
      </Link>
      <section className="import-card">
        <h1>Import orders</h1>
        <p>Get the store's open orders from TCGplayer.</p>
        <div className="import-actions">
          <button type="button" disabled={running} onClick={() => void run('tcgplayer')}>
            {running && source === 'tcgplayer' ? 'Getting orders…' : 'Get new orders'}
          </button>
          {running && (
            <button
              type="button"
              className="import-cancel-action"
              onClick={() => setCancelConfirmationOpen(true)}
            >
              Cancel Import
            </button>
          )}
        </div>
        {renderStatus('tcgplayer')}
        <section className="import-fallback">
          <h2>TCGplayer not working? Upload a packing slip instead.</h2>
          <p>One TCGplayer packing-slip PDF, 25 MB maximum.</p>
          <form onSubmit={submit}>
            <div className="import-file-picker">
              <input
                id="packing-slip"
                className="import-file-picker__input"
                type="file"
                accept="application/pdf,.pdf"
                onChange={(e) => setFile(e.target.files?.[0] ?? null)}
              />
              <label htmlFor="packing-slip" className="import-file-picker__button">
                Choose packing slip PDF
              </label>
              <span className="import-file-picker__name">{file?.name ?? 'No file chosen'}</span>
            </div>
            <button className="import-secondary-action" disabled={!file || running}>
              {running && source === 'pdf' ? 'Importing…' : 'Import orders'}
            </button>
          </form>
          {renderStatus('pdf')}
        </section>
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
