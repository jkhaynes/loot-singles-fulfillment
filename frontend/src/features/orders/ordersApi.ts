export interface OrderListItem {
  orderId: number
  tcgplayerOrderId: string
  status: string
  importedAt: string
}

export async function getOrders(): Promise<OrderListItem[]> {
  const response = await fetch('/api/orders', { credentials: 'include' })

  if (!response.ok) {
    throw new Error(`Failed to load orders (status ${response.status})`)
  }

  return (await response.json()) as OrderListItem[]
}
