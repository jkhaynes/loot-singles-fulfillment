export interface OrderSummary {
  orderId: number
  tcgplayerOrderId: string
  productCount: number
  totalQuantity: number
}

export interface NeedsAttentionOrderSummary extends OrderSummary {
  flaggedProductNames: string[]
}

export interface OrderSection {
  count: number
  orders: OrderSummary[]
}

export interface NeedsAttentionSection {
  count: number
  orders: NeedsAttentionOrderSummary[]
}

export interface DashboardData {
  ready: OrderSection
  inProgress: OrderSection
  needsAttention: NeedsAttentionSection
  picked: OrderSection
  /**
   * The order the signed-in employee already holds, or null (016-mobile-picking FR-026).
   *
   * Computed server-side from the claim itself, not by filtering a section: an order keeps its
   * claim when an issue is reported or when it is fully picked, so a held order can be sitting
   * in Needs Attention or Picked. Never another employee's order.
   */
  activeClaim: OrderSummary | null
}

export async function getDashboard(): Promise<DashboardData> {
  const response = await fetch('/api/dashboard', { credentials: 'include' })

  if (!response.ok) {
    throw new Error(`Failed to load dashboard (status ${response.status})`)
  }

  return (await response.json()) as DashboardData
}
