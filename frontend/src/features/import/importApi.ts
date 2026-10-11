export type ImportStatus = 'inProgress' | 'completed' | 'failed' | 'interrupted' | 'cancelled'
// Per-order codes the UI knows by name. PDF imports carry further codes, so any string is allowed.
export type ImportFailureCode =
  'duplicateOrder' | 'incompleteOrder' | 'tcgplayerResponseInvalid' | (string & {})
export type ImportAttemptFailureCode =
  | 'summaryMismatch'
  | 'unreadablePdf'
  | 'tcgplayerNotConfigured'
  | 'tcgplayerUnavailable'
  | 'tcgplayerAccessRefused'
  | 'tcgplayerResponseInvalid'
export interface ImportOrderResult {
  sourceOrderIdentifier: string | null
  outcome: 'succeeded' | 'rejected'
  failureCode: ImportFailureCode | null
  failureMessage: string | null
  resultingOrderId: number | null
}
export interface ImportSnapshot {
  status: ImportStatus
  ordersDetected: number
  ordersProcessed: number
  succeededCount: number
  failedCount: number
  attemptFailureCode: ImportAttemptFailureCode | null
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
const tcgplayerErrors: Record<number, string> = {
  401: errors[401],
  500: errors[500],
}
export async function* importPackingSlip(
  file: File,
  signal?: AbortSignal,
): AsyncGenerator<ImportSnapshot> {
  const form = new FormData()
  form.append('file', file)
  yield* readImportStream(
    fetch('/api/imports', { method: 'POST', body: form, credentials: 'include', signal }),
    errors,
    signal,
  )
}
export async function* getNewOrdersFromTcgplayer(
  signal?: AbortSignal,
): AsyncGenerator<ImportSnapshot> {
  yield* readImportStream(
    fetch('/api/imports/tcgplayer', { method: 'POST', credentials: 'include', signal }),
    tcgplayerErrors,
    signal,
  )
}
async function* readImportStream(
  request: Promise<Response>,
  statusMessages: Record<number, string>,
  signal?: AbortSignal,
): AsyncGenerator<ImportSnapshot> {
  const response = await request
  if (!response.ok)
    throw new Error(
      statusMessages[response.status] ?? `The import request failed (status ${response.status}).`,
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
