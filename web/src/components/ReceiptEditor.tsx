import { ArrowDown, ArrowUp, Download, Plus, RotateCcw, Save, Trash2, X } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import {
  apiFetch, receiptExportUrl, type ReceiptData, type ReceiptRecord,
  type ReceiptRevision, type ReceiptSummary,
} from '../api'
import { AuthenticatedArtifactAction } from './AuthenticatedArtifact'

export function ReceiptEditor({ receipt, getToken, onClose, onSaved }: {
  receipt: ReceiptSummary; getToken: () => Promise<string | undefined>; onClose: () => void; onSaved: () => void
}) {
  const [record, setRecord] = useState<ReceiptRecord>()
  const [draft, setDraft] = useState<ReceiptData>()
  const [revisions, setRevisions] = useState<ReceiptRevision[]>([])
  const [summary, setSummary] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string>()
  const query = receipt.conversationId ? `?conversationId=${encodeURIComponent(receipt.conversationId)}` : ''

  useEffect(() => {
    void getToken().then(async (token) => {
      const [detail, history] = await Promise.all([
        apiFetch<ReceiptRecord>(`/api/receipts/${encodeURIComponent(receipt.receiptId)}${query}`, token),
        apiFetch<ReceiptRevision[]>(`/api/receipts/${encodeURIComponent(receipt.receiptId)}/revisions${query}`, token),
      ])
      setRecord(detail); setDraft(JSON.parse(detail.currentJson) as ReceiptData); setRevisions(history)
    }).catch((reason: Error) => setError(reason.message))
  }, [getToken, query, receipt.receiptId])

  const original = useMemo(() => record ? JSON.parse(record.rawJson) as ReceiptData : undefined, [record])
  const patch = (update: Partial<ReceiptData>) => setDraft((current) => current ? { ...current, ...update } : current)
  const save = async (verified = false) => {
    if (!draft || !record) return
    setSaving(true); setError(undefined)
    try {
      const token = await getToken()
      const updated = await apiFetch<ReceiptRecord>(`/api/receipts/${encodeURIComponent(receipt.receiptId)}`, token, {
        method: 'PATCH', body: JSON.stringify({
          receipt: draft,
          rowVersion: record.rowVersion,
          conversationId: receipt.conversationId,
          changeSummary: summary || (verified ? 'Reviewed and verified by user' : 'Corrected in receipt editor'),
          verified,
        }),
      })
      setRecord(updated); setDraft(JSON.parse(updated.currentJson) as ReceiptData); setSummary(''); onSaved()
      setRevisions(await apiFetch<ReceiptRevision[]>(`/api/receipts/${encodeURIComponent(receipt.receiptId)}/revisions${query}`, token))
    } catch (reason) { setError((reason as Error).message) } finally { setSaving(false) }
  }
  const move = (index: number, offset: number) => {
    if (!draft) return
    const items = [...draft.items]; const target = index + offset
    if (target < 0 || target >= items.length) return
    ;[items[index], items[target]] = [items[target], items[index]]
    patch({ items: items.map((item, itemIndex) => ({ ...item, index: itemIndex })) })
  }
  if (!draft) return <div className="drawer-backdrop"><aside className="receipt-editor"><button className="close-editor" onClick={onClose}><X /></button>{error ?? 'Loading receipt…'}</aside></div>
  const ocrConfidence = typeof draft.source?.ocrConfidence === 'number' ? draft.source.ocrConfidence : undefined
  return <div className="drawer-backdrop" onMouseDown={onClose}><aside className="receipt-editor" aria-label="Receipt editor" onMouseDown={(event) => event.stopPropagation()}>
    <header><div><small>{record?.reviewStatus || 'Needs review'} · revision {record?.currentRevision ?? 0}</small><h2>Edit receipt</h2></div><button aria-label="Close editor" onClick={onClose}><X /></button></header>
    {error && <div className="drawer-error">{error}</div>}
    <div className="receipt-metrics"><span><small>OCR confidence</small><strong>{ocrConfidence == null ? 'Not recorded' : `${Math.round(ocrConfidence <= 1 ? ocrConfidence * 100 : ocrConfidence)}%`}</strong></span>
      <span><small>Consistency score</small><strong>{record?.validationScore ?? receipt.validationScore}%</strong></span></div>
    <div className="editor-fields">
      <Field label="Merchant" value={draft.merchant.name ?? ''} onChange={(value) => patch({ merchant: { ...draft.merchant, name: value } })} />
      <Field label="Date" value={draft.transaction.dateTime ?? ''} onChange={(value) => patch({ transaction: { ...draft.transaction, dateTime: value } })} />
      <Field label="Currency" value={draft.totals.currency} onChange={(value) => patch({ totals: { ...draft.totals, currency: value.toUpperCase() } })} />
      {(['subtotal', 'tax', 'total'] as const).map((name) => <Field key={name} label={name} type="number" value={draft.totals[name] ?? ''} onChange={(value) => patch({ totals: { ...draft.totals, [name]: value === '' ? undefined : Number(value) } })} />)}
    </div>
    <div className="line-items-heading"><h3>Line items</h3><button onClick={() => patch({ items: [...draft.items, { index: draft.items.length, description: '', quantity: 1 }] })}><Plus size={14} /> Add item</button></div>
    <div className="line-items">
      {draft.items.map((item, index) => <div className="line-item" key={`${item.index}-${index}`}>
        <input aria-label={`Item ${index + 1} description`} value={item.description} placeholder="Description" onChange={(event) => patch({ items: draft.items.map((row, rowIndex) => rowIndex === index ? { ...row, description: event.target.value } : row) })} />
        {(['quantity', 'unitPrice', 'lineTotal'] as const).map((name) => <input key={name} aria-label={`Item ${index + 1} ${name}`} type="number" step="any" placeholder={name} value={item[name] ?? ''} onChange={(event) => patch({ items: draft.items.map((row, rowIndex) => rowIndex === index ? { ...row, [name]: event.target.value === '' ? undefined : Number(event.target.value) } : row) })} />)}
        <button aria-label="Move item up" disabled={index === 0} onClick={() => move(index, -1)}><ArrowUp size={14} /></button>
        <button aria-label="Move item down" disabled={index === draft.items.length - 1} onClick={() => move(index, 1)}><ArrowDown size={14} /></button>
        <button aria-label="Remove item" onClick={() => patch({ items: draft.items.filter((_, rowIndex) => rowIndex !== index).map((row, rowIndex) => ({ ...row, index: rowIndex })) })}><Trash2 size={14} /></button>
      </div>)}
    </div>
    <label className="change-summary">Change summary<input value={summary} onChange={(event) => setSummary(event.target.value)} placeholder="What did you correct?" /></label>
    <div className="editor-actions"><button onClick={() => original && setDraft(structuredClone(original))}><RotateCcw size={15} /> Reset to original</button>
      <AuthenticatedArtifactAction download url={receiptExportUrl(receipt.receiptId, 'original', receipt.conversationId)} fileName={`${receipt.receiptId}-original.json`} getToken={getToken}><Download size={15} /> Original JSON</AuthenticatedArtifactAction>
      <AuthenticatedArtifactAction download url={receiptExportUrl(receipt.receiptId, 'corrected.json', receipt.conversationId)} fileName={`${receipt.receiptId}-corrected.json`} getToken={getToken}><Download size={15} /> Corrected JSON</AuthenticatedArtifactAction>
      <AuthenticatedArtifactAction download url={receiptExportUrl(receipt.receiptId, 'corrected.csv', receipt.conversationId)} fileName={`${receipt.receiptId}-corrected.csv`} getToken={getToken}><Download size={15} /> CSV</AuthenticatedArtifactAction>
      <button disabled={saving} onClick={() => void save(true)}><Save size={15} /> Mark verified</button>
      <button className="primary" disabled={saving} onClick={() => void save()}><Save size={15} /> {saving ? 'Saving…' : 'Save changes'}</button></div>
    <section className="revision-list"><h3>Revision history</h3>{revisions.map((revision) => <article key={revision.revisionNumber}><strong>Revision {revision.revisionNumber}</strong><span>{revision.changeSummary || 'Receipt updated'}</span><time>{new Date(revision.createdUtc).toLocaleString()}</time></article>)}
      {!revisions.length && <p>No corrections have been saved yet.</p>}</section>
  </aside></div>
}

function Field({ label, value, onChange, type = 'text' }: { label: string; value: string | number; onChange: (value: string) => void; type?: string }) {
  return <label>{label}<input type={type} step={type === 'number' ? 'any' : undefined} value={value} onChange={(event) => onChange(event.target.value)} /></label>
}
