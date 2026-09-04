import { useCallback, useEffect, useMemo, useState } from 'react'
import { Bot, CircleUserRound, History, LogIn, LogOut, Menu, MessageSquareText, MoreHorizontal, PanelLeftClose, Plus, Sparkles, Trash2, WandSparkles, X } from 'lucide-react'
import './App.css'
import { apiFetch, streamConversation, type ActivityEvent, type ArtifactDescriptor, type Conversation, type ConversationTurn, type ReceiptSummary, type Run, type RuntimeConfig, type Workflow } from './api'
import { useAppAuth } from './auth/useAppAuth'
import { AuthGate } from './components/AuthGate'
import { ChatPanel } from './components/ChatPanel'
import { ProcessPanel } from './components/ProcessPanel'
import { RunHistory } from './components/RunHistory'
import { useImageAttachments } from './hooks/useImageAttachments'
import type { ChatMessage, MessageArtifact } from './types'

type View = 'chat' | 'process' | 'history'
const anonymousKey = 'ocr-anonymous-conversations'

function App({ config }: { config: RuntimeConfig }) {
  const {
    user,
    isAuthenticated,
    isLoading: authLoading,
    getToken,
    login: beginLogin,
    logout,
  } = useAppAuth()
  const account = isAuthenticated ? user : undefined
  const [view, setView] = useState<View>('chat')
  const [sidebarOpen, setSidebarOpen] = useState(true)
  const [conversations, setConversations] = useState<Conversation[]>([])
  const [activeConversation, setActiveConversation] = useState<string>()
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [draft, setDraft] = useState('')
  const [sending, setSending] = useState(false)
  const [workflows, setWorkflows] = useState<Workflow[]>([])
  const [runs, setRuns] = useState<Run[]>([])
  const [focusRunId, setFocusRunId] = useState<string>()
  const [receipts, setReceipts] = useState<ReceiptSummary[]>([])
  const [notice, setNotice] = useState<string>()
  const [loginBusy, setLoginBusy] = useState(false)

  const attachmentState = useImageAttachments(getToken)
  const saveAnonymous = useCallback((items: Conversation[]) => localStorage.setItem(anonymousKey, JSON.stringify(items)), [])

  const refreshConversations = useCallback(async () => {
    try {
      if (config.auth.enabled && !account) {
        setConversations([])
        return
      }
      if (account) setConversations(await apiFetch<Conversation[]>('/api/conversations', await getToken()))
      else setConversations(JSON.parse(localStorage.getItem(anonymousKey) ?? '[]') as Conversation[])
    } catch (error) { setNotice((error as Error).message) }
  }, [account, config.auth.enabled, getToken])
  const refreshData = useCallback(async () => {
    if (config.auth.enabled && !account) {
      setWorkflows([])
      setRuns([])
      setReceipts([])
      return
    }
    const token = await getToken()
    const anonymous = account ? [] : JSON.parse(localStorage.getItem(anonymousKey) ?? '[]') as Conversation[]
    const scopes = account ? [undefined] : [undefined, ...anonymous.map((item) => item.id)]
    const [workflowRows, runGroups, receiptGroups] = await Promise.all([
      apiFetch<Workflow[]>('/api/workflows', token),
      Promise.all(scopes.map((id) => apiFetch<Run[]>(
        `/api/runs?top=100${id ? `&conversationId=${encodeURIComponent(id)}` : ''}`, token))),
      Promise.all(scopes.map((id) => apiFetch<ReceiptSummary[]>(
        `/api/receipts?top=100${id ? `&conversationId=${encodeURIComponent(id)}` : ''}`, token))),
    ])
    setWorkflows(workflowRows)
    setRuns(uniqueBy(runGroups.flat(), (item) => item.runId))
    setReceipts(uniqueBy(receiptGroups.flat(), (item) => item.receiptId))
  }, [account, config.auth.enabled, getToken])
  useEffect(() => { void refreshConversations(); void refreshData().catch((error: Error) => setNotice(error.message)) }, [refreshConversations, refreshData])

  const selectConversation = async (id: string) => {
    setActiveConversation(id); setView('chat'); setMessages([])
    try {
      const detail = await apiFetch<{ turns: ConversationTurn[] }>(`/api/conversations/${id}`, await getToken())
      setMessages(detail.turns.flatMap((turn) => {
        const turnRuns = turn.events.map((event) => event.run).filter((run): run is Run => Boolean(run))
        const artifacts = turn.events
          .map((event) => event.artifact)
          .filter((artifact): artifact is ArtifactDescriptor & { runId: string } =>
            Boolean(artifact?.runId))
          .map((artifact): MessageArtifact => ({
            ...artifact,
            runId: artifact.runId,
            conversationId: id,
          }))
        return [
          { id: `${id}-${turn.turnIndex}-user`, role: 'user' as const, text: turn.userMessage, createdUtc: turn.createdUtc, attachments: turn.attachments },
          { id: `${id}-${turn.turnIndex}-assistant`, role: 'assistant' as const, text: turn.answer, createdUtc: turn.createdUtc, events: turn.events, runs: turnRuns, artifacts },
        ]
      }))
    } catch (error) { setNotice((error as Error).message) }
  }
  const createConversation = useCallback(async () => {
    try {
      const conversation = await apiFetch<Conversation>('/api/conversations', await getToken(), { method: 'POST', body: JSON.stringify({ title: 'New conversation' }) })
      const next = [conversation, ...conversations]; setConversations(next)
      if (!account) saveAnonymous(next)
      setActiveConversation(conversation.id); setMessages([])
      return conversation.id
    } catch (error) { setNotice((error as Error).message) }
  }, [account, conversations, getToken, saveAnonymous])
  const deleteConversation = async (conversation: Conversation) => {
    try {
      await apiFetch<void>(`/api/conversations/${conversation.id}`, await getToken(), { method: 'DELETE' })
      const next = conversations.filter((item) => item.id !== conversation.id); setConversations(next)
      if (!account) saveAnonymous(next)
      if (activeConversation === conversation.id) { setActiveConversation(undefined); setMessages([]) }
    } catch (error) { setNotice((error as Error).message) }
  }
  const renameConversation = async (conversation: Conversation) => {
    const title = window.prompt('Conversation title', conversation.title)?.trim()
    if (!title || title === conversation.title) return
    try {
      await apiFetch<void>(`/api/conversations/${conversation.id}`, await getToken(), { method: 'PATCH', body: JSON.stringify({ title }) })
      const next = conversations.map((item) => item.id === conversation.id ? { ...item, title } : item); setConversations(next)
      if (!account) saveAnonymous(next)
    } catch (error) { setNotice((error as Error).message) }
  }
  const sendMessage = async () => {
    const ready = attachmentState.items.filter((item) => item.status === 'ready')
    const text = draft.trim() || (ready.length ? 'Please process the attached image(s).' : '')
    if (!text || sending) return
    const conversationId = activeConversation ?? await createConversation()
    if (!conversationId) return
    const attachments = ready.map(({ reference, name, contentType }) => ({ reference, name, contentType }))
    const now = new Date().toISOString()
    setDraft(''); attachmentState.clear(); setSending(true)
    setMessages((current) => [...current,
      { id: crypto.randomUUID(), role: 'user', text, createdUtc: now, attachments },
      { id: crypto.randomUUID(), role: 'assistant', text: '', createdUtc: now, events: [], runs: [], artifacts: [] },
    ])
    try {
      await streamConversation(conversationId, text, await getToken(), attachments, (event, data) => {
        setMessages((current) => updateLast(current, event, data))
      })
      await Promise.all([refreshConversations(), refreshData()])
      const currentRuns = await apiFetch<Run[]>(`/api/runs?top=20&conversationId=${encodeURIComponent(conversationId)}`, await getToken())
      const artifacts = await loadArtifacts(currentRuns, conversationId, getToken)
      setMessages((current) => {
        const next = [...current]
        const previous = next[next.length - 1]
        if (previous) next[next.length - 1] = { ...previous, runs: previous.runs?.length ? previous.runs : currentRuns, artifacts }
        return next
      })
    } catch (error) {
      setMessages((current) => updateLast(current, 'error', { message: (error as Error).message }))
    } finally { setSending(false) }
  }
  const login = async () => {
    setLoginBusy(true)
    setNotice(undefined)
    try {
      await beginLogin()
    } catch (error) {
      setNotice((error as Error).message)
    } finally {
      setLoginBusy(false)
    }
  }
  const status = useMemo(() => !config.sqlEnabled ? { tone: 'warning', text: 'SQL setup needed' } : !config.agentEnabled ? { tone: 'warning', text: 'Agent unavailable' } : { tone: 'online', text: 'All systems ready' }, [config])
  const model = config.modelName ?? config.model ?? (config.agentEnabled ? 'AI model ready' : 'Agent offline')

  if (config.auth.enabled && authLoading)
    return <AuthGate busy error={notice} onLogin={() => undefined} />

  if (config.auth.enabled && !account)
    return <AuthGate busy={loginBusy} error={notice} onLogin={() => void login()} />

  return <div className="app-shell">
    <aside className={`sidebar ${sidebarOpen ? '' : 'sidebar-collapsed'}`}>
      <div className="brand"><div className="brand-mark"><Sparkles size={19} /></div><div className="brand-copy"><strong>Hidden Layer</strong><span>OCR Studio</span></div><button aria-label="Collapse sidebar" className="icon-button collapse-button" onClick={() => setSidebarOpen(false)}><PanelLeftClose size={18} /></button></div>
      <button className="new-chat" onClick={() => { setView('chat'); void createConversation() }}><Plus size={17} /> New conversation</button>
      <nav className="main-nav" aria-label="Main navigation">
        <button className={view === 'chat' ? 'active' : ''} onClick={() => setView('chat')}><MessageSquareText size={18} /> Agent chat</button>
        <button className={view === 'process' ? 'active' : ''} onClick={() => setView('process')}><WandSparkles size={18} /> Process image</button>
        <button className={view === 'history' ? 'active' : ''} onClick={() => setView('history')}><History size={18} /> Run history</button>
      </nav>
      <div className="conversation-heading"><span>Conversations</span><MoreHorizontal size={16} /></div>
      <div className="conversation-list">{conversations.map((conversation) => <div key={conversation.id} className={`conversation-row ${activeConversation === conversation.id ? 'selected' : ''}`}>
        <button onClick={() => void selectConversation(conversation.id)}><MessageSquareText size={15} /><span><strong>{conversation.title}</strong><small>{formatRelative(conversation.lastActiveUtc)}</small></span></button>
        <div className="row-actions"><button aria-label={`Rename ${conversation.title}`} onClick={() => void renameConversation(conversation)}><MoreHorizontal size={14} /></button><button aria-label={`Delete ${conversation.title}`} onClick={() => void deleteConversation(conversation)}><Trash2 size={14} /></button></div>
      </div>)}{!conversations.length && <p className="empty-sidebar">Your conversations will appear here.</p>}</div>
      <div className="sidebar-footer"><div className={`system-status ${status.tone}`}><i />{status.text}</div>
        {config.auth.enabled ? account ? <div className="account-card"><CircleUserRound size={20} /><span><strong>{account.name ?? account.email ?? 'Signed-in account'}</strong><small>Signed in</small></span><button type="button" aria-label="Sign out" title="Sign out" onClick={() => void logout()}><LogOut size={15} /></button></div>
          : <button className="account-button" onClick={() => void login()}><LogIn size={19} /><span><strong>Sign in securely</strong><small>Sync conversations</small></span></button>
          : <div className="anonymous-label"><CircleUserRound size={18} /> Using anonymously</div>}</div>
    </aside>
    <main className="workspace">
      <header className="topbar">{!sidebarOpen && <button aria-label="Open sidebar" className="icon-button" onClick={() => setSidebarOpen(true)}><Menu size={20} /></button>}<div><h1>{view === 'chat' ? 'Agent workspace' : view === 'process' ? 'Process a receipt' : 'Results & history'}</h1><p>{view === 'chat' ? 'Ask naturally. The agent chooses the right OCR tools.' : view === 'process' ? 'Choose a repeatable workflow or ask the agent for help.' : 'Search, inspect, and correct every result.'}</p></div><div className="topbar-badge"><Bot size={16} /> {model}</div></header>
      {notice && <div className="notice" role="alert"><span>{notice}</span><button aria-label="Dismiss notification" onClick={() => setNotice(undefined)}><X size={16} /></button></div>}
      {view === 'chat' && <ChatPanel messages={messages} draft={draft} sending={sending} hasConversation={Boolean(activeConversation)} attachments={attachmentState.items} onDraft={setDraft} onSend={() => void sendMessage()} onAddFiles={(files) => void attachmentState.addFiles(files)} onRemoveAttachment={attachmentState.remove} onViewRun={(run) => { setFocusRunId(run.runId); setView('history') }} getToken={getToken} />}
      {view === 'process' && <ProcessPanel workflows={workflows} conversations={conversations} getToken={getToken} createConversation={createConversation} onComplete={async () => { await Promise.all([refreshData(), refreshConversations()]) }} onOpenHistory={(runId) => { setFocusRunId(runId); setView('history') }} onError={setNotice} />}
      {view === 'history' && <RunHistory runs={runs} receipts={receipts} getToken={getToken} onRefresh={() => void refreshData()} focusRunId={focusRunId} />}
    </main>
  </div>
}

function updateLast(current: ChatMessage[], event: string, data: Record<string, unknown>) {
  const next = [...current]; const previous = next[next.length - 1]
  if (!previous) return current
  const last = { ...previous, events: [...(previous.events ?? [])], runs: [...(previous.runs ?? [])], artifacts: [...(previous.artifacts ?? [])] }
  if (event === 'token') last.text += String(data.text ?? '')
  else if (event === 'done' && data.answer && !last.text) last.text = String(data.answer)
  else if (event === 'error') last.text += `${last.text ? '\n\n' : ''}**Error:** ${String(data.message ?? 'The turn failed.')}`
  else {
    const activity = { ...data, type: event } as ActivityEvent; last.events!.push(activity)
    if (activity.run && !last.runs!.some((run: Run) => run.runId === activity.run!.runId)) last.runs!.push(activity.run)
    if (activity.artifact?.runId) last.artifacts!.push({
      ...activity.artifact,
      runId: activity.artifact.runId,
      conversationId: activity.run?.conversationId,
    })
  }
  next[next.length - 1] = last
  return next
}

async function loadArtifacts(runs: Run[], conversationId: string, getToken: () => Promise<string | undefined>) {
  if (!runs.length) return []
  try {
    const token = await getToken()
    return (await Promise.all(runs.map(async (run) => {
      const rows = await apiFetch<ArtifactDescriptor[]>(`/api/runs/${encodeURIComponent(run.runId)}/artifacts?conversationId=${encodeURIComponent(conversationId)}`, token)
      return rows.map((item): MessageArtifact => ({ ...item, runId: run.runId, conversationId }))
    }))).flat()
  } catch { return [] }
}
function formatRelative(value: string) {
  const minutes = Math.max(0, Math.floor((Date.now() - new Date(value).getTime()) / 60000))
  return minutes < 1 ? 'Just now' : minutes < 60 ? `${minutes}m ago` : minutes < 1440 ? `${Math.floor(minutes / 60)}h ago` : new Date(value).toLocaleDateString()
}
function uniqueBy<T>(items: T[], key: (item: T) => string) {
  return [...new Map(items.map((item) => [key(item), item])).values()]
}
export default App
