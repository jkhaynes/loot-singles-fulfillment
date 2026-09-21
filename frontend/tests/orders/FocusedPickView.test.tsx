import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { FocusedPickView } from '../../src/features/orders/FocusedPickView'
import { groupOrderLines } from '../../src/features/orders/orderGrouping'
import type { OrderLineDetail } from '../../src/features/orders/ordersApi'
import { buildLine } from '../support/orderBuilders'

function renderView(
  lines: OrderLineDetail[],
  overrides: Partial<Parameters<typeof FocusedPickView>[0]> = {},
) {
  const props = {
    groups: groupOrderLines(lines),
    canRecordOutcome: true,
    blockedReason: null,
    recordingLineId: null,
    onPicked: vi.fn(),
    onReportIssue: vi.fn(),
    ...overrides,
  }

  return { props, ...render(<FocusedPickView {...props} />) }
}

/** Three products in one set, so navigation can be exercised without crossing a box boundary. */
function oneSet() {
  return [
    buildLine({ set: 'Alpha', productName: 'First Card' }),
    buildLine({ set: 'Alpha', productName: 'Second Card' }),
    buildLine({ set: 'Alpha', productName: 'Third Card' }),
  ]
}

function next() {
  return screen.getByRole('button', { name: /next/i })
}

function previous() {
  return screen.getByRole('button', { name: /previous|back/i })
}

describe('FocusedPickView — one product at a time', () => {
  it('shows the first product and not the others', () => {
    renderView(oneSet())

    expect(screen.getByRole('heading', { name: 'First Card' })).toBeInTheDocument()
    expect(screen.queryByText('Second Card')).not.toBeInTheDocument()
  })

  it('reports position within the set and the order', () => {
    renderView(oneSet())

    expect(screen.getByText(/1 of 3/i)).toBeInTheDocument()
    expect(screen.getByText(/Alpha/)).toBeInTheDocument()
  })
})

// T020 — the central safety rule (FR-011). Asserted positively: no request issued, and no
// outcome changed. "No error appeared" would pass against a component that recorded silently.
describe('FocusedPickView — navigating records nothing', () => {
  it('issues no pick or issue request while moving forward', async () => {
    const user = userEvent.setup()
    const { props } = renderView(oneSet())

    await user.click(next())
    await user.click(next())

    expect(screen.getByRole('heading', { name: 'Third Card' })).toBeInTheDocument()
    expect(props.onPicked).not.toHaveBeenCalled()
    expect(props.onReportIssue).not.toHaveBeenCalled()
  })

  it('issues no request while moving back', async () => {
    const user = userEvent.setup()
    const { props } = renderView(oneSet())

    await user.click(next())
    await user.click(previous())

    expect(screen.getByRole('heading', { name: 'First Card' })).toBeInTheDocument()
    expect(props.onPicked).not.toHaveBeenCalled()
    expect(props.onReportIssue).not.toHaveBeenCalled()
  })

  it('leaves every line unresolved after a full pass through the order', async () => {
    const user = userEvent.setup()
    const lines = oneSet()
    const { props } = renderView(lines)

    await user.click(next())
    await user.click(next())
    await user.click(previous())
    await user.click(previous())

    // The lines themselves are unchanged — nothing recorded an outcome behind the picker's back.
    expect(lines.every((line) => line.pickOutcome === null)).toBe(true)
    expect(props.onPicked).not.toHaveBeenCalled()
    expect(props.onReportIssue).not.toHaveBeenCalled()
  })

  it('shows a passed-over product as still unresolved when the picker returns', async () => {
    const user = userEvent.setup()
    renderView(oneSet())

    await user.click(next())
    await user.click(previous())

    // Still offering to record it, because nothing was recorded.
    expect(screen.getByRole('button', { name: /^picked$/i })).toBeEnabled()
  })
})

// T021 — FR-013. Swipe is an extra affordance, never the only way through.
describe('FocusedPickView — reachable without swiping', () => {
  it('reaches every product using on-screen controls alone', async () => {
    const user = userEvent.setup()
    renderView(oneSet())

    const seen: string[] = []
    seen.push(screen.getByRole('heading', { level: 2 }).textContent ?? '')
    await user.click(next())
    seen.push(screen.getByRole('heading', { level: 2 }).textContent ?? '')
    await user.click(next())
    seen.push(screen.getByRole('heading', { level: 2 }).textContent ?? '')

    expect(seen).toEqual(['First Card', 'Second Card', 'Third Card'])
  })

  it('disables going back from the first product rather than hiding the control', () => {
    renderView(oneSet())

    // Hiding it would make the layout shift as the picker moves; disabling keeps it steady.
    expect(previous()).toBeDisabled()
  })
})

// T022
describe('FocusedPickView — recording is explicit', () => {
  it('records a pick for the current product only', async () => {
    const user = userEvent.setup()
    const lines = oneSet()
    const { props } = renderView(lines)

    await user.click(next())
    await user.click(screen.getByRole('button', { name: /^picked$/i }))

    expect(props.onPicked).toHaveBeenCalledTimes(1)
    expect(props.onPicked).toHaveBeenCalledWith(lines[1].id)
  })

  it('shows a recorded product as picked', () => {
    const lines = [buildLine({ set: 'Alpha', productName: 'Done', pickOutcome: 'picked' })]
    renderView(lines)

    expect(screen.getByRole('button', { name: /^picked$/i })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
  })

  it('opens the issue form on request and reports against the current product', async () => {
    const user = userEvent.setup()
    const lines = oneSet()
    const { props } = renderView(lines)

    await user.click(screen.getByRole('button', { name: /report issue/i }))
    await user.click(screen.getByRole('button', { name: /submit issue/i }))

    expect(props.onReportIssue).toHaveBeenCalledTimes(1)
    expect(props.onReportIssue.mock.calls[0][0]).toBe(lines[0].id)
  })
})

// T024 — PRD §5.3 and §15. The focused view is where a missed "PULL 3 COPIES" costs most.
describe('FocusedPickView — quantity emphasis', () => {
  it('emphasises a quantity greater than one', () => {
    renderView([buildLine({ set: 'Alpha', productName: 'Three Of These', quantity: 3 })])

    const quantity = screen.getByText('3')
    expect(quantity).toHaveAttribute('data-emphasis', 'high')
  })

  it('states the count in words as well as a numeral', () => {
    renderView([buildLine({ set: 'Alpha', quantity: 3 })])

    // A bare numeral is easy to skim past on a phone held in one hand.
    expect(screen.getByText(/pull 3 copies/i)).toBeInTheDocument()
  })

  it('does not emphasise a single copy', () => {
    renderView([buildLine({ set: 'Alpha', quantity: 1 })])

    expect(screen.getByText('1')).not.toHaveAttribute('data-emphasis')
    expect(screen.queryByText(/pull 1 cop/i)).not.toBeInTheDocument()
  })
})

// T025 — the claim released by a manager while the picker is working (spec edge case).
describe('FocusedPickView — claim lost mid-pick', () => {
  it('withdraws the recording actions', () => {
    renderView(oneSet(), {
      canRecordOutcome: false,
      blockedReason: 'A manager released this order.',
    })

    expect(screen.queryByRole('button', { name: /^picked$/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /report issue/i })).not.toBeInTheDocument()
  })

  it('explains why, rather than leaving the picker to guess', () => {
    renderView(oneSet(), {
      canRecordOutcome: false,
      blockedReason: 'A manager released this order.',
    })

    expect(screen.getByRole('status')).toHaveTextContent('A manager released this order.')
  })

  it('still allows navigation, so the picker can see what they were holding', async () => {
    const user = userEvent.setup()
    renderView(oneSet(), { canRecordOutcome: false, blockedReason: 'Released.' })

    await user.click(next())

    expect(screen.getByRole('heading', { name: 'Second Card' })).toBeInTheDocument()
  })
})

describe('FocusedPickView — recording in flight', () => {
  it('disables the action for the line being recorded', () => {
    const lines = oneSet()
    renderView(lines, { recordingLineId: lines[0].id })

    expect(screen.getByRole('button', { name: /^picked$/i })).toBeDisabled()
  })
})

describe('FocusedPickView — card identity', () => {
  it('shows the set, collector number and condition alongside the name', () => {
    renderView([
      buildLine({
        set: 'Alpha',
        productName: 'Genesect ex',
        collectorNumber: '#067/086',
        condition: 'Near Mint',
        variant: 'Holofoil',
      }),
    ])

    const card = screen.getByRole('article')
    expect(within(card).getByText('#067/086')).toBeInTheDocument()
    expect(within(card).getByText('Near Mint')).toBeInTheDocument()
    expect(within(card).getByText('Holofoil')).toBeInTheDocument()
  })

  it('shows no image rather than a wrong one when none resolved', () => {
    renderView([buildLine({ set: 'Alpha', imageUrl: null })])

    // PRD §17: no image is better than the wrong image.
    expect(screen.getByLabelText(/image unavailable/i)).toBeInTheDocument()
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })
})
