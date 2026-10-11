import { describe, expect, it, vi } from 'vitest'
import { getNewOrdersFromTcgplayer, importPackingSlip } from '../../src/features/import/importApi'

function response(body: string, status = 200) {
  return new Response(body, {
    status,
    headers: {
      'Content-Type': status === 200 ? 'application/x-ndjson' : 'application/problem+json',
    },
  })
}

describe('importPackingSlip', () => {
  it('forwards the abort signal and preserves an intentional AbortError', async () => {
    const controller = new AbortController()
    const abortError = new DOMException('The operation was aborted.', 'AbortError')
    const fetchMock = vi.fn().mockRejectedValue(abortError)
    vi.stubGlobal('fetch', fetchMock)

    const consume = async () => {
      for await (const _ of importPackingSlip(
        new File(['pdf'], 'orders.pdf', { type: 'application/pdf' }),
        controller.signal,
      ))
        void _
    }

    controller.abort()
    await expect(consume()).rejects.toBe(abortError)
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/imports',
      expect.objectContaining({ signal: controller.signal }),
    )
  })

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

describe('getNewOrdersFromTcgplayer', () => {
  const inProgress = {
    status: 'inProgress',
    ordersDetected: 2,
    ordersProcessed: 1,
    succeededCount: 1,
    failedCount: 0,
    results: [],
  }

  it('POSTs to the TCGplayer route with credentials and the signal, and yields snapshots', async () => {
    const controller = new AbortController()
    const fetchMock = vi
      .fn()
      .mockResolvedValue(
        response(
          [
            JSON.stringify(inProgress),
            JSON.stringify({ ...inProgress, status: 'completed', ordersProcessed: 2 }),
            JSON.stringify({ ...inProgress, status: 'failed', ordersProcessed: 99 }),
          ].join('\n'),
        ),
      )
    vi.stubGlobal('fetch', fetchMock)
    const seen = []
    for await (const snapshot of getNewOrdersFromTcgplayer(controller.signal)) seen.push(snapshot)
    expect(fetchMock).toHaveBeenCalledWith('/api/imports/tcgplayer', {
      method: 'POST',
      credentials: 'include',
      signal: controller.signal,
    })
    expect(seen.map((item) => item.ordersProcessed)).toEqual([1, 2])
  })

  it('surfaces an attempt-wide failure snapshot as the terminal snapshot', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        response(
          JSON.stringify({
            ...inProgress,
            status: 'failed',
            attemptFailureCode: 'tcgplayerNotConfigured',
            attemptFailureMessage: 'Not set up here.',
          }),
        ),
      ),
    )
    const seen = []
    for await (const snapshot of getNewOrdersFromTcgplayer()) seen.push(snapshot)
    expect(seen).toHaveLength(1)
    expect(seen[0]).toMatchObject({
      status: 'failed',
      attemptFailureCode: 'tcgplayerNotConfigured',
    })
  })

  it('derives Interrupted and retains the last snapshot when EOF arrives early', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response(JSON.stringify(inProgress))))
    const seen = []
    for await (const snapshot of getNewOrdersFromTcgplayer()) seen.push(snapshot)
    expect(seen.at(-1)).toMatchObject({ status: 'interrupted', ordersProcessed: 1 })
  })

  it('preserves an intentional AbortError', async () => {
    const controller = new AbortController()
    const abortError = new DOMException('The operation was aborted.', 'AbortError')
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(abortError))
    const consume = async () => {
      for await (const _ of getNewOrdersFromTcgplayer(controller.signal)) void _
    }
    controller.abort()
    await expect(consume()).rejects.toBe(abortError)
  })

  it('rethrows an abort that happens mid-stream instead of reporting Interrupted', async () => {
    const controller = new AbortController()
    const abortError = new DOMException('The operation was aborted.', 'AbortError')
    const encoder = new TextEncoder()
    const body = new ReadableStream<Uint8Array>({
      start(stream) {
        stream.enqueue(encoder.encode(JSON.stringify(inProgress) + '\n'))
      },
      pull() {
        controller.abort()
        throw abortError
      },
    })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(body, { status: 200 })))
    const seen = []
    const consume = async () => {
      for await (const snapshot of getNewOrdersFromTcgplayer(controller.signal)) seen.push(snapshot)
    }
    await expect(consume()).rejects.toBe(abortError)
    expect(seen).toHaveLength(1)
  })

  it.each([
    [401, 'log in'],
    [500, 'server'],
  ])('maps HTTP %s to a distinguishable message', async (status, message) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response('{}', status)))
    const consume = async () => {
      for await (const _ of getNewOrdersFromTcgplayer()) void _
    }
    await expect(consume()).rejects.toThrow(message)
  })
})
