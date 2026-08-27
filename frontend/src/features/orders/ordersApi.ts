export interface OrderListItem {
  orderId: number
  tcgplayerOrderId: string
  status: string
  importedAt: string
  claimedByEmployeeId: number | null
  claimedByEmployeeName: string | null
}

export interface OrderLineDetail {
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
