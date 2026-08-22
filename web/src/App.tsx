import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useMsal } from '@azure/msal-react'
import {
  Bot, Check, ChevronRight, CircleUserRound, Clock3, FileImage, History,
  Image as ImageIcon, LogIn, LogOut, Menu, MessageSquareText, MoreHorizontal,
  PanelLeftClose, Plus, ReceiptText, Send, Sparkles, Trash2, Upload, WandSparkles, X,
} from 'lucide-react'
import './App.css'
import {
  apiFetch, artifactUrl, streamConversation,
  type Conversation, type ConversationTurn, type Receipt, type Run, type Workflow,
} from './api'
import type { RuntimeConfig } from './main'

type View = 'chat' | 'process' | 'history'
type ChatMessage = { role: 'user' | 'assistant'; text: string; events?: string[] }
const anonymousKey = 'ocr-anonymous-conversations'

function App({ config }: { config: RuntimeConfig }) {
  const { instance, accounts } = useMsal()
  const account = accounts[0]
  const [view, setView] = useState<View>('chat')
  const [sidebarOpen, setSidebarOpen] = useState(true)
  const [conversations, setConversations] = useState<Conversation[]>([])
  const [activeConversation, setActiveConversation] = useState<string>()
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [draft, setDraft] = useState('')
  const [sending, setSending] = useState(false)
  const [workflows, setWorkflows] = useState<Workflow[]>([])
  const [runs, setRuns] = useState<Run[]>([])
  const [receipts, setReceipts] = useState<Receipt[]>([])
  const [selectedArtifact, setSelectedArtifact] = useState<string>()
  const [notice, setNotice] = useState<string>()
  const bottomRef = useRef<HTMLDivElement>(null)

  const getToken = useCallback(async () => {
    if (!config.auth.enabled || !account) return undefined
    const scope = `api://${config.auth.clientId}/access_as_user`
    const result = await instance.acquireTokenSilent({ account, scopes: [scope] })
    return result.accessToken
  }, [account, config.auth.clientId, config.auth.enabled, instance])

  const saveAnonymous = useCallback((items: Conversation[]) => {
    localStorage.setItem(anonymousKey, JSON.stringify(items))
  }, [])

  const refreshConversations = useCallback(async () => {
    try {
      if (account) {
        const token = await getToken()
        setConversations(await apiFetch<Conversation[]>('/api/conversations', token))
      } else {
        const local = JSON.parse(localStorage.getItem(anonymousKey) ?? '[]') as Conversation[]
        setConversations(local)
      }
    } catch (error) {
      setNotice((error as Error).message)
    }
  }, [account, getToken])

  const refreshData = useCallback(async () => {
    const token = await getToken()
    const [workflowRows, runRows, receiptRows] = await Promise.all([
      apiFetch<Workflow[]>('/api/workflows', token),
      apiFetch<Run[]>('/api/runs?top=100', token),
      apiFetch<Receipt[]>('/api/receipts?top=100', token),
    ])
    setWorkflows(workflowRows)
    setRuns(runRows)
    setReceipts(receiptRows)
  }, [getToken])

  useEffect(() => {
    void refreshConversations()
    void refreshData().catch((error) => setNotice(error.message))
  }, [refreshConversations, refreshData])

  useEffect(() => {
    if (!account) return
    const anonymous = JSON.parse(localStorage.getItem(anonymousKey) ?? '[]') as Conversation[]
    if (!anonymous.length) return
    void getToken().then((token) =>
      apiFetch<void>('/api/conversations/claim', token, {
        method: 'POST',
        body: JSON.stringify({ conversationIds: anonymous.map((item) => item.id) }),
      }),
    ).then(() => {
      localStorage.removeItem(anonymousKey)
      return refreshConversations()
    }).catch((error) => setNotice(error.message))
  }, [account, getToken, refreshConversations])

  useEffect(() => bottomRef.current?.scrollIntoView({ behavior: 'smooth' }), [messages])

  const selectConversation = async (id: string) => {
    setActiveConversation(id)
    setView('chat')
    setMessages([])
    try {
      const token = await getToken()
      const detail = await apiFetch<{ turns: ConversationTurn[] }>(
        `/api/conversations/${id}`, token,
      )
      setMessages(detail.turns.flatMap((turn) => [
        { role: 'user' as const, text: turn.userMessage },
        { role: 'assistant' as const, text: turn.answer },
      ]))
    } catch (error) {
      setNotice((error as Error).message)
    }
  }

  const createConversation = async () => {
    try {
      const token = await getToken()
      const conversation = await apiFetch<Conversation>('/api/conversations', token, {
        method: 'POST',
        body: JSON.stringify({ title: 'New conversation' }),
      })
      const next = [conversation, ...conversations]
      setConversations(next)
      if (!account) saveAnonymous(next)
      setActiveConversation(conversation.id)
      setMessages([])
      setView('chat')
      return conversation.id
    } catch (error) {
      setNotice((error as Error).message)
    }
  }

  const deleteConversation = async (conversation: Conversation) => {
    try {
      const token = await getToken()
      await apiFetch<void>(`/api/conversations/${conversation.id}`, token, { method: 'DELETE' })
      const next = conversations.filter((item) => item.id !== conversation.id)
      setConversations(next)
      if (!account) saveAnonymous(next)
      if (activeConversation === conversation.id) {
        setActiveConversation(undefined)
        setMessages([])
      }
    } catch (error) {
      setNotice((error as Error).message)
    }
  }

  const renameConversation = async (conversation: Conversation) => {
    const title = window.prompt('Conversation title', conversation.title)?.trim()
    if (!title || title === conversation.title) return
    try {
      const token = await getToken()
      await apiFetch<void>(`/api/conversations/${conversation.id}`, token, {
        method: 'PATCH',
        body: JSON.stringify({ title }),
      })
      const next = conversations.map((item) =>
        item.id === conversation.id ? { ...item, title } : item)
      setConversations(next)
      if (!account) saveAnonymous(next)
    } catch (error) {
      setNotice((error as Error).message)
    }
  }

  const sendMessage = async () => {
    const text = draft.trim()
    if (!text || sending) return
    let conversationId = activeConversation
    if (!conversationId) conversationId = await createConversation()
    if (!conversationId) return

    setDraft('')
    setSending(true)
    setMessages((current) => [
      ...current,
      { role: 'user', text },
      { role: 'assistant', text: '', events: [] },
    ])
    try {
      const token = await getToken()
      await streamConversation(conversationId, text, token, (event, data) => {
        setMessages((current) => {
          const next = [...current]
          const last = { ...next[next.length - 1] }
          if (event === 'token') last.text += String(data.text ?? '')
          if (event === 'tool') {
            last.events = [...(last.events ?? []), `Using ${String(data.toolName ?? 'tool')}`]
          }
          if (event === 'step') {
            const step = data.step as { completed?: number; total?: number; toolName?: string }
            last.events = [
              ...(last.events ?? []),
              `${step.completed}/${step.total} · ${step.toolName}`,
            ]
          }
          if (event === 'error') last.text += `\n${String(data.message ?? 'The turn failed.')}`
          next[next.length - 1] = last
          return next
        })
      })
      await Promise.all([refreshConversations(), refreshData()])
    } catch (error) {
      setMessages((current) => {
        const next = [...current]
        next[next.length - 1] = { role: 'assistant', text: (error as Error).message }
        return next
      })
    } finally {
      setSending(false)
    }
  }

  const login = async () => {
    const scope = `api://${config.auth.clientId}/access_as_user`
    await instance.loginPopup({ scopes: [scope], prompt: 'select_account' })
  }

  const status = useMemo(() => {
    if (!config.sqlEnabled) return { tone: 'warning', text: 'SQL setup needed' }
    if (!config.agentEnabled) return { tone: 'warning', text: 'Agent key needed' }
    return { tone: 'online', text: 'All systems ready' }
  }, [config])

  return (
    <div className="app-shell">
      <aside className={`sidebar ${sidebarOpen ? '' : 'sidebar-collapsed'}`}>
        <div className="brand">
          <div className="brand-mark"><Sparkles size={19} /></div>
          <div className="brand-copy"><strong>Hidden Layer</strong><span>OCR Studio</span></div>
          <button className="icon-button collapse-button" onClick={() => setSidebarOpen(false)}>
            <PanelLeftClose size={18} />
          </button>
        </div>

        <button className="new-chat" onClick={() => void createConversation()}>
          <Plus size={17} /> New conversation
        </button>

        <nav className="main-nav">
          <button className={view === 'chat' ? 'active' : ''} onClick={() => setView('chat')}>
            <MessageSquareText size={18} /> Agent chat
          </button>
          <button className={view === 'process' ? 'active' : ''} onClick={() => setView('process')}>
            <WandSparkles size={18} /> Process image
          </button>
          <button className={view === 'history' ? 'active' : ''} onClick={() => setView('history')}>
            <History size={18} /> Run history
          </button>
        </nav>

        <div className="conversation-heading"><span>Conversations</span><MoreHorizontal size={16} /></div>
        <div className="conversation-list">
          {conversations.map((conversation) => (
            <div
              key={conversation.id}
              className={`conversation-row ${activeConversation === conversation.id ? 'selected' : ''}`}
            >
              <button onClick={() => void selectConversation(conversation.id)}>
                <MessageSquareText size={15} />
                <span>
                  <strong>{conversation.title}</strong>
                  <small>{formatRelative(conversation.lastActiveUtc)}</small>
                </span>
              </button>
              <div className="row-actions">
                <button title="Rename" onClick={() => void renameConversation(conversation)}>
                  <MoreHorizontal size={14} />
                </button>
                <button title="Delete" onClick={() => void deleteConversation(conversation)}>
                  <Trash2 size={14} />
                </button>
              </div>
            </div>
          ))}
          {!conversations.length && <p className="empty-sidebar">Your conversations will appear here.</p>}
        </div>

        <div className="sidebar-footer">
          <div className={`system-status ${status.tone}`}><i />{status.text}</div>
          {config.auth.enabled ? (
            account ? (
              <button className="account-button" onClick={() => void instance.logoutPopup()}>
                <CircleUserRound size={20} />
                <span><strong>{account.name ?? 'Microsoft account'}</strong><small>Signed in</small></span>
                <LogOut size={15} />
              </button>
            ) : (
              <button className="account-button" onClick={() => void login()}>
                <LogIn size={19} /><span><strong>Sign in with Microsoft</strong><small>Sync conversations</small></span>
              </button>
            )
          ) : (
            <div className="anonymous-label"><CircleUserRound size={18} /> Using anonymously</div>
          )}
        </div>
      </aside>

      <main className="workspace">
        <header className="topbar">
          {!sidebarOpen && (
            <button className="icon-button" onClick={() => setSidebarOpen(true)}><Menu size={20} /></button>
          )}
          <div>
            <h1>{view === 'chat' ? 'Agent workspace' : view === 'process' ? 'Process a receipt' : 'Results & history'}</h1>
            <p>{view === 'chat' ? 'Ask naturally. The agent chooses the right OCR tools.' :
              view === 'process' ? 'Run a deterministic workflow without the model.' :
                'Review every run, receipt, and generated artifact.'}</p>
          </div>
          <div className="topbar-badge"><Bot size={16} /> GPT-4o mini</div>
        </header>

        {notice && (
          <div className="notice"><span>{notice}</span><button onClick={() => setNotice(undefined)}><X size={16} /></button></div>
        )}

        {view === 'chat' && (
          <ChatPanel
            messages={messages}
            draft={draft}
            sending={sending}
            hasConversation={Boolean(activeConversation)}
            onDraft={setDraft}
            onSend={() => void sendMessage()}
            bottomRef={bottomRef}
          />
        )}
        {view === 'process' && (
          <ProcessPanel
            workflows={workflows}
            getToken={getToken}
            onComplete={async () => { await refreshData(); setView('history') }}
            onError={setNotice}
          />
        )}
        {view === 'history' && (
          <HistoryPanel
            runs={runs}
            receipts={receipts}
            onArtifact={setSelectedArtifact}
            onRefresh={() => void refreshData()}
          />
        )}
      </main>

      {selectedArtifact && (
        <div className="artifact-overlay" onClick={() => setSelectedArtifact(undefined)}>
          <div className="artifact-dialog" onClick={(event) => event.stopPropagation()}>
            <div><span><ImageIcon size={18} /> Artifact preview</span>
              <button onClick={() => setSelectedArtifact(undefined)}><X size={18} /></button></div>
            <img src={artifactUrl(selectedArtifact)} alt="OCR artifact" />
            <code>{selectedArtifact}</code>
          </div>
        </div>
      )}
    </div>
  )
}

function ChatPanel({
  messages, draft, sending, hasConversation, onDraft, onSend, bottomRef,
}: {
  messages: ChatMessage[]; draft: string; sending: boolean; hasConversation: boolean
  onDraft: (value: string) => void; onSend: () => void
  bottomRef: React.RefObject<HTMLDivElement | null>
}) {
  const suggestions = [
    'Extract this receipt and validate the totals',
    'Assess the image quality before OCR',
    'Explain the artifacts from my latest run',
  ]
  return (
    <section className="chat-panel">
      <div className="message-scroll">
        {!messages.length && (
          <div className="welcome">
            <div className="welcome-icon"><WandSparkles size={26} /></div>
            <span className="eyebrow">OCR PREPROCESSING AGENT</span>
            <h2>Turn difficult scans into<br />reliable data.</h2>
            <p>Upload through the process panel, then ask the agent to assess, improve, extract, or explain.</p>
            <div className="suggestions">
              {suggestions.map((suggestion) => (
                <button key={suggestion} onClick={() => onDraft(suggestion)}>
                  <Sparkles size={15} /> {suggestion}<ChevronRight size={15} />
                </button>
              ))}
            </div>
          </div>
        )}
        {messages.map((message, index) => (
          <article key={index} className={`message ${message.role}`}>
            <div className="message-avatar">
              {message.role === 'assistant' ? <Sparkles size={16} /> : <CircleUserRound size={17} />}
            </div>
            <div className="message-body">
              <span>{message.role === 'assistant' ? 'OCR Agent' : 'You'}</span>
              {message.events?.length ? (
                <div className="event-stack">
                  {message.events.map((event, eventIndex) => (
                    <small key={`${event}-${eventIndex}`}><Check size={12} /> {event}</small>
                  ))}
                </div>
              ) : null}
              <p>{message.text || (sending && index === messages.length - 1 ? 'Thinking…' : '')}</p>
            </div>
          </article>
        ))}
        <div ref={bottomRef} />
      </div>
      <div className="composer-wrap">
        <div className="composer">
          <textarea
            value={draft}
            onChange={(event) => onDraft(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter' && !event.shiftKey) {
                event.preventDefault()
                onSend()
              }
            }}
            placeholder={hasConversation ? 'Ask a follow-up…' : 'Start a conversation with the OCR agent…'}
            rows={1}
          />
          <button disabled={!draft.trim() || sending} onClick={onSend}>
            <Send size={17} />
          </button>
        </div>
        <small>Enter to send · Shift + Enter for a new line</small>
      </div>
    </section>
  )
}

function ProcessPanel({
  workflows, getToken, onComplete, onError,
}: {
  workflows: Workflow[]; getToken: () => Promise<string | undefined>
  onComplete: () => Promise<void>; onError: (message: string) => void
}) {
  const [file, setFile] = useState<File>()
  const [workflowId, setWorkflowId] = useState('receipt_extract_v3')
  const [running, setRunning] = useState(false)
  const [result, setResult] = useState<string>()

  const run = async () => {
    if (!file || !workflowId) return
    setRunning(true)
    setResult(undefined)
    try {
      const token = await getToken()
      const form = new FormData()
      form.append('file', file)
      const upload = await apiFetch<{ reference: string }>('/api/images', token, {
        method: 'POST', body: form,
      })
      const job = await apiFetch<{ output: string }>('/api/jobs', token, {
        method: 'POST',
        body: JSON.stringify({
          mode: 'workflow', name: workflowId, imageReference: upload.reference,
        }),
      })
      setResult(job.output)
      await onComplete()
    } catch (error) {
      onError((error as Error).message)
    } finally {
      setRunning(false)
    }
  }

  return (
    <section className="process-page">
      <div className="process-card">
        <div className="card-heading"><div><Upload size={20} /></div>
          <span><small>STEP 1</small><h2>Choose a receipt image</h2></span></div>
        <label className={`drop-zone ${file ? 'has-file' : ''}`}>
          <input type="file" accept="image/*" onChange={(event) => setFile(event.target.files?.[0])} />
          {file ? <><FileImage size={35} /><strong>{file.name}</strong><span>{formatBytes(file.size)}</span></> :
            <><Upload size={35} /><strong>Drop an image or browse</strong><span>PNG, JPG, TIFF or WebP · up to 20 MB</span></>}
        </label>
      </div>
      <div className="process-card">
        <div className="card-heading"><div><WandSparkles size={20} /></div>
          <span><small>STEP 2</small><h2>Select a workflow</h2></span></div>
        <div className="workflow-grid">
          {workflows.map((workflow) => (
            <button
              key={workflow.id}
              className={workflowId === workflow.id ? 'selected' : ''}
              onClick={() => setWorkflowId(workflow.id)}
            >
              <span>{workflowId === workflow.id && <Check size={13} />}{workflow.name}</span>
              <p>{workflow.description}</p><code>{workflow.id}</code>
            </button>
          ))}
        </div>
      </div>
      <button className="run-button" disabled={!file || running} onClick={() => void run()}>
        {running ? <><span className="spinner" /> Processing receipt…</> :
          <><Sparkles size={18} /> Run selected workflow</>}
      </button>
      {result && <pre className="result-output">{result}</pre>}
    </section>
  )
}

function HistoryPanel({
  runs, receipts, onArtifact, onRefresh,
}: {
  runs: Run[]; receipts: Receipt[]; onArtifact: (reference: string) => void; onRefresh: () => void
}) {
  const [section, setSection] = useState<'runs' | 'receipts'>('runs')
  return (
    <section className="history-page">
      <div className="history-toolbar">
        <div className="segmented">
          <button className={section === 'runs' ? 'active' : ''} onClick={() => setSection('runs')}>
            <History size={16} /> Runs <span>{runs.length}</span>
          </button>
          <button className={section === 'receipts' ? 'active' : ''} onClick={() => setSection('receipts')}>
            <ReceiptText size={16} /> Receipts <span>{receipts.length}</span>
          </button>
        </div>
        <button className="refresh-button" onClick={onRefresh}>Refresh</button>
      </div>
      {section === 'runs' ? (
        <div className="run-list">
          {runs.map((run) => (
            <article className="run-row" key={run.runId}>
              <div className={`run-icon ${run.success ? 'success' : 'failure'}`}>
                {run.success ? <Check size={18} /> : <X size={18} />}
              </div>
              <div className="run-primary">
                <strong>{run.name}</strong><code>{run.runId}</code>
                {run.error && <p>{run.error}</p>}
              </div>
              <div className="run-meta">
                <span><Clock3 size={14} /> {formatDuration(run.durationMs)}</span>
                <span>{run.stepCount} {run.stepCount === 1 ? 'step' : 'steps'}</span>
                <small>{new Date(run.startTimeUtc).toLocaleString()}</small>
              </div>
              {run.outputReference ? (
                <button className="artifact-button" onClick={() => onArtifact(run.outputReference)}>
                  <ImageIcon size={16} /> View artifact
                </button>
              ) : <span />}
            </article>
          ))}
          {!runs.length && <EmptyState icon={<History />} title="No runs yet" text="Process a receipt to see it here." />}
        </div>
      ) : (
        <div className="receipt-grid">
          {receipts.map((receipt) => (
            <article key={receipt.receiptId}>
              <div className="receipt-top"><ReceiptText size={20} />
                <span className={receipt.isValid ? 'valid' : 'review'}>
                  {receipt.isValid ? 'Validated' : 'Review'}
                </span></div>
              <h3>{receipt.merchant || 'Unknown merchant'}</h3>
              <p>{receipt.transactionUtc ? new Date(receipt.transactionUtc).toLocaleDateString() : 'Date unavailable'}</p>
              <strong className="receipt-total">
                {receipt.total?.toLocaleString(undefined, { style: 'currency', currency: receipt.currency }) ?? '—'}
              </strong>
              <div className="score"><span style={{ width: `${receipt.validationScore}%` }} />
                <small>{receipt.validationScore}% confidence</small></div>
            </article>
          ))}
          {!receipts.length && <EmptyState icon={<ReceiptText />} title="No receipts indexed" text="Extraction results will appear here." />}
        </div>
      )}
    </section>
  )
}

function EmptyState({ icon, title, text }: { icon: React.ReactNode; title: string; text: string }) {
  return <div className="empty-state">{icon}<strong>{title}</strong><span>{text}</span></div>
}

function formatRelative(value: string) {
  const minutes = Math.max(0, Math.floor((Date.now() - new Date(value).getTime()) / 60000))
  if (minutes < 1) return 'Just now'
  if (minutes < 60) return `${minutes}m ago`
  if (minutes < 1440) return `${Math.floor(minutes / 60)}h ago`
  return new Date(value).toLocaleDateString()
}

function formatDuration(ms: number) {
  return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s`
}

function formatBytes(bytes: number) {
  return bytes < 1024 * 1024 ? `${Math.round(bytes / 1024)} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

export default App
