import { describe, expect, it, vi } from 'vitest'
import { importPackingSlip } from '../../src/features/import/importApi'

function response(body: string, status = 200) {
  return new Response(body, {
    status,
    headers: {
      'Content-Type': status === 200 ? 'application/x-ndjson' : 'application/problem+json',
    },
  })
}

describe('importPackingSlip', () => {
  it('replaces state for each line and stops at a terminal snapshot', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        response(
          [
            JSON.stringify({
              status: 'inProgress',
              ordersDetected: 2,
              ordersProcessed: 1,
              succeededCount: 1,
              failedCount: 0,
              results: [],
            }),
            JSON.stringify({
              status: 'completed',
              ordersDetected: 2,
              ordersProcessed: 2,
              succeededCount: 2,
              failedCount: 0,
              results: [],
            }),
            JSON.stringify({
              status: 'failed',
              ordersDetected: 99,
              ordersProcessed: 99,
              succeededCount: 0,
              failedCount: 99,
              results: [],
            }),
          ].join('\n'),
        ),
      ),
    )
    const seen = []
    for await (const snapshot of importPackingSlip(
      new File(['pdf'], 'orders.pdf', { type: 'application/pdf' }),
    ))
      seen.push(snapshot)
    expect(seen.map((item) => item.ordersProcessed)).toEqual([1, 2])
  })

  it('derives Interrupted and retains the last snapshot when EOF arrives early', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        response(
          JSON.stringify({
            status: 'inProgress',
            ordersDetected: 2,
            ordersProcessed: 1,
            succeededCount: 1,
            failedCount: 0,
            results: [],
          }),
        ),
      ),
    )
    const seen = []
    for await (const snapshot of importPackingSlip(
      new File(['pdf'], 'orders.pdf', { type: 'application/pdf' }),
    ))
      seen.push(snapshot)
    expect(seen.at(-1)).toMatchObject({
      status: 'interrupted',
      ordersProcessed: 1,
    })
  })

  it.each([
    [400, 'valid PDF'],
    [401, 'log in'],
    [413, '25 MB'],
    [415, 'PDF'],
    [500, 'server'],
  ])('maps HTTP %s to a distinguishable message', async (status, message) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response('{}', status)))
    const consume = async () => {
      for await (const _ of importPackingSlip(new File([], 'x.pdf'))) void _
    }
    await expect(consume()).rejects.toThrow(message)
  })
})
