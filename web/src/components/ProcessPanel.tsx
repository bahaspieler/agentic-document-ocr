import { Bot, Check, FileImage, Sparkles, Upload, WandSparkles } from 'lucide-react'
import { useState } from 'react'
import {
  apiFetch, streamConversation, streamWorkflowJob, type ActivityEvent,
  type ChatAttachment, type Conversation, type Run,
  type Workflow, type WorkflowResult,
} from '../api'
import type { ChatMessage, MessageArtifact } from '../types'
import { Message } from './Message'

export function ProcessPanel({
  workflows, conversations, getToken, createConversation, onComplete, onOpenHistory, onError,
}: {
  workflows: Workflow[]; conversations: Conversation[]; getToken: () => Promise<string | undefined>
  createConversation: () => Promise<string | undefined>; onComplete: () => Promise<void>
  onOpenHistory: (runId?: string) => void; onError: (message: string) => void
}) {
  const [file, setFile] = useState<File>()
  const [mode, setMode] = useState<'workflow' | 'agent'>('workflow')
  const [workflowId, setWorkflowId] = useState('receipt_extract_v3')
  const [conversationId, setConversationId] = useState('')
  const [instruction, setInstruction] = useState('Extract this receipt, validate its totals, and summarize anything that needs review.')
  const [running, setRunning] = useState(false)
  const [result, setResult] = useState<WorkflowResult | string>()
  const [processMessage, setProcessMessage] = useState<ChatMessage>()

  const run = async () => {
    if (!file) return
    setRunning(true); setResult(undefined)
    setProcessMessage({
      id: crypto.randomUUID(), role: 'assistant', text: '',
      createdUtc: new Date().toISOString(), events: [], runs: [], artifacts: [],
    })
    try {
      const token = await getToken()
      const form = new FormData(); form.append('file', file)
      const upload = await apiFetch<{ reference: string }>('/api/images', token, { method: 'POST', body: form })
      const target = conversationId || await createConversation()
      if (!target) throw new Error('A conversation could not be created.')
      setConversationId(target)
      if (mode === 'workflow') {
        await streamWorkflowJob({
          mode: 'workflow', name: workflowId, imageReference: upload.reference,
          conversationId: target, idempotencyKey: crypto.randomUUID(),
          userMessage: `Run the guided workflow ${workflowId} on ${file.name}.`,
          attachmentName: file.name, attachmentContentType: file.type,
        }, token, (event, data) => {
          setProcessMessage((current) => updateProcessMessage(current, event, data))
          if (event === 'done' && data.result) {
            const workflowResult = data.result as WorkflowResult
            setResult(workflowResult)
            setProcessMessage((current) => current ? {
              ...current,
              text: workflowResult.success
                ? `Workflow **${workflowResult.workflowId}** completed successfully. Review the output and files below.`
                : `Workflow **${workflowResult.workflowId}** failed: ${workflowResult.error ?? 'Unknown error'}`,
            } : current)
          }
        })
      } else {
        const attachment: ChatAttachment = { reference: upload.reference, name: file.name, contentType: file.type }
        let answer = ''
        await streamConversation(target, instruction, token, [attachment], (event, data) => {
          if (event === 'token') answer += String(data.text ?? '')
          if (event === 'done') answer = String(data.answer ?? answer)
          setResult(answer)
          setProcessMessage((current) => updateProcessMessage(current, event, data))
        })
      }
      await onComplete()
    } catch (error) { onError((error as Error).message) } finally { setRunning(false) }
  }

  return <section className="process-page">
    <div className="mode-picker" role="group" aria-label="Processing mode">
      <button className={mode === 'workflow' ? 'active' : ''} onClick={() => setMode('workflow')}><WandSparkles size={17} /><span><strong>Guided workflow</strong><small>Fast, consistent pipeline</small></span></button>
      <button className={mode === 'agent' ? 'active' : ''} onClick={() => setMode('agent')}><Bot size={17} /><span><strong>Agent-assisted</strong><small>Ask for a tailored result</small></span></button>
    </div>
    <div className="process-card">
      <div className="card-heading"><div><Upload size={20} /></div><span><small>IMAGE</small><h2>Choose a receipt image</h2></span></div>
      <label className={`drop-zone ${file ? 'has-file' : ''}`} onDragOver={(event) => event.preventDefault()}
        onDrop={(event) => { event.preventDefault(); setFile(event.dataTransfer.files[0]) }}>
        <input type="file" accept="image/*" onChange={(event) => setFile(event.target.files?.[0])} />
        {file ? <><FileImage size={35} /><strong>{file.name}</strong><span>{formatBytes(file.size)}</span></> :
          <><Upload size={35} /><strong>Drop an image or browse</strong><span>PNG, JPG, TIFF or WebP · up to 20 MB</span></>}
      </label>
    </div>
    <div className="process-card agent-options"><label>Save result in conversation<select value={conversationId} onChange={(event) => setConversationId(event.target.value)}>
      <option value="">Create a new conversation</option>{conversations.map((conversation) => <option key={conversation.id} value={conversation.id}>{conversation.title}</option>)}
    </select></label></div>
    {mode === 'workflow' ? <div className="process-card">
      <div className="card-heading"><div><WandSparkles size={20} /></div><span><small>WORKFLOW</small><h2>Choose what to run</h2></span></div>
      <div className="workflow-grid">{workflows.map((workflow) => <button key={workflow.id} className={workflowId === workflow.id ? 'selected' : ''} onClick={() => setWorkflowId(workflow.id)}>
        <span>{workflowId === workflow.id && <Check size={13} />}{workflow.name}</span><p>{workflow.description}</p>
      </button>)}</div>
    </div> : <div className="process-card agent-options">
      <label>Instructions<textarea value={instruction} onChange={(event) => setInstruction(event.target.value)} rows={3} /></label>
    </div>}
    <button className="run-button" disabled={!file || running || (mode === 'workflow' && !workflowId)} onClick={() => void run()}>
      {running ? <><span className="spinner" /> Processing receipt…</> : <><Sparkles size={18} />{mode === 'workflow' ? 'Run workflow' : 'Send to agent'}</>}
    </button>
    {processMessage && <section className="result-workspace"><header>{running ? <span className="spinner" /> : <Check size={18} />}<div><h2>{running ? 'Processing…' : 'Processing complete'}</h2><p>{running ? 'Live progress appears below.' : 'Your result is ready. You can continue working here.'}</p></div></header>
      <Message message={processMessage} pending={running} onViewRun={(run) => onOpenHistory(run.runId)} />
      <div className="result-actions">
        {result && typeof result !== 'string' && result.receiptId && <button onClick={() => onOpenHistory(result.runId)}>Edit receipt</button>}
        {result && typeof result !== 'string' && <button onClick={() => onOpenHistory(result.runId)}>View run</button>}
        {!running && <button onClick={() => { setFile(undefined); setResult(undefined); setProcessMessage(undefined) }}>Process another</button>}
      </div>
    </section>}
  </section>
}

function formatBytes(bytes: number) { return bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB` }

function updateProcessMessage(
  current: ChatMessage | undefined,
  event: string,
  data: Record<string, unknown>,
): ChatMessage | undefined {
  if (!current) return current
  const next = {
    ...current,
    events: [...(current.events ?? [])],
    runs: [...(current.runs ?? [])],
    artifacts: [...(current.artifacts ?? [])],
  }
  if (event === 'token') next.text += String(data.text ?? '')
  else if (event === 'done' && data.answer && !next.text) next.text = String(data.answer)
  else if (event === 'error') next.text += `${next.text ? '\n\n' : ''}**Error:** ${String(data.message ?? 'Processing failed.')}`
  else if (event !== 'done') {
    const activity = { ...data, type: event } as ActivityEvent
    next.events.push(activity)
    if (activity.run && !next.runs.some((run: Run) => run.runId === activity.run!.runId))
      next.runs.push(activity.run)
    if (activity.artifact?.runId)
      next.artifacts.push({
        ...activity.artifact,
        runId: activity.artifact.runId,
        conversationId: activity.run?.conversationId,
      } as MessageArtifact)
  }
  return next
}
