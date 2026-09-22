import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ReportedIssueDetails } from '../../src/features/orders/ReportedIssueDetails'
import { pickingIssueTypes } from '../../src/features/orders/ordersApi'
import type { PickingIssueDetail } from '../../src/features/orders/ordersApi'

// 018 (amended 2026-09-22). One set of rules for what a report shows, on the phone's sheet and the
// desktop's issue panel alike. The design must hold for every issue type: counts appear in the
// issue form's own words, and nothing assumes the problem was a shortage.
const report: PickingIssueDetail = {
  issueType: 'cardNotFound',
  requiredQuantity: 4,
  foundQuantity: 3,
  note: 'Only 3 in the binder slot',
  reportedByEmployeeName: 'Sam',
  reportedAt: '2026-09-22T10:51:00Z',
}
const when = new Date(report.reportedAt).toLocaleString()

describe('ReportedIssueDetails', () => {
  it('names the issue type', () => {
    render(<ReportedIssueDetails issue={report} />)

    expect(screen.getByText('Card Not Found')).toBeInTheDocument()
  })

  it('shows the counts in the form’s own words when both were recorded', () => {
    render(<ReportedIssueDetails issue={report} />)

    expect(screen.getByText('Required 4 · Found 3')).toBeInTheDocument()
  })

  it('omits the counts when only one was recorded', () => {
    const { container } = render(
      <ReportedIssueDetails issue={{ ...report, requiredQuantity: null }} />,
    )

    expect(container).not.toHaveTextContent(/Counts|Required/)
  })

  it('omits the counts when neither was recorded', () => {
    const { container } = render(
      <ReportedIssueDetails issue={{ ...report, requiredQuantity: null, foundQuantity: null }} />,
    )

    expect(container).not.toHaveTextContent(/Counts|Required/)
  })

  it('shows the note when there is one, and nothing when there is not', () => {
    const { rerender, container } = render(<ReportedIssueDetails issue={report} />)
    expect(screen.getByText('Only 3 in the binder slot')).toBeInTheDocument()

    rerender(<ReportedIssueDetails issue={{ ...report, note: null }} />)
    expect(container).not.toHaveTextContent('Only 3 in the binder slot')
    expect(container).not.toHaveTextContent(/Note/)
  })

  it('names the reporter with the time, or gives the time alone', () => {
    const { rerender } = render(<ReportedIssueDetails issue={report} />)
    expect(screen.getByText(`Sam · ${when}`)).toBeInTheDocument()

    rerender(<ReportedIssueDetails issue={{ ...report, reportedByEmployeeName: null }} />)
    expect(screen.getByText(when)).toBeInTheDocument()
  })

  // SC-001. The first design said "3 of 4 pulled · 1 short", which only makes sense for a missing
  // card. It must read correctly whatever was reported.
  it.each(pickingIssueTypes.map((type) => [type.label, type.value] as const))(
    'says nothing about "pulled" or "short" for %s',
    (_label, issueType) => {
      const { container } = render(<ReportedIssueDetails issue={{ ...report, issueType }} />)

      expect(container).not.toHaveTextContent(/pulled|short/i)
    },
  )
})
