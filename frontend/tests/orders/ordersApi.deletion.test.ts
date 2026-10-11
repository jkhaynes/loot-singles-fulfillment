import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  OrderAlreadyDeletedError,
  OrderClaimedBeforeError,
  OrderNotFoundError,
  deleteAllOrders,
  deleteOrder,
  getDeletionSummary,
  getOrders,
  type OrderListItem,
} from '../../src/features/orders/ordersApi'

function mockFetch(status: number, body?: unknown) {
  const fetchMock = vi.fn().mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('getDeletionSummary', () => {
  it('GETs the summary and returns it with asOf as the server sent it', async () => {
    const summary = {
      asOf: '2026-10-11T12:34:56.1234567Z',
      deletableCount: 5,
      keptCount: 2,
      claimedCount: 1,
      withUnresolvedIssueCount: 3,
    }
    const fetchMock = mockFetch(200, summary)

    const result = await getDeletionSummary()

    expect(fetchMock).toHaveBeenCalledWith('/api/orders/deletion-summary', {
      credentials: 'include',
    })
    expect(result).toEqual(summary)
  })

  it('throws on a failed response', async () => {
    mockFetch(500)
    await expect(getDeletionSummary()).rejects.toThrow('status 500')
  })
})

describe('deleteOrder', () => {
  it('DELETEs the order and resolves on 204', async () => {
    const fetchMock = mockFetch(204)

    await expect(deleteOrder(42)).resolves.toBeUndefined()

    expect(fetchMock).toHaveBeenCalledWith('/api/orders/42', {
      method: 'DELETE',
      credentials: 'include',
    })
  })

  it('throws OrderNotFoundError on order_not_found', async () => {
    mockFetch(404, { error: 'order_not_found' })
    await expect(deleteOrder(42)).rejects.toBeInstanceOf(OrderNotFoundError)
  })

  it('throws OrderAlreadyDeletedError on order_already_deleted', async () => {
    mockFetch(404, { error: 'order_already_deleted' })
    await expect(deleteOrder(42)).rejects.toBeInstanceOf(OrderAlreadyDeletedError)
  })

  it('throws OrderClaimedBeforeError on order_claimed_before', async () => {
    mockFetch(409, { error: 'order_claimed_before' })
    await expect(deleteOrder(42)).rejects.toBeInstanceOf(OrderClaimedBeforeError)
  })

  it('throws a generic error on any other failure', async () => {
    mockFetch(500)
    await expect(deleteOrder(42)).rejects.toThrow('status 500')
  })
})

describe('deleteAllOrders', () => {
  it('POSTs asOf exactly as given and returns the counts', async () => {
    const fetchMock = mockFetch(200, { deletedCount: 4, keptCount: 1 })
    const asOf = '2026-10-11T12:34:56.1234567Z'

    const result = await deleteAllOrders(asOf)

    expect(fetchMock).toHaveBeenCalledWith('/api/orders/delete-all', {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ asOf }),
    })
    expect(result).toEqual({ deletedCount: 4, keptCount: 1 })
  })

  it('throws on invalid_as_of', async () => {
    mockFetch(400, { error: 'invalid_as_of' })
    await expect(deleteAllOrders('nonsense')).rejects.toThrow('status 400')
  })
})

describe('OrderListItem deletion fields', () => {
  it('carries canDelete and deleteBlockedReason from the list response', async () => {
    const items: OrderListItem[] = [
      {
        orderId: 1,
        tcgplayerOrderId: 'A-1',
        status: 'ready',
        importedAt: '2026-10-11T00:00:00Z',
        claimedByEmployeeId: null,
        claimedByEmployeeName: null,
        canDelete: true,
        deleteBlockedReason: null,
      },
      {
        orderId: 2,
        tcgplayerOrderId: 'A-2',
        status: 'picked',
        importedAt: '2026-10-11T00:00:00Z',
        claimedByEmployeeId: null,
        claimedByEmployeeName: null,
        canDelete: false,
        deleteBlockedReason: 'claimed_before',
      },
    ]
    mockFetch(200, items)

    const result = await getOrders()

    expect(result[0].canDelete).toBe(true)
    expect(result[0].deleteBlockedReason).toBeNull()
    expect(result[1].canDelete).toBe(false)
    expect(result[1].deleteBlockedReason).toBe('claimed_before')
  })
})
