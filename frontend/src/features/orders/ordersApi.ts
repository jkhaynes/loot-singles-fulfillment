export interface OrderListItem {
  orderId: number
  tcgplayerOrderId: string
  status: string
  importedAt: string
  claimedByEmployeeId: number | null
  claimedByEmployeeName: string | null
}

export type PickOutcome = 'picked' | 'hasIssue'

/** Human-readable label for an order status, so no screen renders the raw enum value. */
export function orderStatusLabel(status: string): string {
  switch (status) {
    case 'ready':
      return 'Ready'
    case 'inProgress':
      return 'In Progress'
    case 'picked':
      return 'Picked'
    case 'needsAttention':
      return 'Needs Attention'
    default:
      return status
  }
}

export type PickingIssueType =
  | 'cardNotFound'
  | 'insufficientQuantity'
  | 'wrongCardInLocation'
  | 'wrongVariant'
  | 'wrongCondition'
  | 'damaged'
  | 'inventoryDiscrepancy'
  | 'informationIncorrect'
  | 'other'

/** Issue types a picker can report, in the order they appear in the form (PRD §19.3). */
export const pickingIssueTypes: { value: PickingIssueType; label: string }[] = [
  { value: 'cardNotFound', label: 'Card Not Found' },
  { value: 'insufficientQuantity', label: 'Insufficient Quantity' },
  { value: 'wrongCardInLocation', label: 'Wrong Card In Location' },
  { value: 'wrongVariant', label: 'Wrong Variant' },
  { value: 'wrongCondition', label: 'Wrong Condition' },
  { value: 'damaged', label: 'Damaged' },
  { value: 'inventoryDiscrepancy', label: 'Inventory Discrepancy' },
  { value: 'informationIncorrect', label: 'Information Incorrect' },
  { value: 'other', label: 'Other' },
]

export function pickingIssueTypeLabel(issueType: string): string {
  return pickingIssueTypes.find((type) => type.value === issueType)?.label ?? issueType
}

export interface PickingIssueDetail {
  issueType: PickingIssueType
  requiredQuantity: number | null
  foundQuantity: number | null
  note: string | null
  reportedByEmployeeName: string | null
  reportedAt: string
}

export interface ReportIssueRequest {
  issueType: PickingIssueType
  requiredQuantity: number | null
  foundQuantity: number | null
  note: string | null
}

export interface OrderLineDetail {
  id: number
  pickOutcome: PickOutcome | null
  currentIssue: PickingIssueDetail | null
  productName: string
  productLine: string
  set: string
  collectorNumber: string
  rarity: string | null
  variant: string | null
  condition: string
  quantity: number
  imageUrl: string | null
}

export interface OrderDetail {
  orderId: number
  tcgplayerOrderId: string
  status: string
  lines: OrderLineDetail[]
  claimedByEmployeeId: number | null
  claimedByEmployeeName: string | null
}

export interface OrderClaimUpdate {
  orderId: number
  tcgplayerOrderId: string
  status: string
  claimedByEmployeeId: number | null
  claimedByEmployeeName: string | null
}

export class OrderNotFoundError extends Error {
  constructor() {
    super('Order not found')
    this.name = 'OrderNotFoundError'
  }
}

export class NotYourClaimError extends Error {
  constructor() {
    super('This order is not claimed by you')
    this.name = 'NotYourClaimError'
  }
}

export class OrderNotClaimedError extends Error {
  constructor() {
    super('This order is no longer claimed by anyone')
    this.name = 'OrderNotClaimedError'
  }
}

export class NoOrdersAvailableError extends Error {
  constructor() {
    super('No orders are currently available')
    this.name = 'NoOrdersAvailableError'
  }
}

export class EmployeeHasActiveClaimError extends Error {
  claimedOrderId: number | null

  constructor(claimedOrderId: number | null) {
    super('You already have an order claimed')
    this.name = 'EmployeeHasActiveClaimError'
    this.claimedOrderId = claimedOrderId
  }
}

export class OrderAlreadyClaimedError extends Error {
  claimedByEmployeeName: string | null

  constructor(claimedByEmployeeName: string | null) {
    super('This order is already claimed')
    this.name = 'OrderAlreadyClaimedError'
    this.claimedByEmployeeName = claimedByEmployeeName
  }
}

export async function getOrders(): Promise<OrderListItem[]> {
  const response = await fetch('/api/orders', { credentials: 'include' })

  if (!response.ok) {
    throw new Error(`Failed to load orders (status ${response.status})`)
  }

  return (await response.json()) as OrderListItem[]
}

export async function getOrderDetail(orderId: number): Promise<OrderDetail> {
  const response = await fetch(`/api/orders/${orderId}`, { credentials: 'include' })

  if (response.status === 404) {
    throw new OrderNotFoundError()
  }

  if (!response.ok) {
    throw new Error(`Failed to load order (status ${response.status})`)
  }

  return (await response.json()) as OrderDetail
}

export async function recordPicked(orderId: number, lineId: number): Promise<OrderDetail> {
  const response = await fetch(`/api/orders/${orderId}/lines/${lineId}/pick`, {
    method: 'POST',
    credentials: 'include',
  })

  if (response.status === 404) {
    throw new OrderNotFoundError()
  }

  if (response.status === 409) {
    throw new NotYourClaimError()
  }

  if (!response.ok) {
    throw new Error(`Failed to record pick (status ${response.status})`)
  }

  return (await response.json()) as OrderDetail
}

export async function reportIssue(
  orderId: number,
  lineId: number,
  request: ReportIssueRequest,
): Promise<OrderDetail> {
  const response = await fetch(`/api/orders/${orderId}/lines/${lineId}/report-issue`, {
    method: 'POST',
    credentials: 'include',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  })

  if (response.status === 404) {
    throw new OrderNotFoundError()
  }

  if (response.status === 409) {
    throw new NotYourClaimError()
  }

  if (!response.ok) {
    throw new Error(`Failed to report issue (status ${response.status})`)
  }

  return (await response.json()) as OrderDetail
}

interface ClaimConflictBody {
  error: string
  claimedOrderId?: number | null
  claimedByEmployeeName?: string | null
}

export async function pickNextOrder(): Promise<OrderClaimUpdate> {
  const response = await fetch('/api/orders/pick-next', {
    method: 'POST',
    credentials: 'include',
  })

  if (response.status === 409) {
    const body = (await response.json()) as ClaimConflictBody
    if (body.error === 'no_orders_available') {
      throw new NoOrdersAvailableError()
    }
    throw new EmployeeHasActiveClaimError(body.claimedOrderId ?? null)
  }

  if (!response.ok) {
    throw new Error(`Failed to pick next order (status ${response.status})`)
  }

  return (await response.json()) as OrderClaimUpdate
}

export async function claimOrder(orderId: number): Promise<OrderClaimUpdate> {
  const response = await fetch(`/api/orders/${orderId}/claim`, {
    method: 'POST',
    credentials: 'include',
  })

  if (response.status === 404) {
    throw new OrderNotFoundError()
  }

  if (response.status === 409) {
    const body = (await response.json()) as ClaimConflictBody
    if (body.error === 'order_already_claimed') {
      throw new OrderAlreadyClaimedError(body.claimedByEmployeeName ?? null)
    }
    throw new EmployeeHasActiveClaimError(body.claimedOrderId ?? null)
  }

  if (!response.ok) {
    throw new Error(`Failed to claim order (status ${response.status})`)
  }

  return (await response.json()) as OrderClaimUpdate
}

export async function releaseOrder(orderId: number): Promise<OrderClaimUpdate> {
  const response = await fetch(`/api/orders/${orderId}/release`, {
    method: 'POST',
    credentials: 'include',
  })

  if (response.status === 404) {
    throw new OrderNotFoundError()
  }

  if (response.status === 409) {
    throw new NotYourClaimError()
  }

  if (!response.ok) {
    throw new Error(`Failed to release order (status ${response.status})`)
  }

  return (await response.json()) as OrderClaimUpdate
}

export async function forceReleaseOrder(orderId: number): Promise<OrderClaimUpdate> {
  const response = await fetch(`/api/orders/${orderId}/force-release`, {
    method: 'POST',
    credentials: 'include',
  })

  if (response.status === 404) {
    throw new OrderNotFoundError()
  }

  if (response.status === 409) {
    throw new OrderNotClaimedError()
  }

  if (!response.ok) {
    throw new Error(`Failed to force-release order (status ${response.status})`)
  }

  return (await response.json()) as OrderClaimUpdate
}
