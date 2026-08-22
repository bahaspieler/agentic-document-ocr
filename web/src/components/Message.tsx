import { CircleUserRound, Sparkles } from 'lucide-react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import rehypeSanitize from 'rehype-sanitize'
import type { Run } from '../api'
import type { ChatMessage } from '../types'
import { ActivityTimeline } from './ActivityTimeline'
import { ResultCard } from './ResultCard'

export function Message({ message, pending = false, onViewRun }: {
  message: ChatMessage; pending?: boolean; onViewRun?: (run: Run) => void
}) {
  const assistant = message.role === 'assistant'
  return (
    <article className={`message ${message.role}`}>
      <div className="message-avatar" aria-hidden="true">
        {assistant ? <Sparkles size={16} /> : <CircleUserRound size={17} />}
      </div>
      <div className="message-body">
        <header><strong>{assistant ? 'OCR Agent' : 'You'}</strong>
          <time dateTime={message.createdUtc}>{formatTime(message.createdUtc)}</time></header>
        {message.attachments?.length ? (
          <div className="sent-attachments">
            {message.attachments.map((attachment) => (
              <img key={attachment.reference} src={`/api/artifacts/${attachment.reference.split('/').map(encodeURIComponent).join('/')}`} alt={attachment.name ?? 'Attached image'} />
            ))}
          </div>
        ) : null}
        {assistant && message.events?.length ? <ActivityTimeline events={message.events} /> : null}
        <div className="message-content">
          {assistant ? (
            <ReactMarkdown remarkPlugins={[remarkGfm]} rehypePlugins={[rehypeSanitize]}>
              {message.text || (pending ? 'Thinking…' : '')}
            </ReactMarkdown>
          ) : <p>{message.text}</p>}
        </div>
        {message.runs?.map((run) => (
          <ResultCard key={run.runId} run={run} artifacts={message.artifacts?.filter((item) => item.runId === run.runId) ?? []} onViewRun={onViewRun} />
        ))}
      </div>
    </article>
  )
}

function formatTime(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '' : date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}
