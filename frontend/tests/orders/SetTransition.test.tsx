import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { SetTransition } from '../../src/features/orders/SetTransition'
import { advanceFrom, groupOrderLines } from '../../src/features/orders/orderGrouping'
import { buildLine } from '../support/orderBuilders'

function handlers() {
  return {
    onContinue: vi.fn(),
    onReturnToLine: vi.fn(),
    onReportMissing: vi.fn(),
  }
}

/** The picker has finished Alpha and Beta is next. */
function completeAdvance() {
  const lines = [
    buildLine({ set: 'Alpha', productName: 'A1', pickOutcome: 'picked' }),
    buildLine({ set: 'Beta', productName: 'B1', quantity: 4 }),
    buildLine({ set: 'Beta', productName: 'B2' }),
  ]
  const advance = advanceFrom(groupOrderLines(lines), lines[0].id)
  if (advance.kind !== 'set-complete') throw new Error('expected set-complete')
  return advance
}

/** The picker is at the end of Alpha but A2 was never resolved. */
function incompleteAdvance() {
  const lines = [
    buildLine({ set: 'Alpha', productName: 'A1', pickOutcome: 'picked' }),
    buildLine({ set: 'Alpha', productName: 'A2', quantity: 3 }),
    buildLine({ set: 'Beta', productName: 'B1' }),
  ]
  const advance = advanceFrom(groupOrderLines(lines), lines[1].id)
  if (advance.kind !== 'set-incomplete') throw new Error('expected set-incomplete')
  return advance
}

describe('SetTransition — box finished', () => {
  it('names the finished set and the next one with its counts', () => {
    render(<SetTransition advance={completeAdvance()} {...handlers()} />)

    expect(screen.getByText('Alpha')).toBeInTheDocument()
    expect(screen.getByText('Beta')).toBeInTheDocument()
    // Two products, five physical cards — what the next box owes.
    expect(screen.getByText(/2 products/i)).toBeInTheDocument()
    expect(screen.getByText(/5 cards/i)).toBeInTheDocument()
  })

  it('offers a single way forward', async () => {
    const user = userEvent.setup()
    const spies = handlers()
    render(<SetTransition advance={completeAdvance()} {...spies} />)

    await user.click(screen.getByRole('button', { name: /start beta/i }))

    expect(spies.onContinue).toHaveBeenCalledTimes(1)
  })

  it('does not present the unfinished-box warning', () => {
    render(<SetTransition advance={completeAdvance()} {...handlers()} />)

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})

describe('SetTransition — box not finished (the guard)', () => {
  it('says the set is unfinished and lists what is outstanding', () => {
    render(<SetTransition advance={incompleteAdvance()} {...handlers()} />)

    expect(screen.getByRole('alert')).toBeInTheDocument()

    const outstanding = screen.getByRole('list')
    expect(within(outstanding).getByText('A2')).toBeInTheDocument()
    // The quantity still owed is the high-risk number here (PRD §15).
    expect(within(outstanding).getByText('3')).toHaveAttribute('data-emphasis', 'high')
  })

  it('offers exactly three choices', () => {
    render(<SetTransition advance={incompleteAdvance()} {...handlers()} />)

    // Return to a product, report what is missing, leave the set (FR-017). No fourth door,
    // and no way past without choosing one.
    expect(screen.getByRole('button', { name: /back to A2/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /report what is missing/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /leave the (set|box)/i })).toBeInTheDocument()
    expect(screen.getAllByRole('button')).toHaveLength(3)
  })

  it('returns the picker to the unresolved product', async () => {
    const user = userEvent.setup()
    const spies = handlers()
    const advance = incompleteAdvance()
    render(<SetTransition advance={advance} {...spies} />)

    await user.click(screen.getByRole('button', { name: /back to A2/i }))

    expect(spies.onReturnToLine).toHaveBeenCalledWith(advance.unresolvedLines[0].id)
  })

  it('sends the picker to report the first outstanding product', async () => {
    const user = userEvent.setup()
    const spies = handlers()
    const advance = incompleteAdvance()
    render(<SetTransition advance={advance} {...spies} />)

    await user.click(screen.getByRole('button', { name: /report what is missing/i }))

    expect(spies.onReportMissing).toHaveBeenCalledWith(advance.unresolvedLines[0].id)
  })

  it('lets the picker leave deliberately, without recording anything', async () => {
    const user = userEvent.setup()
    const spies = handlers()
    render(<SetTransition advance={incompleteAdvance()} {...spies} />)

    await user.click(screen.getByRole('button', { name: /leave the (set|box)/i }))

    // Leaving is a navigation, never an outcome (FR-019, FR-011).
    expect(spies.onContinue).toHaveBeenCalledTimes(1)
    expect(spies.onReturnToLine).not.toHaveBeenCalled()
    expect(spies.onReportMissing).not.toHaveBeenCalled()
  })

  it('never describes an unfinished set as finished', () => {
    render(<SetTransition advance={incompleteAdvance()} {...handlers()} />)

    expect(screen.queryByText(/finished|complete/i)).not.toBeInTheDocument()
  })
})
