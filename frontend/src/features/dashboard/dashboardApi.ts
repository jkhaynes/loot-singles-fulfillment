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
}

export async function getDashboard(): Promise<DashboardData> {
  const response = await fetch('/api/dashboard', { credentials: 'include' })

  if (!response.ok) {
    throw new Error(`Failed to load dashboard (status ${response.status})`)
  }

  return (await response.json()) as DashboardData
}
