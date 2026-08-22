import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { importPackingSlip } from './importApi'
import type { ImportSnapshot } from './importApi'
import './ImportPage.css'
export function ImportPage() {
  const [file, setFile] = useState<File | null>(null)
  const [snapshot, setSnapshot] = useState<ImportSnapshot | null>(null)
  const [error, setError] = useState('')
  const [running, setRunning] = useState(false)
  async function submit(event?: FormEvent) {
    event?.preventDefault()
    if (!file) return
    setError('')
    setRunning(true)
    setSnapshot(null)
    try {
      for await (const next of importPackingSlip(file)) setSnapshot(next)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'The import could not be started.')
    } finally {
      setRunning(false)
    }
  }
  const retry = snapshot?.status === 'failed' || snapshot?.status === 'interrupted'
  return (
    <main className="import-page">
      <nav className="import-nav">
        <Link to="/">Dashboard</Link>
        <Link to="/orders">Browse orders</Link>
      </nav>
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
    </main>
  )
}
