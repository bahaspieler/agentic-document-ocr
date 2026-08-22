import { ChevronRight, Send, Sparkles, WandSparkles } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import type { PendingAttachment } from '../hooks/useImageAttachments'
import type { Run } from '../api'
import type { ChatMessage } from '../types'
import { AttachmentPicker } from './AttachmentPicker'
import { Message } from './Message'

export function ChatPanel({
  messages, draft, sending, hasConversation, attachments, onDraft, onSend, onAddFiles, onRemoveAttachment, onViewRun,
}: {
  messages: ChatMessage[]; draft: string; sending: boolean; hasConversation: boolean
  attachments: PendingAttachment[]; onDraft: (value: string) => void; onSend: () => void
  onAddFiles: (files: File[]) => void; onRemoveAttachment: (id: string) => void
  onViewRun?: (run: Run) => void
}) {
  const [dragging, setDragging] = useState(false)
  const bottomRef = useRef<HTMLDivElement>(null)
  useEffect(() => bottomRef.current?.scrollIntoView({ behavior: 'smooth' }), [messages])
  const canSend = (draft.trim() || attachments.some((item) => item.status === 'ready')) &&
    !sending && !attachments.some((item) => item.status === 'uploading')
  const suggestions = ['Extract this receipt and validate the totals', 'Assess the image quality before OCR', 'Explain the artifacts from my latest run']
  return (
    <section className={`chat-panel ${dragging ? 'is-dragging' : ''}`}
      onDragOver={(event) => { event.preventDefault(); setDragging(true) }}
      onDragLeave={(event) => { if (!event.currentTarget.contains(event.relatedTarget as Node)) setDragging(false) }}
      onDrop={(event) => { event.preventDefault(); setDragging(false); onAddFiles(Array.from(event.dataTransfer.files)) }}>
      {dragging && <div className="drop-overlay"><ImageDropIcon /> Drop images to attach</div>}
      <div className="message-scroll">
        {!messages.length && <div className="welcome">
          <div className="welcome-icon"><WandSparkles size={26} /></div>
          <span className="eyebrow">OCR PREPROCESSING AGENT</span>
          <h2>Turn difficult scans into<br />reliable data.</h2>
          <p>Attach one or more receipt images, then ask the agent to assess, improve, extract, or explain.</p>
          <div className="suggestions">{suggestions.map((suggestion) =>
            <button key={suggestion} onClick={() => onDraft(suggestion)}><Sparkles size={15} />{suggestion}<ChevronRight size={15} /></button>)}</div>
        </div>}
        {messages.map((message, index) =>
          <Message key={message.id} message={message} pending={sending && index === messages.length - 1} onViewRun={onViewRun} />)}
        <div ref={bottomRef} />
      </div>
      <div className="composer-wrap">
        <div className="composer">
          <AttachmentPicker items={attachments} onAdd={onAddFiles} onRemove={onRemoveAttachment} />
          <textarea value={draft} onChange={(event) => onDraft(event.target.value)}
            onPaste={(event) => {
              const files = Array.from(event.clipboardData.items).filter((item) => item.kind === 'file')
                .map((item) => item.getAsFile()).filter((file): file is File => Boolean(file))
              if (files.length) onAddFiles(files)
            }}
            onKeyDown={(event) => { if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); if (canSend) onSend() } }}
            placeholder={hasConversation ? 'Ask a follow-up…' : 'Ask the OCR agent…'} rows={1} aria-label="Message" />
          <button className="send-button" aria-label="Send message" disabled={!canSend} onClick={onSend}><Send size={17} /></button>
        </div>
        <small>Paste or drop images · Enter to send · Shift + Enter for a new line</small>
      </div>
    </section>
  )
}

function ImageDropIcon() { return <WandSparkles size={28} aria-hidden="true" /> }
