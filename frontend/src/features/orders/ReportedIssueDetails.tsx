import { pickingIssueTypeLabel } from './ordersApi'
import type { PickingIssueDetail } from './ordersApi'

/**
 * What a report holds, shown the same way on the phone's sheet and the desktop's issue panel (018
 * FR-010–FR-013, FR-022). Each view supplies its own frame and class; the rules live here once.
 *
 * Nothing is invented where the report holds nothing: no counts line unless both counts were
 * recorded, no note line without a note, and the time alone when the reporter is unknown. Counts use
 * the issue form's own words, because what "found" means depends on the issue type. For a damaged
 * card it may mean found in good condition, so nothing here says "pulled" or "short".
 */
export function ReportedIssueDetails({
  issue,
  className,
}: {
  issue: PickingIssueDetail
  className?: string
}) {
  const { requiredQuantity: required, foundQuantity: found } = issue
  const when = new Date(issue.reportedAt).toLocaleString()

  return (
    <dl className={className}>
      <div>
        <dt>Problem</dt>
        <dd>{pickingIssueTypeLabel(issue.issueType)}</dd>
      </div>
      {required !== null && found !== null && (
        <div>
          <dt>Counts</dt>
          <dd>{`Required ${required} · Found ${found}`}</dd>
        </div>
      )}
      {issue.note && (
        <div>
          <dt>Note</dt>
          <dd>{issue.note}</dd>
        </div>
      )}
      <div>
        <dt>Reported</dt>
        <dd>{issue.reportedByEmployeeName ? `${issue.reportedByEmployeeName} · ${when}` : when}</dd>
      </div>
    </dl>
  )
}
