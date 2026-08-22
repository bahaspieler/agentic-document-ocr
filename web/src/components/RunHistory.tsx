import { Check, Clock3, Filter, History, ReceiptText, Search, X } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import type { ReceiptSummary, Run } from '../api'
import { ReceiptEditor } from './ReceiptEditor'
import { RunDetailDrawer } from './RunDetailDrawer'

export function RunHistory({ runs, receipts, getToken, onRefresh, focusRunId }: {
  runs: Run[]; receipts: ReceiptSummary[]; getToken: () => Promise<string | undefined>
  onRefresh: () => void; focusRunId?: string
}) {
  const [section, setSection] = useState<'runs' | 'receipts'>('runs')
  const [search, setSearch] = useState('')
  const [mode, setMode] = useState('all')
  const [status, setStatus] = useState('all')
  const [name, setName] = useState('all')
  const [conversation, setConversation] = useState('all')
  const [sort, setSort] = useState<'newest' | 'oldest' | 'duration'>('newest')
  const [selectedRun, setSelectedRun] = useState<Run>()
  const [selectedReceipt, setSelectedReceipt] = useState<ReceiptSummary>()
  useEffect(() => {
    if (!focusRunId) return
    const run = runs.find((item) => item.runId === focusRunId)
    if (run) {
      setSection('runs')
      setSelectedRun(run)
    }
  }, [focusRunId, runs])
  const names = [...new Set(runs.map((run) => run.name))]
  const conversations = [...new Set(runs.map((run) => run.conversationId).filter(Boolean))] as string[]
  const filteredRuns = useMemo(() => runs.filter((run) => {
    const query = search.toLowerCase()
    return (!query || `${run.name} ${run.runId} ${run.conversationId ?? ''}`.toLowerCase().includes(query)) &&
      (mode === 'all' || run.mode === mode) && (status === 'all' || String(run.success) === status) &&
      (name === 'all' || run.name === name) && (conversation === 'all' || run.conversationId === conversation)
  }).sort((a, b) => sort === 'duration' ? b.durationMs - a.durationMs :
    (new Date(a.startTimeUtc).getTime() - new Date(b.startTimeUtc).getTime()) * (sort === 'newest' ? -1 : 1)), [conversation, mode, name, runs, search, sort, status])
  const filteredReceipts = receipts.filter((receipt) => !search ||
    `${receipt.merchant ?? ''} ${receipt.receiptId} ${receipt.currency}`.toLowerCase().includes(search.toLowerCase()))

  return <section className="history-page">
    <div className="history-toolbar"><div className="segmented">
      <button className={section === 'runs' ? 'active' : ''} onClick={() => setSection('runs')}><History size={16} /> Runs <span>{runs.length}</span></button>
      <button className={section === 'receipts' ? 'active' : ''} onClick={() => setSection('receipts')}><ReceiptText size={16} /> Receipts <span>{receipts.length}</span></button>
    </div><button className="refresh-button" onClick={onRefresh}>Refresh</button></div>
    <div className="filter-bar"><label className="search-box"><Search size={15} /><input aria-label="Search history" value={search} onChange={(event) => setSearch(event.target.value)} placeholder={`Search ${section}…`} />{search && <button aria-label="Clear search" onClick={() => setSearch('')}><X size={13} /></button>}</label>
      {section === 'runs' && <><Filter size={14} />
        <select aria-label="Mode filter" value={mode} onChange={(event) => setMode(event.target.value)}><option value="all">All modes</option><option value="workflow">Workflow</option><option value="tool">Tool</option></select>
        <select aria-label="Success filter" value={status} onChange={(event) => setStatus(event.target.value)}><option value="all">Any status</option><option value="true">Successful</option><option value="false">Failed</option></select>
        <select aria-label="Name filter" value={name} onChange={(event) => setName(event.target.value)}><option value="all">All names</option>{names.map((item) => <option key={item} value={item}>{friendly(item)}</option>)}</select>
        <select aria-label="Conversation filter" value={conversation} onChange={(event) => setConversation(event.target.value)}><option value="all">All conversations</option>{conversations.map((id) => <option key={id} value={id}>{shortId(id)}</option>)}</select>
        <select aria-label="Sort history" value={sort} onChange={(event) => setSort(event.target.value as typeof sort)}><option value="newest">Newest first</option><option value="oldest">Oldest first</option><option value="duration">Longest first</option></select>
      </>}</div>
    {section === 'runs' ? <div className="run-list">{filteredRuns.map((run) => <button className="run-row" key={run.runId} onClick={() => setSelectedRun(run)}>
      <div className={`run-icon ${run.success ? 'success' : 'failure'}`}>{run.success ? <Check size={18} /> : <X size={18} />}</div>
      <div className="run-primary"><strong>{friendly(run.name)}</strong><span className="meta-tags"><i>{run.mode}</i>{run.conversationId && <i>Conversation {shortId(run.conversationId)}</i>}</span><code>{run.runId}</code>{run.error && <p>{run.error}</p>}</div>
      <div className="run-meta"><span><Clock3 size={14} /> {formatDuration(run.durationMs)}</span><span>{run.stepCount} {run.stepCount === 1 ? 'step' : 'steps'}</span><small>{formatDate(run.startTimeUtc)}</small></div>
      <span className="details-link">View details</span>
    </button>)}{!filteredRuns.length && <Empty title="No matching runs" text={runs.length ? 'Try changing your filters.' : 'Process a receipt to see it here.'} />}</div>
      : <div className="receipt-grid">{filteredReceipts.map((receipt) => <button key={receipt.receiptId} onClick={() => setSelectedReceipt(receipt)}>
        <div className="receipt-top"><ReceiptText size={20} /><span className={receipt.isValid ? 'valid' : 'review'}>{reviewLabel(receipt.reviewStatus, receipt.isValid)}</span></div>
        <h3>{receipt.merchant || 'Unknown merchant'}</h3><p>{receipt.transactionUtc ? new Date(receipt.transactionUtc).toLocaleDateString() : 'Date unavailable'} · {receipt.conversationId ? `Conversation ${shortId(receipt.conversationId)}` : 'Direct run'}</p>
        <strong className="receipt-total">{formatMoney(receipt.total, receipt.currency)}</strong>
        <div className="score"><span style={{ width: `${receipt.validationScore}%` }} /><small>{receipt.validationScore}% consistency checks</small></div>
        <small className="ocr-score">OCR confidence: {receipt.ocrConfidence == null ? 'Not recorded' : `${Math.round(receipt.ocrConfidence)}%`}</small>
      </button>)}{!filteredReceipts.length && <Empty title="No matching receipts" text={receipts.length ? 'Try a different search.' : 'Extraction results will appear here.'} />}</div>}
    <RunDetailDrawer run={selectedRun} getToken={getToken} onClose={() => setSelectedRun(undefined)} />
    {selectedReceipt && <ReceiptEditor receipt={selectedReceipt} getToken={getToken} onClose={() => setSelectedReceipt(undefined)} onSaved={onRefresh} />}
  </section>
}

function Empty({ title, text }: { title: string; text: string }) { return <div className="empty-state"><History /><strong>{title}</strong><span>{text}</span></div> }
function friendly(value: string) { return value.replace(/_/g, ' ').replace(/\b\w/g, (letter: string) => letter.toUpperCase()) }
function shortId(value: string) { return value.slice(0, 8) }
function formatDuration(ms: number) { return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s` }
function formatDate(value: string) { return new Date(value).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' }) }
function formatMoney(value: number | undefined, currency: string) { return value == null ? '—' : value.toLocaleString(undefined, { style: 'currency', currency: currency || 'USD' }) }
function reviewLabel(value: string | undefined, valid: boolean) {
  if (value === 'user_corrected') return 'User corrected'
  if (value === 'user_verified') return 'User verified'
  if (value === 'checks_passed') return 'Checks passed'
  return valid ? 'Checks passed' : 'Needs review'
}
