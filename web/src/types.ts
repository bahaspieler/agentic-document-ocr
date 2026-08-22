import type { ActivityEvent, ArtifactDescriptor, ChatAttachment, Run } from './api'

export type MessageArtifact = ArtifactDescriptor & { runId: string; conversationId?: string }
export type ChatMessage = {
  id: string
  role: 'user' | 'assistant'
  text: string
  createdUtc: string
  attachments?: ChatAttachment[]
  events?: ActivityEvent[]
  runs?: Run[]
  artifacts?: MessageArtifact[]
}
