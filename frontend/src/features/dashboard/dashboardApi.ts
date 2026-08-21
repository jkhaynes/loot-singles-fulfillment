export interface OrderSummary {
  orderId: number
  tcgplayerOrderId: string
  productCount: number
  totalQuantity: number
}

export interface ReadySection {
  count: number
  orders: OrderSummary[]
}

export interface DashboardData {
  ready: ReadySection
}

export async function getDashboard(): Promise<DashboardData> {
  const response = await fetch('/api/dashboard', { credentials: 'include' })

  if (!response.ok) {
    throw new Error(`Failed to load dashboard (status ${response.status})`)
  }

  return (await response.json()) as DashboardData
}
