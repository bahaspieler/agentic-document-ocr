export type Conversation = {
  id: string
  ownerUserId?: string
  title: string
  createdUtc: string
  lastActiveUtc: string
  turnCount: number
}

export type ConversationTurn = {
  turnIndex: number
  userMessage: string
  answer: string
  createdUtc: string
}

export type Workflow = { id: string; name: string; description: string }

export type Run = {
  runId: string
  mode: string
  name: string
  conversationId?: string
  inputReference: string
  outputReference: string
  success: boolean
  error?: string
  startTimeUtc: string
  durationMs: number
  stepCount: number
}

export type Receipt = {
  receiptId: string
  runId?: string
  merchant?: string
  transactionUtc?: string
  currency: string
  total?: number
  isValid: boolean
  validationScore: number
}

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
  onEvent: (event: string, data: Record<string, unknown>) => void,
): Promise<void> {
  const response = await fetch(`/api/conversations/${conversationId}/messages`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: JSON.stringify({ message, idempotencyKey: crypto.randomUUID() }),
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

export function artifactUrl(reference: string): string {
  return `/api/artifacts/${reference.split('/').map(encodeURIComponent).join('/')}`
}
