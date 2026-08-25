export interface OrderListItem {
  orderId: number
  tcgplayerOrderId: string
  status: string
  importedAt: string
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
}

export class OrderNotFoundError extends Error {
  constructor() {
    super('Order not found')
    this.name = 'OrderNotFoundError'
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
