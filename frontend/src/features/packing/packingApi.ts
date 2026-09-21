import type { LabelContributor } from '../orders/ordersApi'

/** What the packing desk shows for one resolved order (FR-025). */
export interface PackingView {
  orderId: number
  tcgplayerOrderId: string
  cardCount: number
  /** Every contributor — the desk has no size constraint, unlike the label (FR-043). */
  pickedBy: LabelContributor[]
  pickedAt: string | null
  status: string
  canPack: boolean
  blockedReason: string | null
  unresolvedProducts: string[]
  hasPackingSlip: boolean
}

export class PackingOrderNotFoundError extends Error {
  constructor() {
    super('That code matches no order')
    this.name = 'PackingOrderNotFoundError'
  }
}

export class OrderAlreadyPackedError extends Error {
  constructor() {
    super('This order has already been packed')
    this.name = 'OrderAlreadyPackedError'
  }
}

/** Blocked by a picking issue, carrying the products so the desk can name them (FR-028). */
export class OrderHasUnresolvedIssueError extends Error {
  unresolvedProducts: string[]

  constructor(unresolvedProducts: string[]) {
    super('This order still has an unresolved product')
    this.name = 'OrderHasUnresolvedIssueError'
    this.unresolvedProducts = unresolvedProducts
  }
}

export async function resolvePackingCode(code: string): Promise<PackingView> {
  const response = await fetch(`/api/packing/orders/${encodeURIComponent(code)}`, {
    credentials: 'include',
  })

  if (response.status === 404) {
    throw new PackingOrderNotFoundError()
  }

  if (!response.ok) {
    throw new Error(`Couldn't look that order up (status ${response.status})`)
  }

  return (await response.json()) as PackingView
}

export async function getAwaitingPacking(): Promise<PackingView[]> {
  const response = await fetch('/api/packing/awaiting', { credentials: 'include' })

  if (!response.ok) {
    throw new Error(`Couldn't load what's awaiting packing (status ${response.status})`)
  }

  return (await response.json()) as PackingView[]
}

export async function markPacked(orderId: number): Promise<PackingView> {
  const response = await fetch(`/api/orders/${orderId}/packed`, {
    method: 'POST',
    credentials: 'include',
  })

  if (response.status === 404) {
    throw new PackingOrderNotFoundError()
  }

  if (response.status === 409) {
    const body = (await response.json()) as {
      error?: string
      unresolvedProducts?: string[]
    }

    if (body.error === 'order_has_unresolved_issue') {
      throw new OrderHasUnresolvedIssueError(body.unresolvedProducts ?? [])
    }

    throw new OrderAlreadyPackedError()
  }

  if (!response.ok) {
    throw new Error(`Couldn't mark that order packed (status ${response.status})`)
  }

  return (await response.json()) as PackingView
}

/** Where the slip is fetched from. Opened in a tab so the browser's own PDF viewer prints it. */
export function packingSlipUrl(orderId: number): string {
  return `/api/orders/${orderId}/packing-slip`
}
