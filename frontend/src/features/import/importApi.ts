export type ImportStatus = 'inProgress' | 'completed' | 'failed' | 'interrupted' | 'cancelled'
export interface ImportOrderResult {
  sourceOrderIdentifier: string | null
  outcome: 'succeeded' | 'rejected'
  failureCode: string | null
  failureMessage: string | null
  resultingOrderId: number | null
}
export interface ImportSnapshot {
  status: ImportStatus
  ordersDetected: number
  ordersProcessed: number
  succeededCount: number
  failedCount: number
  attemptFailureCode: string | null
  attemptFailureMessage: string | null
  operationFailureMessage: string | null
  results: ImportOrderResult[]
}
const errors: Record<number, string> = {
  400: 'Select one valid PDF file.',
  401: 'Your session expired. Please log in again.',
  413: 'The PDF must be 25 MB or smaller.',
  415: 'The selected file must be a PDF.',
  500: 'The server could not start the import. Please retry.',
}
export async function* importPackingSlip(
  file: File,
  signal?: AbortSignal,
): AsyncGenerator<ImportSnapshot> {
  const form = new FormData()
  form.append('file', file)
  const response = await fetch('/api/imports', {
    method: 'POST',
    body: form,
    credentials: 'include',
    signal,
  })
  if (!response.ok)
    throw new Error(
      errors[response.status] ?? `The import request failed (status ${response.status}).`,
    )
  if (!response.body) throw new Error('The server returned no import stream.')
  const reader = response.body.pipeThrough(new TextDecoderStream()).getReader()
  let buffer = ''
  let last: ImportSnapshot | null = null
  try {
    while (true) {
      const { value, done } = await reader.read()
      buffer += value ?? ''
      const lines = buffer.split('\n')
      buffer = lines.pop() ?? ''
      if (done && buffer.trim()) lines.push(buffer)
      for (const line of lines) {
        if (!line.trim()) continue
        last = JSON.parse(line) as ImportSnapshot
        yield last
        if (last.status === 'completed' || last.status === 'failed') return
      }
      if (done) break
    }
  } catch (caught) {
    if (signal?.aborted) throw caught
    /* an incomplete stream is represented as Interrupted below */
  }
  yield {
    ...(last ?? {
      ordersDetected: 0,
      ordersProcessed: 0,
      succeededCount: 0,
      failedCount: 0,
      attemptFailureCode: null,
      attemptFailureMessage: null,
      operationFailureMessage: null,
      results: [],
    }),
    status: 'interrupted',
  }
}
