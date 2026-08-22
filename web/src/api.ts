export type RuntimeConfig = {
  auth: { enabled: boolean; clientId: string; authority: string }
  sqlEnabled: boolean
  agentEnabled: boolean
  model?: string
  modelName?: string
}
export type Conversation = { id: string; ownerUserId?: string; title: string; createdUtc: string; lastActiveUtc: string; turnCount: number }
export type ChatAttachment = { reference: string; name?: string; contentType?: string }
export type ArtifactDescriptor = { runId?: string; name: string; sizeBytes: number; lastModifiedUtc: string; contentType: string; previewable: boolean }
export type ActivityEvent = {
  type?: string
  text?: string
  toolName?: string
  message?: string
  step?: { completed?: number; total?: number; toolName?: string; inputReference?: string; outputReference?: string; durationMs?: number; error?: string }
  run?: Run
  artifact?: ArtifactDescriptor
}
export type ConversationTurn = {
  turnIndex: number; userMessage: string; answer: string; createdUtc: string
  events: ActivityEvent[]; runIds: string[]; attachments: ChatAttachment[]
}
export type Workflow = { id: string; name: string; description: string }
export type Run = {
  runId: string; mode: string; name: string; version?: string; source?: string
  conversationId?: string; inputReference: string; outputReference: string
  outputDirectory?: string; success: boolean; error?: string; startTimeUtc: string
  endTimeUtc?: string; durationMs: number; stepCount: number; storageProvider?: string
  argumentsJson?: string; traceJson?: string
}
export type ReceiptSummary = {
  receiptId: string; runId?: string; conversationId?: string; merchant?: string
  transactionUtc?: string; currency: string; subtotal?: number; tax?: number; total?: number
  ocrConfidence?: number; isValid: boolean; validationScore: number; createdUtc: string
  updatedUtc?: string; reviewStatus?: string; currentRevision?: number
}
export type ReceiptLineItem = {
  index: number; description: string; quantity?: number; unitPrice?: number
  lineTotal?: number; taxIndicator?: string; discountIndicator?: boolean
  [key: string]: unknown
}
export type ReceiptData = {
  receiptId?: string
  merchant: { name?: string; [key: string]: unknown }
  transaction: { dateTime?: string; [key: string]: unknown }
  totals: { currency: string; subtotal?: number; tax?: number; total?: number; [key: string]: unknown }
  items: ReceiptLineItem[]
  source?: Record<string, unknown>
  validation?: Record<string, unknown>
}
export type ReceiptRecord = ReceiptSummary & {
  rawJson: string; currentJson: string; updatedUtc: string; reviewStatus: string
  currentRevision: number; rowVersion: string
}
export type ReceiptRevision = {
  receiptId: string; revisionNumber: number; editorIdentity: string
  createdUtc: string; correctedJson: string; changeSummary?: string
}
export type WorkflowResult = {
  workflowId: string; version: string; inputImageRef: string; runId: string
  outputImageRef: string; outputDirectory: string; finalOutput?: string; receiptId?: string
  totalDurationMs: number; success: boolean; error?: string; status: string
  trace?: Array<Record<string, unknown>>; context?: Record<string, string>
}
export type WorkflowJobResponse = { result: WorkflowResult; artifacts: ArtifactDescriptor[] }
export type ToolJobResponse = { output: string }

export async function apiFetch<T>(
  path: string,
  token?: string,
  init?: RequestInit,
): Promise<T> {
  const headers = new Headers(init?.headers)
  if (token) headers.set('Authorization', `Bearer ${token}`)
  if (init?.body && !(init.body instanceof FormData)) headers.set('Content-Type', 'application/json')
  const response = await fetch(path, { ...init, headers })
  if (!response.ok) {
    const detail = await response.json().catch(() => null)
    throw new Error(detail?.error ?? detail?.detail ?? `Request failed (${response.status})`)
  }
  if (response.status === 204) return undefined as T
  return response.json()
}

export async function streamConversation(
  conversationId: string,
  message: string,
  token: string | undefined,
  attachments: ChatAttachment[],
  onEvent: (event: string, data: Record<string, unknown>) => void,
): Promise<void> {
  const response = await fetch(`/api/conversations/${conversationId}/messages`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: JSON.stringify({ message, idempotencyKey: crypto.randomUUID(), attachments }),
  })
  if (!response.ok || !response.body) {
    const detail = await response.json().catch(() => null)
    throw new Error(detail?.error ?? `Chat failed (${response.status})`)
  }

  const reader = response.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''
  while (true) {
    const { done, value } = await reader.read()
    buffer += decoder.decode(value, { stream: !done })
    const frames = buffer.split('\n\n')
    buffer = frames.pop() ?? ''
    for (const frame of frames) {
      let event = 'message'
      let data = ''
      for (const line of frame.split('\n')) {
        if (line.startsWith('event:')) event = line.slice(6).trim()
        if (line.startsWith('data:')) data += line.slice(5).trim()
      }
      if (data) onEvent(event, JSON.parse(data))
    }
    if (done) break
  }
}

export async function streamWorkflowJob(
  request: Record<string, unknown>,
  token: string | undefined,
  onEvent: (event: string, data: Record<string, unknown>) => void,
): Promise<void> {
  const response = await fetch('/api/jobs/stream', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: JSON.stringify(request),
  })
  if (!response.ok || !response.body) {
    const detail = await response.json().catch(() => null)
    throw new Error(detail?.error ?? `Workflow failed (${response.status})`)
  }
  const reader = response.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''
  while (true) {
    const { done, value } = await reader.read()
    buffer += decoder.decode(value, { stream: !done })
    const frames = buffer.split('\n\n')
    buffer = frames.pop() ?? ''
    for (const frame of frames) {
      let event = 'message'
      let data = ''
      for (const line of frame.split('\n')) {
        if (line.startsWith('event:')) event = line.slice(6).trim()
        if (line.startsWith('data:')) data += line.slice(5).trim()
      }
      if (data) onEvent(event, JSON.parse(data))
    }
    if (done) break
  }
}

export function artifactUrl(reference: string, conversationId?: string, download = false): string {
  const query = new URLSearchParams()
  if (conversationId) query.set('conversationId', conversationId)
  if (download) query.set('download', 'true')
  const suffix = query.size ? `?${query}` : ''
  return `/api/artifacts/${reference.split('/').map(encodeURIComponent).join('/')}${suffix}`
}

export function runArtifactUrl(runId: string, name: string, conversationId?: string, download = false) {
  const query = new URLSearchParams()
  if (conversationId) query.set('conversationId', conversationId)
  if (download) query.set('download', 'true')
  return `/api/runs/${encodeURIComponent(runId)}/artifacts/${encodeURIComponent(name)}?${query}`
}

export function receiptExportUrl(receiptId: string, format: 'original' | 'corrected.json' | 'corrected.csv', conversationId?: string) {
  const query = conversationId ? `?conversationId=${encodeURIComponent(conversationId)}` : ''
  return `/api/receipts/${encodeURIComponent(receiptId)}/${format}${query}`
}
