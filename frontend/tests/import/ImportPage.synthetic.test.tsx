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

// Shaped like the Development synthetic source's result: eight orders imported through the real
// pipeline, then a second press reporting them as already imported.
const firstPress: importApi.ImportSnapshot = {
  status: 'completed',
  ordersDetected: 2,
  ordersProcessed: 2,
  succeededCount: 2,
  failedCount: 0,
  attemptFailureCode: null,
  attemptFailureMessage: null,
  operationFailureMessage: null,
  results: ['SYN-0001-A1', 'SYN-0002-A1'].map((id, index) => ({
    sourceOrderIdentifier: id,
    outcome: 'succeeded',
    failureCode: null,
    failureMessage: null,
    resultingOrderId: index + 1,
  })),
}

const secondPress: importApi.ImportSnapshot = {
  ...firstPress,
  succeededCount: 0,
  failedCount: 2,
  results: firstPress.results.map((result) => ({
    ...result,
    outcome: 'rejected',
    failureCode: 'duplicateOrder',
    failureMessage: 'This order was already imported.',
    resultingOrderId: null,
  })),
}

describe('ImportPage with synthetic source results (021 FR-008)', () => {
  beforeEach(() => vi.resetAllMocks())

  it('shows synthetic results exactly like real ones, with no mock or synthetic wording', async () => {
    vi.mocked(importApi.getNewOrdersFromTcgplayer)
      .mockReturnValueOnce(snapshots(firstPress))
      .mockReturnValueOnce(snapshots(secondPress))
    const router = createMemoryRouter([{ path: '/import', element: <ImportPage /> }], {
      initialEntries: ['/import'],
    })
    const { container } = render(<RouterProvider router={router} />)

    await userEvent.click(screen.getByRole('button', { name: /get new orders/i }))
    expect(await screen.findByText('SYN-0002-A1')).toBeInTheDocument()
    expect(container).not.toHaveTextContent(/mock|synthetic/i)

    await userEvent.click(screen.getByRole('button', { name: /get new orders/i }))
    expect(await screen.findAllByText(/already imported/i)).not.toHaveLength(0)
    expect(container).not.toHaveTextContent(/mock|synthetic/i)
  })
})
