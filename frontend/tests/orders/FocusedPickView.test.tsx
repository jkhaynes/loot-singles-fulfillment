import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { FocusedPickView } from '../../src/features/orders/FocusedPickView'
import { groupOrderLines } from '../../src/features/orders/orderGrouping'
import type { OrderLineDetail, PickingIssueDetail } from '../../src/features/orders/ordersApi'
import { buildIssueLine, buildLine } from '../support/orderBuilders'

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

/** Three cards in one box, so navigation can be exercised without crossing a boundary. */
function oneBox() {
  return [
    buildLine({ set: 'Alpha', productName: 'First Card' }),
    buildLine({ set: 'Alpha', productName: 'Second Card' }),
    buildLine({ set: 'Alpha', productName: 'Third Card' }),
  ]
}

const next = () => screen.getByRole('button', { name: 'Next card', exact: true })
const previous = () => screen.getByRole('button', { name: /previous card/i })
const pickedButton = () => screen.getByRole('button', { name: /^picked|^pulled all/i })

describe('FocusedPickView — one card at a time', () => {
  it('shows the current card and not the others', () => {
    renderView(oneBox())

    expect(screen.getByRole('heading', { name: 'First Card' })).toBeInTheDocument()
    expect(screen.queryByText('Second Card')).not.toBeInTheDocument()
  })

  it('names the box and the position within it', () => {
    renderView(oneBox())

    expect(screen.getByText('Alpha')).toBeInTheDocument()
    expect(screen.getByText('1 of 3')).toBeInTheDocument()
  })

  it('fills the box bar to match the position beside it', () => {
    // Position, not completion: a bar that disagreed with the number next to it would read as
    // broken. What was actually pulled is checked on the review screen.
    const { container } = renderView(oneBox())

    expect(container.querySelector<HTMLElement>('.focused-pick__boxFill')?.style.width).toBe('33%')
  })
})

// FR-011. Asserted positively — no request issued, no outcome changed. "No error appeared"
// would pass against a component that recorded silently.
describe('FocusedPickView — moving records nothing', () => {
  it('issues no request while moving forward', async () => {
    const user = userEvent.setup()
    const { props } = renderView(oneBox())

    await user.click(next())
    await user.click(next())

    expect(screen.getByRole('heading', { name: 'Third Card' })).toBeInTheDocument()
    expect(props.onPicked).not.toHaveBeenCalled()
    expect(props.onReportIssue).not.toHaveBeenCalled()
  })

  it('leaves every card unresolved after a full pass', async () => {
    const user = userEvent.setup()
    const lines = oneBox()
    const { props } = renderView(lines)

    await user.click(next())
    await user.click(next())
    await user.click(previous())
    await user.click(previous())

    expect(lines.every((line) => line.pickOutcome === null)).toBe(true)
    expect(props.onPicked).not.toHaveBeenCalled()
  })

  it('still offers to record a card the picker passed over', async () => {
    const user = userEvent.setup()
    renderView(oneBox())

    await user.click(next())
    await user.click(previous())

    expect(pickedButton()).toBeEnabled()
    expect(pickedButton()).toHaveAttribute('aria-pressed', 'false')
  })
})

// FR-019a. The defect this screen was rebuilt to fix.
describe('FocusedPickView — moving is never blocked', () => {
  function singleCardBoxes() {
    return ['Alpha', 'Beta', 'Gamma'].map((set) => buildLine({ set, productName: `Card ${set}` }))
  }

  it('crosses into the next box with the current one unresolved', async () => {
    const user = userEvent.setup()
    renderView(singleCardBoxes())

    await user.click(next())

    expect(screen.getByRole('heading', { name: 'Card Beta' })).toBeInTheDocument()
  })

  it('walks single-card boxes end to end with nothing resolved', async () => {
    const user = userEvent.setup()
    renderView(singleCardBoxes())

    await user.click(next())
    await user.click(next())

    expect(screen.getByRole('heading', { name: 'Card Gamma' })).toBeInTheDocument()
  })

  it('announces a new box on the card rather than on a screen of its own', async () => {
    const user = userEvent.setup()
    renderView(singleCardBoxes())

    await user.click(next())

    // The card is still there — this is a band, not an interstitial.
    expect(screen.getByRole('heading', { name: 'Card Beta' })).toBeInTheDocument()
    expect(screen.getByText(/new box/i)).toBeInTheDocument()
  })
})

describe('FocusedPickView — reachable without swiping', () => {
  it('reaches every card using the on-screen controls alone', async () => {
    const user = userEvent.setup()
    renderView(oneBox())

    const seen = [screen.getByRole('heading', { level: 2 }).textContent]
    await user.click(next())
    seen.push(screen.getByRole('heading', { level: 2 }).textContent)
    await user.click(next())
    seen.push(screen.getByRole('heading', { level: 2 }).textContent)

    expect(seen).toEqual(['First Card', 'Second Card', 'Third Card'])
  })

  it('disables going back from the first card rather than hiding the control', () => {
    renderView(oneBox())

    expect(previous()).toBeDisabled()
  })
})

describe('FocusedPickView — recording is explicit', () => {
  it('records a pick for the current card only', async () => {
    const user = userEvent.setup()
    const lines = oneBox()
    const { props } = renderView(lines)

    await user.click(next())
    await user.click(pickedButton())

    expect(props.onPicked).toHaveBeenCalledTimes(1)
    expect(props.onPicked).toHaveBeenCalledWith(lines[1].id)
  })

  it('confirms visibly once recorded', () => {
    const { container } = renderView([
      buildLine({ set: 'Alpha', productName: 'Done', pickOutcome: 'picked' }),
    ])

    // Label and card state, not colour alone.
    expect(screen.getByRole('button', { name: /picked ✓/i })).toBeInTheDocument()
    expect(container.querySelector('.focused-pick__card--picked')).not.toBeNull()
  })

  it('says it is recording while the request is in flight', () => {
    const lines = oneBox()
    renderView(lines, { recordingLineId: lines[0].id })

    expect(screen.getByRole('button', { name: /recording/i })).toBeDisabled()
  })

  it('reports an issue against the current card', async () => {
    const user = userEvent.setup()
    const lines = oneBox()
    const { props } = renderView(lines)

    await user.click(screen.getByRole('button', { name: /report an issue/i }))
    await user.click(screen.getByRole('button', { name: /submit issue/i }))

    expect(props.onReportIssue).toHaveBeenCalledTimes(1)
    expect(props.onReportIssue.mock.calls[0][0]).toBe(lines[0].id)
  })
})

// PRD §5.3, §15 — the costliest picking error in the shop.
describe('FocusedPickView — quantity', () => {
  it('states a quantity greater than one loudly, in words and figures', () => {
    renderView([buildLine({ set: 'Alpha', quantity: 3 })])

    expect(screen.getByText('3')).toHaveAttribute('data-emphasis', 'high')
    expect(screen.getByText(/copies to pull/i)).toBeInTheDocument()
  })

  it('carries the count into the button label', () => {
    renderView([buildLine({ set: 'Alpha', quantity: 3 })])

    expect(screen.getByRole('button', { name: 'Pulled all 3' })).toBeInTheDocument()
  })

  it('says nothing about quantity for a single copy', () => {
    renderView([buildLine({ set: 'Alpha', quantity: 1 })])

    // Printing "1" on every card would train the picker to ignore the element that matters
    // when it says 3.
    expect(screen.queryByText(/copies to pull/i)).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Picked' })).toBeInTheDocument()
  })

  // T063 / FR-021. The counter walks products, so calling them cards understates the pile in
  // an order where any line has quantity greater than one — "Card 1 of 2" when three cards
  // must be pulled. Products and physical cards are the one distinction this feature exists
  // to keep straight, so the label has to name which it is counting.
  it('counts products by that name, never calling them cards', () => {
    renderView([
      buildLine({ set: 'Alpha', quantity: 3, productName: 'Three Copies' }),
      buildLine({ set: 'Alpha', quantity: 1, productName: 'One Copy' }),
    ])

    expect(screen.getByText('Product 1 of 2')).toBeInTheDocument()
    expect(screen.queryByText(/^Card \d+ of \d+$/)).not.toBeInTheDocument()
  })
})

describe('FocusedPickView — card identity', () => {
  it('shows the collector number and the variant as separate chips', () => {
    renderView([
      buildLine({
        set: 'Alpha',
        collectorNumber: '#067/086',
        variant: 'Holofoil',
        condition: 'Near Mint',
        rarity: 'Double Rare',
      }),
    ])

    const card = screen.getByRole('article')
    expect(within(card).getByText('#067/086')).toBeInTheDocument()
    // The one fact the artwork cannot carry (PRD §16).
    expect(within(card).getByText('HOLOFOIL')).toBeInTheDocument()
    expect(within(card).getByText('Double Rare · Near Mint')).toBeInTheDocument()
  })

  it('omits the variant chip when the line has none', () => {
    renderView([buildLine({ set: 'Alpha', variant: null })])

    expect(screen.queryByText('HOLOFOIL')).not.toBeInTheDocument()
  })

  it('shows no image rather than a wrong one when none resolved', () => {
    renderView([buildLine({ set: 'Alpha', imageUrl: null })])

    // PRD §17. The placeholder is small on purpose — nothing to look at must not outrank the name.
    expect(screen.getByText('No image')).toBeInTheDocument()
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })
})

describe('FocusedPickView — claim lost mid-pick', () => {
  const blocked = { canRecordOutcome: false, blockedReason: 'A manager released this order.' }

  it('withdraws the recording actions and explains why', () => {
    renderView(oneBox(), blocked)

    expect(screen.queryByRole('button', { name: /^picked/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /report an issue/i })).not.toBeInTheDocument()
    expect(screen.getByText('A manager released this order.')).toBeInTheDocument()
  })

  it('still allows navigation, so the picker can see what they were holding', async () => {
    const user = userEvent.setup()
    renderView(oneBox(), blocked)

    await user.click(next())

    expect(screen.getByRole('heading', { name: 'Second Card' })).toBeInTheDocument()
  })
})

describe('FocusedPickView — the final review', () => {
  it('opens past the last card, listing every product', async () => {
    const user = userEvent.setup()
    renderView([
      buildLine({ set: 'Alpha', productName: 'Pulled', quantity: 2, pickOutcome: 'picked' }),
      buildLine({ set: 'Beta', productName: 'Left Open' }),
    ])

    await user.click(next())
    await user.click(next())

    expect(screen.getByText(/count the sleeve/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Pulled/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Left Open/i })).toBeInTheDocument()
  })

  it('jumps back to a card from the review and carries on', async () => {
    const user = userEvent.setup()
    renderView([
      buildLine({ set: 'Alpha', productName: 'Left Open' }),
      buildLine({ set: 'Beta', productName: 'Done', pickOutcome: 'picked' }),
    ])

    await user.click(next())
    await user.click(next())
    await user.click(screen.getByRole('button', { name: /Left Open/i }))

    expect(screen.getByRole('heading', { name: 'Left Open', level: 2 })).toBeInTheDocument()
  })

  it('completes only on a deliberate press, recording nothing', async () => {
    const user = userEvent.setup()
    const onCompleted = vi.fn()
    const lines = [buildLine({ set: 'Alpha', productName: 'Left Open' })]
    const { props } = renderView(lines, { onCompleted })

    await user.click(next())
    await user.click(screen.getByRole('button', { name: /finish picking/i }))

    expect(onCompleted).toHaveBeenCalledTimes(1)
    expect(props.onPicked).not.toHaveBeenCalled()
    expect(lines[0].pickOutcome).toBeNull()
  })

  it('returns to the cards without completing', async () => {
    const user = userEvent.setup()
    const onCompleted = vi.fn()
    renderView([buildLine({ set: 'Alpha', productName: 'Only One' })], { onCompleted })

    await user.click(next())
    await user.click(screen.getByRole('button', { name: /back to the cards/i }))

    expect(screen.getByRole('heading', { name: 'Only One', level: 2 })).toBeInTheDocument()
    expect(onCompleted).not.toHaveBeenCalled()
  })
})

// The claim action started life as a small chip in the top bar, beside the order code, while
// the dock showed a passive "not claimed" message. On an unclaimed order Claim *is* the action,
// so it belongs in the dock at the same weight as Picked, in reach of the thumb.
describe('FocusedPickView — claiming an order you are viewing', () => {
  const viewing = {
    canRecordOutcome: false,
    blockedReason: 'This order is not claimed, so picks cannot be recorded.',
  }

  it('offers Claim in the dock, not a passive message', async () => {
    const onClaim = vi.fn()
    renderView(oneBox(), { ...viewing, canClaim: true, onClaim })

    const claim = screen.getByRole('button', { name: /^claim$/i })
    expect(claim).toBeInTheDocument()
    // The dock's primary slot, the same control Picked occupies once the order is held.
    expect(claim).toHaveClass('focused-pick__picked')
    expect(screen.queryByText(/not claimed/i)).not.toBeInTheDocument()

    await userEvent.setup().click(claim)
    expect(onClaim).toHaveBeenCalledTimes(1)
  })

  it('says it is claiming while the request is in flight', () => {
    renderView(oneBox(), { ...viewing, canClaim: true, isClaiming: true, onClaim: vi.fn() })

    expect(screen.getByRole('button', { name: /claiming/i })).toBeDisabled()
  })

  it('falls back to the explanation when the order cannot be claimed', () => {
    renderView(oneBox(), {
      canRecordOutcome: false,
      blockedReason: 'Sam is picking this order.',
      canClaim: false,
    })

    expect(screen.queryByRole('button', { name: /^claim$/i })).not.toBeInTheDocument()
    expect(screen.getByText('Sam is picking this order.')).toBeInTheDocument()
  })

  it('offers picking rather than claiming once the order is held', () => {
    renderView(oneBox(), { canRecordOutcome: true, canClaim: false })

    expect(screen.getByRole('button', { name: /^picked$/i })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^claim$/i })).not.toBeInTheDocument()
  })
})

// 018-phone-issue-card. A reported product kept the actions of an untouched one: its main button
// still read "Pulled all 4" on a product reported as 3 of 4, and one tap on it replaced the report.
// A reported product now shows its issue on a chip, and its dock offers only moving on.
const shortReport: PickingIssueDetail = {
  issueType: 'cardNotFound',
  requiredQuantity: 4,
  foundQuantity: 3,
  note: 'Only 3 in the binder slot',
  reportedByEmployeeName: 'Sam',
  reportedAt: '2026-09-21T14:14:00Z',
}

function reportedFirst() {
  return [
    buildIssueLine({
      set: 'Alpha',
      productName: 'Reported Card',
      quantity: 4,
      currentIssue: shortReport,
    }),
    buildLine({ set: 'Alpha', productName: 'Next Up' }),
  ]
}

const issueDockNext = () => screen.getByRole('button', { name: 'Next card ›' })

describe('FocusedPickView — a reported product (018 US1)', () => {
  // T003
  it('names the issue on a chip, even when counts were recorded', () => {
    renderView(reportedFirst())

    expect(screen.getByRole('button', { name: 'Card Not Found' })).toBeInTheDocument()
  })

  it('offers only moving on, and states no quantity pulled', () => {
    renderView(reportedFirst())

    expect(issueDockNext()).toBeInTheDocument()
    expect(next()).toBeInTheDocument()
    expect(previous()).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^picked|^pulled all/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /report an issue/i })).not.toBeInTheDocument()
  })

  it('keeps the product identity and its quantity emphasis', () => {
    renderView(reportedFirst())

    expect(screen.getByRole('heading', { name: 'Reported Card' })).toBeInTheDocument()
    expect(screen.getByText('4')).toHaveAttribute('data-emphasis', 'high')
  })

  // T004
  it('moves to the next product and records nothing', async () => {
    const user = userEvent.setup()
    const { props } = renderView(reportedFirst())

    await user.click(issueDockNext())

    expect(screen.getByRole('heading', { name: 'Next Up' })).toBeInTheDocument()
    expect(props.onPicked).not.toHaveBeenCalled()
    expect(props.onReportIssue).not.toHaveBeenCalled()
  })

  it('opens the final review from the last product, as the arrow does', async () => {
    const user = userEvent.setup()
    renderView([
      buildIssueLine({
        set: 'Alpha',
        productName: 'Only Card',
        quantity: 4,
        currentIssue: shortReport,
      }),
    ])

    await user.click(issueDockNext())

    expect(screen.getByText(/count the sleeve/i)).toBeInTheDocument()
  })

  it('is still reported after moving away and back', async () => {
    const user = userEvent.setup()
    renderView(reportedFirst())

    await user.click(next())
    await user.click(previous())

    expect(screen.getByRole('button', { name: 'Card Not Found' })).toBeInTheDocument()
    expect(issueDockNext()).toBeInTheDocument()
  })

  // T005 — what must not change.
  it('leaves a product with no outcome as it was', () => {
    renderView([buildLine({ set: 'Alpha', productName: 'Untouched', quantity: 4 })])

    expect(screen.getByRole('button', { name: 'Pulled all 4' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /report an issue/i })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Next card ›' })).not.toBeInTheDocument()
  })

  it('leaves a picked product as it was, with no chip', () => {
    renderView([buildLine({ set: 'Alpha', productName: 'Pulled', pickOutcome: 'picked' })])

    expect(screen.getByRole('button', { name: /picked ✓/i })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Card Not Found' })).not.toBeInTheDocument()
  })

  it('shows the chip to someone who cannot record, and keeps their dock as it was', () => {
    renderView(reportedFirst(), {
      canRecordOutcome: false,
      blockedReason: 'A manager released this order.',
    })

    expect(screen.getByRole('button', { name: 'Card Not Found' })).toBeInTheDocument()
    expect(screen.getByText('A manager released this order.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Next card ›' })).not.toBeInTheDocument()
  })
})

// T008 — the sheet the chip opens: what was reported, and nothing invented where it wasn't.
const reportedAt = new Date(shortReport.reportedAt).toLocaleString()
const sheet = () => screen.getByRole('dialog', { name: 'Reported issue' })
const openSheet = async (user: ReturnType<typeof userEvent.setup>) =>
  user.click(screen.getByRole('button', { name: 'Card Not Found' }))

function reportedWith(report: Partial<PickingIssueDetail>) {
  return [
    buildIssueLine({
      set: 'Alpha',
      productName: 'Reported Card',
      quantity: 4,
      currentIssue: { ...shortReport, ...report },
    }),
    buildLine({ set: 'Alpha', productName: 'Next Up' }),
  ]
}

describe('FocusedPickView — the reported-issue sheet (018 US2)', () => {
  it('does not open on arriving at the product', () => {
    renderView(reportedFirst())

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('shows what was reported when the chip is tapped', async () => {
    const user = userEvent.setup()
    renderView(reportedFirst())

    await openSheet(user)

    expect(within(sheet()).getByText('Card Not Found')).toBeInTheDocument()
    expect(within(sheet()).getByText('Required 4 · Found 3')).toBeInTheDocument()
    expect(within(sheet()).getByText('Only 3 in the binder slot')).toBeInTheDocument()
    // Amended 2026-09-22: the design holds for every issue type, so nothing assumes a shortage.
    expect(sheet()).not.toHaveTextContent(/pulled|short/i)
    expect(within(sheet()).getByText(`Sam · ${reportedAt}`)).toBeInTheDocument()
  })

  it('omits the quantity when only one count was recorded', async () => {
    const user = userEvent.setup()
    renderView(reportedWith({ requiredQuantity: null }))

    await openSheet(user)

    expect(sheet()).not.toHaveTextContent(/Required/)
  })

  it('omits the quantity when neither count was recorded', async () => {
    const user = userEvent.setup()
    renderView(
      reportedWith({
        requiredQuantity: null,
        foundQuantity: null,
      }),
    )

    await openSheet(user)

    expect(sheet()).not.toHaveTextContent(/Required/)
  })

  it('shows a report with no counts, such as a damaged card, without a counts line', async () => {
    const user = userEvent.setup()
    renderView(
      reportedWith({
        issueType: 'damaged',
        requiredQuantity: null,
        foundQuantity: null,
        note: 'Corner crease, and it is the only copy',
      }),
    )

    await user.click(screen.getByRole('button', { name: 'Damaged' }))

    expect(within(sheet()).getByText('Damaged')).toBeInTheDocument()
    expect(within(sheet()).getByText('Corner crease, and it is the only copy')).toBeInTheDocument()
    expect(sheet()).not.toHaveTextContent(/Required|pulled|short/i)
  })

  it('omits the note when none was recorded', async () => {
    const user = userEvent.setup()
    renderView(reportedWith({ note: null }))

    await openSheet(user)

    expect(within(sheet()).queryByText('Only 3 in the binder slot')).not.toBeInTheDocument()
    expect(within(sheet()).getByText('Required 4 · Found 3')).toBeInTheDocument()
  })

  it('shows the time alone when the reporter is unknown', async () => {
    const user = userEvent.setup()
    renderView(reportedWith({ reportedByEmployeeName: null }))

    await openSheet(user)

    expect(within(sheet()).getByText(reportedAt)).toBeInTheDocument()
  })

  it('closes on Close, and records nothing', async () => {
    const user = userEvent.setup()
    const { props } = renderView(reportedFirst())

    await openSheet(user)
    await user.click(within(sheet()).getByRole('button', { name: 'Close' }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(props.onPicked).not.toHaveBeenCalled()
    expect(props.onReportIssue).not.toHaveBeenCalled()
  })

  it('closes when the picker moves to another product', async () => {
    const user = userEvent.setup()
    renderView(reportedFirst())

    await openSheet(user)
    await user.click(next())
    await user.click(previous())

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
})

// T013 — correcting a report from the sheet. Both corrections are outcomes the picker could always
// record (015: a later outcome replaces a report); they have moved from the dock to the sheet.
describe('FocusedPickView — correcting a report (018 US3)', () => {
  it('offers Resolved, Edit report and Close to someone who can record', async () => {
    const user = userEvent.setup()
    renderView(reportedFirst())

    await openSheet(user)

    expect(within(sheet()).getByRole('button', { name: 'Resolved' })).toBeInTheDocument()
    expect(within(sheet()).getByRole('button', { name: 'Edit report' })).toBeInTheDocument()
    expect(within(sheet()).getByRole('button', { name: 'Close' })).toBeInTheDocument()
  })

  it('reads Resolved whatever the quantity', async () => {
    const user = userEvent.setup()
    renderView([
      buildIssueLine({
        set: 'Alpha',
        productName: 'Single',
        quantity: 1,
        currentIssue: { ...shortReport, requiredQuantity: 1, foundQuantity: 0 },
      }),
    ])

    await openSheet(user)

    expect(within(sheet()).getByRole('button', { name: 'Resolved' })).toBeInTheDocument()
  })

  it('records the product as picked when the report is resolved', async () => {
    const user = userEvent.setup()
    const lines = reportedFirst()
    const { props } = renderView(lines)

    await openSheet(user)
    await user.click(within(sheet()).getByRole('button', { name: 'Resolved' }))

    expect(props.onPicked).toHaveBeenCalledWith(lines[0].id)
  })

  it('cannot be tapped again while the correction is saving', async () => {
    const user = userEvent.setup()
    const lines = reportedFirst()
    const { rerender, props } = renderView(lines)

    await openSheet(user)
    rerender(<FocusedPickView {...props} recordingLineId={lines[0].id} />)

    expect(within(sheet()).getByRole('button', { name: 'Resolved' })).toBeDisabled()
    expect(within(sheet()).getByRole('button', { name: 'Edit report' })).toBeDisabled()
  })

  // A save that fails leaves the line reported, so the sheet and chip stay as they were.
  it('stays open, unchanged, when the product is still reported after saving', async () => {
    const user = userEvent.setup()
    const lines = reportedFirst()
    const { rerender, props } = renderView(lines)

    await openSheet(user)
    await user.click(within(sheet()).getByRole('button', { name: 'Resolved' }))
    rerender(<FocusedPickView {...props} recordingLineId={null} />)

    expect(sheet()).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Card Not Found' })).toBeInTheDocument()
  })

  it('opens the issue form filled in from the current report', async () => {
    const user = userEvent.setup()
    renderView(reportedFirst())

    await openSheet(user)
    await user.click(within(sheet()).getByRole('button', { name: 'Edit report' }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.getByLabelText('Issue type')).toHaveValue('cardNotFound')
    expect(screen.getByLabelText('Quantity required')).toHaveValue(4)
    expect(screen.getByLabelText('Quantity found')).toHaveValue(3)
    expect(screen.getByLabelText('Note (optional)')).toHaveValue('Only 3 in the binder slot')
  })

  it('submits the changed report', async () => {
    const user = userEvent.setup()
    const lines = reportedFirst()
    const { props } = renderView(lines)

    await openSheet(user)
    await user.click(within(sheet()).getByRole('button', { name: 'Edit report' }))
    await user.selectOptions(screen.getByLabelText('Issue type'), 'wrongVariant')
    await user.click(screen.getByRole('button', { name: 'Submit Issue' }))

    expect(props.onReportIssue).toHaveBeenCalledWith(lines[0].id, {
      issueType: 'wrongVariant',
      requiredQuantity: 4,
      foundQuantity: 3,
      note: 'Only 3 in the binder slot',
    })
  })

  it('changes nothing when the changed report is cancelled', async () => {
    const user = userEvent.setup()
    const { props } = renderView(reportedFirst())

    await openSheet(user)
    await user.click(within(sheet()).getByRole('button', { name: 'Edit report' }))
    await user.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(props.onReportIssue).not.toHaveBeenCalled()
    expect(props.onPicked).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Card Not Found' })).toBeInTheDocument()
  })

  it('offers only Close to someone who cannot record', async () => {
    const user = userEvent.setup()
    renderView(reportedFirst(), { canRecordOutcome: false, blockedReason: 'Not yours.' })

    await openSheet(user)

    expect(
      within(sheet())
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['Close'])
  })
})
