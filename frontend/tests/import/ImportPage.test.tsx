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
  getNewOrdersFromTcgplayer: vi.fn(),
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
    expect(
      screen.getByRole('heading', {
        name: /tcgplayer not working\? upload a packing slip instead\./i,
      }),
    ).toBeInTheDocument()
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

  describe('Get new orders from TCGplayer', () => {
    const duplicate = (id: string): importApi.ImportOrderResult => ({
      sourceOrderIdentifier: id,
      outcome: 'rejected',
      failureCode: 'duplicateOrder',
      failureMessage: 'Already imported.',
      resultingOrderId: null,
    })

    function pendingApiImport() {
      return vi.mocked(importApi.getNewOrdersFromTcgplayer).mockImplementation(async function* (
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

    it('offers Get new orders as the primary action and packing-slip upload as the fallback', () => {
      renderImportPage()
      const primary = screen.getByRole('button', { name: /get new orders/i })
      const fallbackHeading = screen.getByRole('heading', {
        name: /tcgplayer not working\? upload a packing slip instead\./i,
      })
      expect(primary.compareDocumentPosition(fallbackHeading)).toBe(
        Node.DOCUMENT_POSITION_FOLLOWING,
      )
      expect(screen.getByLabelText(/packing slip/i)).toBeInTheDocument()
      expect(screen.getByRole('button', { name: /^import orders$/i })).toBeInTheDocument()
    })

    it('shows API results directly under Get new orders, before the packing-slip section', async () => {
      vi.mocked(importApi.getNewOrdersFromTcgplayer).mockReturnValue(snapshots(base))
      renderImportPage()
      await userEvent.click(screen.getByRole('button', { name: /get new orders/i }))
      const progress = await screen.findByText(/2 of 2 orders processed/)
      const primary = screen.getByRole('button', { name: /get new orders/i })
      const fallbackHeading = screen.getByRole('heading', {
        name: /tcgplayer not working\? upload a packing slip instead\./i,
      })
      expect(primary.compareDocumentPosition(progress)).toBe(Node.DOCUMENT_POSITION_FOLLOWING)
      expect(progress.compareDocumentPosition(fallbackHeading)).toBe(
        Node.DOCUMENT_POSITION_FOLLOWING,
      )
    })

    it('shows PDF results with the packing-slip section, after its Import orders button', async () => {
      vi.mocked(importApi.importPackingSlip).mockReturnValue(snapshots(base))
      renderImportPage()
      await userEvent.upload(screen.getByLabelText(/packing slip/i), new File(['x'], 'x.pdf'))
      await userEvent.click(screen.getByRole('button', { name: /^import orders$/i }))
      const progress = await screen.findByText(/2 of 2 orders processed/)
      const fallbackHeading = screen.getByRole('heading', {
        name: /tcgplayer not working\? upload a packing slip instead\./i,
      })
      const pdfButton = screen.getByRole('button', { name: /^import orders$/i })
      expect(fallbackHeading.compareDocumentPosition(progress)).toBe(
        Node.DOCUMENT_POSITION_FOLLOWING,
      )
      expect(pdfButton.compareDocumentPosition(progress)).toBe(Node.DOCUMENT_POSITION_FOLLOWING)
    })

    it('shows No file chosen until a packing slip is picked, then the file name', async () => {
      renderImportPage()
      expect(screen.getByText('No file chosen')).toBeInTheDocument()
      await userEvent.upload(
        screen.getByLabelText(/packing slip/i),
        new File(['pdf'], 'orders.pdf', { type: 'application/pdf' }),
      )
      expect(screen.getByText('orders.pdf')).toBeInTheDocument()
      expect(screen.queryByText('No file chosen')).not.toBeInTheDocument()
    })

    it('shows progress and then the per-order results', async () => {
      vi.mocked(importApi.getNewOrdersFromTcgplayer).mockReturnValue(
        snapshots({ ...base, status: 'inProgress', ordersProcessed: 1 }, base),
      )
      renderImportPage()
      await userEvent.click(screen.getByRole('button', { name: /get new orders/i }))
      expect(await screen.findByText('A-1')).toBeInTheDocument()
      expect(screen.getByText(/Quantity must be positive/)).toBeInTheDocument()
      expect(screen.getByText(/2 of 2/)).toBeInTheDocument()
      expect(screen.queryByText(/no new orders/i)).not.toBeInTheDocument()
      expect(importApi.importPackingSlip).not.toHaveBeenCalled()
    })

    it('shows No new orders when nothing was detected', async () => {
      vi.mocked(importApi.getNewOrdersFromTcgplayer).mockReturnValue(
        snapshots({
          ...base,
          ordersDetected: 0,
          ordersProcessed: 0,
          succeededCount: 0,
          failedCount: 0,
          results: [],
        }),
      )
      renderImportPage()
      await userEvent.click(screen.getByRole('button', { name: /get new orders/i }))
      expect(await screen.findByText(/no new orders/i)).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: /retry/i })).not.toBeInTheDocument()
      expect(screen.queryByText(/import failed/i)).not.toBeInTheDocument()
    })

    it('shows No new orders and one already-imported line when every result is already imported', async () => {
      vi.mocked(importApi.getNewOrdersFromTcgplayer).mockReturnValue(
        snapshots({
          ...base,
          succeededCount: 0,
          failedCount: 2,
          results: [duplicate('D-1'), duplicate('D-2')],
        }),
      )
      renderImportPage()
      await userEvent.click(screen.getByRole('button', { name: /get new orders/i }))
      expect(await screen.findByText(/no new orders/i)).toBeInTheDocument()
      expect(screen.getByText('2 already imported')).toBeInTheDocument()
      expect(screen.queryByText('D-1')).not.toBeInTheDocument()
      expect(screen.queryByText('D-2')).not.toBeInTheDocument()
      expect(screen.queryByRole('listitem')).not.toBeInTheDocument()
    })

    it('collapses already-imported results into one line and keeps imported and rejected rows', async () => {
      vi.mocked(importApi.getNewOrdersFromTcgplayer).mockReturnValue(
        snapshots({
          ...base,
          ordersDetected: 4,
          ordersProcessed: 4,
          failedCount: 3,
          results: [duplicate('D-1'), ...base.results, duplicate('D-2')],
        }),
      )
      renderImportPage()
      await userEvent.click(screen.getByRole('button', { name: /get new orders/i }))
      expect(await screen.findByText('2 already imported')).toBeInTheDocument()
      expect(screen.queryByText('D-1')).not.toBeInTheDocument()
      expect(screen.queryByText('D-2')).not.toBeInTheDocument()
      expect(screen.queryByText('Already imported.')).not.toBeInTheDocument()
      expect(screen.getAllByRole('listitem')).toHaveLength(2)
      expect(screen.getByText('A-1')).toBeInTheDocument()
      expect(screen.getByText(/Quantity must be positive/)).toBeInTheDocument()
      expect(screen.getByText(/4 of 4 orders processed/)).toBeInTheDocument()
      expect(screen.queryByText(/failed/i)).not.toBeInTheDocument()
      expect(screen.queryByText(/no new orders/i)).not.toBeInTheDocument()
    })

    it('shows no already-imported line when the API press had none', async () => {
      vi.mocked(importApi.getNewOrdersFromTcgplayer).mockReturnValue(snapshots(base))
      renderImportPage()
      await userEvent.click(screen.getByRole('button', { name: /get new orders/i }))
      await screen.findByText('A-1')
      expect(screen.queryByText(/already imported/i)).not.toBeInTheDocument()
    })

    it('keeps one row per already-imported order on a PDF import', async () => {
      vi.mocked(importApi.importPackingSlip).mockReturnValue(
        snapshots({
          ...base,
          succeededCount: 0,
          failedCount: 2,
          results: [duplicate('D-1'), duplicate('D-2')],
        }),
      )
      renderImportPage()
      await userEvent.upload(
        screen.getByLabelText(/packing slip/i),
        new File(['pdf'], 'orders.pdf', { type: 'application/pdf' }),
      )
      await userEvent.click(screen.getByRole('button', { name: /^import orders$/i }))
      expect(await screen.findByText('D-1')).toBeInTheDocument()
      expect(screen.getByText('D-2')).toBeInTheDocument()
      expect(screen.getAllByText('Already imported.')).toHaveLength(2)
      expect(screen.queryByText(/\d+ already imported/)).not.toBeInTheDocument()
    })

    it('does not show No new orders when some results are not duplicates', async () => {
      vi.mocked(importApi.getNewOrdersFromTcgplayer).mockReturnValue(
        snapshots({ ...base, results: [duplicate('D-1'), ...base.results] }),
      )
      renderImportPage()
      await userEvent.click(screen.getByRole('button', { name: /get new orders/i }))
      await screen.findByText('1 already imported')
      expect(screen.queryByText(/no new orders/i)).not.toBeInTheDocument()
    })

    it.each([
      [
        'zero detected orders',
        { ordersDetected: 0, ordersProcessed: 0, failedCount: 0, results: [] },
        /0 of 0 orders processed/,
      ],
      [
        'only duplicates',
        { failedCount: 2, results: [duplicate('D-1'), duplicate('D-2')] },
        /2 of 2 orders processed/,
      ],
    ] as const)(
      'does not show No new orders for a PDF import with %s',
      async (_name, overrides, progress) => {
        vi.mocked(importApi.importPackingSlip).mockReturnValue(
          snapshots({ ...base, succeededCount: 0, ...overrides, results: [...overrides.results] }),
        )
        renderImportPage()
        await userEvent.upload(
          screen.getByLabelText(/packing slip/i),
          new File(['pdf'], 'orders.pdf', { type: 'application/pdf' }),
        )
        await userEvent.click(screen.getByRole('button', { name: /^import orders$/i }))
        expect(await screen.findByText(progress)).toBeInTheDocument()
        expect(screen.queryByText(/no new orders/i)).not.toBeInTheDocument()
      },
    )

    const attemptFailure = (code: string, message: string): importApi.ImportSnapshot => ({
      ...base,
      status: 'failed',
      ordersDetected: 0,
      ordersProcessed: 0,
      succeededCount: 0,
      failedCount: 0,
      attemptFailureCode: code,
      attemptFailureMessage: message,
      results: [],
    })

    it.each([
      [
        'tcgplayerNotConfigured',
        "Getting orders from TCGplayer isn't set up here. Use packing-slip upload instead.",
      ],
      [
        'tcgplayerUnavailable',
        "Couldn't reach TCGplayer. Orders already imported are kept. Try again in a few minutes, or upload a packing slip.",
      ],
      [
        'tcgplayerAccessRefused',
        "TCGplayer refused the store's connection. A manager needs to check the TCGplayer API setup. You can upload a packing slip meanwhile.",
      ],
    ] as const)(
      'shows the server message verbatim and a pointer to the packing slip below for %s',
      async (code, message) => {
        vi.mocked(importApi.getNewOrdersFromTcgplayer).mockReturnValue(
          snapshots(attemptFailure(code, message)),
        )
        renderImportPage()
        await userEvent.click(screen.getByRole('button', { name: /get new orders/i }))
        const alert = await screen.findByRole('alert')
        expect(alert).toHaveTextContent(message)
        expect(alert).toHaveTextContent('You can still upload a packing slip below.')
        expect(screen.queryByText(/didn't understand/i)).not.toBeInTheDocument()
        expect(screen.queryByText(/below still works/i)).not.toBeInTheDocument()
        expect(screen.queryByText(/no new orders/i)).not.toBeInTheDocument()
        expect(screen.queryByText(/completed orders remain imported/i)).not.toBeInTheDocument()
      },
    )

    it('leads a responseInvalid failure with plain language and keeps the server detail underneath', async () => {
      const detail = "TCGplayer has no order status named 'Ready To Ship'."
      vi.mocked(importApi.getNewOrdersFromTcgplayer).mockReturnValue(
        snapshots(attemptFailure('tcgplayerResponseInvalid', detail)),
      )
      renderImportPage()
      await userEvent.click(screen.getByRole('button', { name: /get new orders/i }))
      const lead = await screen.findByText(
        "TCGplayer sent a reply the app didn't understand. A manager should check the TCGplayer setup.",
      )
      const detailLine = screen.getByText(detail)
      expect(lead.compareDocumentPosition(detailLine)).toBe(Node.DOCUMENT_POSITION_FOLLOWING)
      expect(detailLine).toHaveClass('import-alert__detail')
      expect(screen.getByRole('alert')).toHaveTextContent(
        'You can still upload a packing slip below.',
      )
    })

    it('hides 0 of 0 orders processed when the attempt failed before detecting any orders', async () => {
      vi.mocked(importApi.getNewOrdersFromTcgplayer).mockReturnValue(
        snapshots(attemptFailure('tcgplayerUnavailable', "Couldn't reach TCGplayer.")),
      )
      renderImportPage()
      await userEvent.click(screen.getByRole('button', { name: /get new orders/i }))
      await screen.findByText(/couldn't reach tcgplayer/i)
      expect(screen.queryByText(/orders processed/i)).not.toBeInTheDocument()
    })

    it('has no Retry button after an API failure; pressing Get new orders again retries', async () => {
      vi.mocked(importApi.getNewOrdersFromTcgplayer)
        .mockReturnValueOnce(
          snapshots(attemptFailure('tcgplayerUnavailable', "Couldn't reach TCGplayer.")),
        )
        .mockReturnValueOnce(snapshots(base))
      renderImportPage()
      await userEvent.click(screen.getByRole('button', { name: /get new orders/i }))
      await screen.findByText(/couldn't reach tcgplayer/i)
      expect(screen.queryByRole('button', { name: /retry/i })).not.toBeInTheDocument()
      const again = screen.getByRole('button', { name: /get new orders/i })
      expect(again).toBeEnabled()
      await userEvent.click(again)
      expect(await screen.findByText(/Quantity must be positive/)).toBeInTheDocument()
      expect(importApi.getNewOrdersFromTcgplayer).toHaveBeenCalledTimes(2)
      expect(importApi.importPackingSlip).not.toHaveBeenCalled()
    })

    it('shows Interrupted guidance for a lost API connection', async () => {
      vi.mocked(importApi.getNewOrdersFromTcgplayer).mockReturnValue(
        snapshots({ ...base, status: 'interrupted' }),
      )
      renderImportPage()
      await userEvent.click(screen.getByRole('button', { name: /get new orders/i }))
      expect(await screen.findByText(/incomplete and potentially stale/i)).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: /retry/i })).not.toBeInTheDocument()
      expect(screen.getByRole('button', { name: /get new orders/i })).toBeEnabled()
    })

    it('keeps running when cancellation is declined and shows Cancelled after confirmation', async () => {
      const importMock = pendingApiImport()
      renderImportPage()
      await userEvent.click(screen.getByRole('button', { name: /get new orders/i }))
      expect(await screen.findByText(/1 of 2/)).toBeInTheDocument()
      expect(screen.getByRole('button', { name: /import orders/i })).toBeDisabled()

      await userEvent.click(screen.getByRole('button', { name: /cancel import/i }))
      await userEvent.click(screen.getByRole('button', { name: /keep importing/i }))
      expect(importMock.mock.calls[0][0]?.aborted).toBe(false)

      await userEvent.click(screen.getByRole('button', { name: /cancel import/i }))
      await userEvent.click(screen.getByRole('button', { name: /stop import/i }))
      expect(await screen.findByText(/import cancelled/i)).toBeInTheDocument()
      expect(screen.queryByText(/connection lost/i)).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: /retry/i })).not.toBeInTheDocument()
      expect(screen.getByRole('button', { name: /get new orders/i })).toBeEnabled()
      expect(importMock.mock.calls[0][0]?.aborted).toBe(true)
    })

    it('guards navigation while an API import runs and aborts on confirmed leave', async () => {
      const importMock = pendingApiImport()
      renderImportPage()
      await userEvent.click(screen.getByRole('button', { name: /get new orders/i }))
      await screen.findByText(/1 of 2/)

      const whileRunning = new Event('beforeunload', { cancelable: true })
      window.dispatchEvent(whileRunning)
      expect(whileRunning.defaultPrevented).toBe(true)

      await userEvent.click(screen.getByRole('link', { name: /back to dashboard/i }))
      await userEvent.click(screen.getByRole('button', { name: /stay and continue/i }))
      expect(importMock.mock.calls[0][0]?.aborted).toBe(false)

      await userEvent.click(screen.getByRole('link', { name: /back to dashboard/i }))
      await userEvent.click(screen.getByRole('button', { name: /leave and stop/i }))
      expect(await screen.findByText(/dashboard destination/i)).toBeInTheDocument()
      expect(importMock.mock.calls[0][0]?.aborted).toBe(true)
    })
  })
})
