import { StrictMode } from 'react'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ImportPage } from '../../src/features/import/ImportPage'
import * as importApi from '../../src/features/import/importApi'

vi.mock('../../src/features/import/importApi', async (original) => ({
  ...(await original<typeof import('../../src/features/import/importApi')>()),
  importPackingSlip: vi.fn(),
}))

async function* snapshots(...items: importApi.ImportSnapshot[]) {
  for (const item of items) yield item
}

function renderImportPage(initialEntries = ['/import'], initialIndex?: number) {
  const router = createMemoryRouter(
    [
      { path: '/import', element: <ImportPage /> },
      { path: '/', element: <p>Dashboard destination</p> },
    ],
    { initialEntries, initialIndex },
  )
  render(<RouterProvider router={router} />)
  return router
}

function renderImportPageInStrictMode() {
  const router = createMemoryRouter([{ path: '/import', element: <ImportPage /> }], {
    initialEntries: ['/import'],
  })
  render(
    <StrictMode>
      <RouterProvider router={router} />
    </StrictMode>,
  )
}

function pendingImport() {
  return vi.mocked(importApi.importPackingSlip).mockImplementation(async function* (
    _file: File,
    signal?: AbortSignal,
  ) {
    yield { ...base, status: 'inProgress', ordersProcessed: 1 }
    await new Promise<void>((_resolve, reject) => {
      signal?.addEventListener(
        'abort',
        () => reject(new DOMException('The operation was aborted.', 'AbortError')),
        { once: true },
      )
    })
  })
}
const base: importApi.ImportSnapshot = {
  status: 'completed',
  ordersDetected: 2,
  ordersProcessed: 2,
  succeededCount: 1,
  failedCount: 1,
  attemptFailureCode: null,
  attemptFailureMessage: null,
  operationFailureMessage: null,
  results: [
    {
      sourceOrderIdentifier: 'A-1',
      outcome: 'succeeded',
      failureCode: null,
      failureMessage: null,
      resultingOrderId: 1,
    },
    {
      sourceOrderIdentifier: 'A-2',
      outcome: 'rejected',
      failureCode: 'invalidQuantity',
      failureMessage: 'Quantity must be positive.',
      resultingOrderId: null,
    },
  ],
}

describe('ImportPage', () => {
  beforeEach(() => vi.resetAllMocks())

  it('provides a button-style return to Dashboard without premature Orders navigation', () => {
    renderImportPage()

    expect(screen.getByRole('link', { name: /back to dashboard/i })).toHaveAttribute('href', '/')
    expect(screen.queryByRole('link', { name: /browse orders/i })).not.toBeInTheDocument()
  })

  it('submits a file and renders progress and specific per-order outcomes', async () => {
    vi.mocked(importApi.importPackingSlip).mockReturnValue(
      snapshots({ ...base, status: 'inProgress', ordersProcessed: 1 }, base),
    )
    renderImportPage()
    await userEvent.upload(
      screen.getByLabelText(/packing slip/i),
      new File(['pdf'], 'orders.pdf', { type: 'application/pdf' }),
    )
    await userEvent.click(screen.getByRole('button', { name: /import/i }))
    expect(await screen.findByText('A-1')).toBeInTheDocument()
    expect(screen.getByText(/Quantity must be positive/)).toBeInTheDocument()
    expect(screen.getByText(/2 of 2/)).toBeInTheDocument()
  })

  it('settles and re-enables submission under React Strict Mode effect replay', async () => {
    vi.mocked(importApi.importPackingSlip).mockReturnValue(snapshots(base))
    renderImportPageInStrictMode()
    await userEvent.upload(screen.getByLabelText(/packing slip/i), new File(['x'], 'x.pdf'))
    await userEvent.click(screen.getByRole('button', { name: /import orders/i }))

    expect(await screen.findByText(/2 of 2/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /import orders/i })).toBeEnabled()
    expect(screen.queryByRole('button', { name: /cancel import/i })).not.toBeInTheDocument()
  })
  it.each([
    ['summaryMismatch', 'summary'],
    ['unreadablePdf', 'could not be read'],
  ])('shows attempt feedback for %s', async (code, text) => {
    vi.mocked(importApi.importPackingSlip).mockReturnValue(
      snapshots({
        ...base,
        attemptFailureCode: code,
        attemptFailureMessage: 'The file could not be read; summary differs.',
      }),
    )
    renderImportPage()
    await userEvent.upload(screen.getByLabelText(/packing slip/i), new File(['x'], 'x.pdf'))
    await userEvent.click(screen.getByRole('button', { name: /import/i }))
    expect(await screen.findByText(new RegExp(text, 'i'))).toBeInTheDocument()
  })
  it.each([
    ['failed', 'completed orders remain imported'],
    ['interrupted', 'incomplete and potentially stale'],
  ])('shows retry guidance for %s', async (status, text) => {
    vi.mocked(importApi.importPackingSlip).mockReturnValue(
      snapshots({
        ...base,
        status: status as importApi.ImportSnapshot['status'],
        operationFailureMessage: status === 'failed' ? 'The import could not be completed.' : null,
      }),
    )
    renderImportPage()
    await userEvent.upload(screen.getByLabelText(/packing slip/i), new File(['x'], 'x.pdf'))
    await userEvent.click(screen.getByRole('button', { name: /import/i }))
    expect(await screen.findByText(new RegExp(text, 'i'))).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument()
  })

  it('keeps running when cancellation is declined and shows Cancelled after confirmation', async () => {
    const importMock = pendingImport()
    renderImportPage()
    await userEvent.upload(screen.getByLabelText(/packing slip/i), new File(['x'], 'x.pdf'))
    await userEvent.click(screen.getByRole('button', { name: /import orders/i }))
    expect(await screen.findByText(/1 of 2/)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /cancel import/i }))
    expect(screen.getByRole('alertdialog')).toHaveTextContent(/completed orders remain imported/i)
    await userEvent.click(screen.getByRole('button', { name: /keep importing/i }))
    expect(screen.getByRole('button', { name: /cancel import/i })).toBeInTheDocument()
    expect(importMock.mock.calls[0][1]?.aborted).toBe(false)

    await userEvent.click(screen.getByRole('button', { name: /cancel import/i }))
    await userEvent.click(screen.getByRole('button', { name: /stop import/i }))

    expect(await screen.findByText(/import cancelled/i)).toBeInTheDocument()
    expect(screen.getByText(/remaining processing stopped/i)).toBeInTheDocument()
    expect(screen.queryByText(/connection lost/i)).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument()
    expect(importMock.mock.calls[0][1]?.aborted).toBe(true)
  })

  it('guards application navigation and aborts before confirmed navigation', async () => {
    const importMock = pendingImport()
    renderImportPage()
    await userEvent.upload(screen.getByLabelText(/packing slip/i), new File(['x'], 'x.pdf'))
    await userEvent.click(screen.getByRole('button', { name: /import orders/i }))
    await screen.findByText(/1 of 2/)

    await userEvent.click(screen.getByRole('link', { name: /back to dashboard/i }))
    await userEvent.click(screen.getByRole('button', { name: /stay and continue/i }))
    expect(screen.getByRole('heading', { name: /import packing slip/i })).toBeInTheDocument()
    expect(importMock.mock.calls[0][1]?.aborted).toBe(false)

    await userEvent.click(screen.getByRole('link', { name: /back to dashboard/i }))
    await userEvent.click(screen.getByRole('button', { name: /leave and stop/i }))
    expect(await screen.findByText(/dashboard destination/i)).toBeInTheDocument()
    expect(importMock.mock.calls[0][1]?.aborted).toBe(true)
  })

  it('guards browser-history navigation and registers beforeunload only while running', async () => {
    const importMock = pendingImport()
    const router = renderImportPage(['/', '/import'], 1)
    const beforeRunning = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(beforeRunning)
    expect(beforeRunning.defaultPrevented).toBe(false)

    await userEvent.upload(screen.getByLabelText(/packing slip/i), new File(['x'], 'x.pdf'))
    await userEvent.click(screen.getByRole('button', { name: /import orders/i }))
    await screen.findByText(/1 of 2/)

    const whileRunning = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(whileRunning)
    expect(whileRunning.defaultPrevented).toBe(true)

    await router.navigate(-1)
    expect(await screen.findByRole('alertdialog')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: /leave and stop/i }))
    expect(await screen.findByText(/dashboard destination/i)).toBeInTheDocument()
    expect(importMock.mock.calls[0][1]?.aborted).toBe(true)
  })
})
