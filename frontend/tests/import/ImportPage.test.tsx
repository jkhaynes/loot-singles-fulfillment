import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
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
  it('submits a file and renders progress and specific per-order outcomes', async () => {
    vi.mocked(importApi.importPackingSlip).mockReturnValue(
      snapshots({ ...base, status: 'inProgress', ordersProcessed: 1 }, base),
    )
    render(
      <MemoryRouter>
        <ImportPage />
      </MemoryRouter>,
    )
    await userEvent.upload(
      screen.getByLabelText(/packing slip/i),
      new File(['pdf'], 'orders.pdf', { type: 'application/pdf' }),
    )
    await userEvent.click(screen.getByRole('button', { name: /import/i }))
    expect(await screen.findByText('A-1')).toBeInTheDocument()
    expect(screen.getByText(/Quantity must be positive/)).toBeInTheDocument()
    expect(screen.getByText(/2 of 2/)).toBeInTheDocument()
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
    render(
      <MemoryRouter>
        <ImportPage />
      </MemoryRouter>,
    )
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
    render(
      <MemoryRouter>
        <ImportPage />
      </MemoryRouter>,
    )
    await userEvent.upload(screen.getByLabelText(/packing slip/i), new File(['x'], 'x.pdf'))
    await userEvent.click(screen.getByRole('button', { name: /import/i }))
    expect(await screen.findByText(new RegExp(text, 'i'))).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument()
  })
})
