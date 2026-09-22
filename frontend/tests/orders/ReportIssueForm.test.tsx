import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ReportIssueForm } from '../../src/features/orders/ReportIssueForm'
import type { PickingIssueDetail } from '../../src/features/orders/ordersApi'

const current: PickingIssueDetail = {
  issueType: 'wrongVariant',
  requiredQuantity: 4,
  foundQuantity: 3,
  note: 'Holo in the non-holo slot',
  reportedByEmployeeName: 'Sam',
  reportedAt: '2026-09-21T14:14:00Z',
}

// 018 FR-016. "Change report" edits the report the picker already made rather than asking them to
// enter it again (PRD §19.2: common reporting should not require unnecessary typing).
describe('ReportIssueForm', () => {
  it('starts from an existing report when given one', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn()
    render(
      <ReportIssueForm
        lineId={7}
        isSubmitting={false}
        initial={current}
        onCancel={vi.fn()}
        onSubmit={onSubmit}
      />,
    )

    expect(screen.getByLabelText('Issue type')).toHaveValue('wrongVariant')
    expect(screen.getByLabelText('Quantity required')).toHaveValue(4)
    expect(screen.getByLabelText('Quantity found')).toHaveValue(3)
    expect(screen.getByLabelText('Note (optional)')).toHaveValue('Holo in the non-holo slot')

    await user.click(screen.getByRole('button', { name: 'Submit Issue' }))

    expect(onSubmit).toHaveBeenCalledWith({
      issueType: 'wrongVariant',
      requiredQuantity: 4,
      foundQuantity: 3,
      note: 'Holo in the non-holo slot',
    })
  })

  // The desktop list passes no report, and its form must stay exactly as it was.
  it('starts blank without one', () => {
    render(
      <ReportIssueForm lineId={7} isSubmitting={false} onCancel={vi.fn()} onSubmit={vi.fn()} />,
    )

    expect(screen.getByLabelText('Issue type')).toHaveValue('cardNotFound')
    expect(screen.getByLabelText('Quantity required')).toHaveValue(null)
    expect(screen.getByLabelText('Quantity found')).toHaveValue(null)
    expect(screen.getByLabelText('Note (optional)')).toHaveValue('')
  })
})
