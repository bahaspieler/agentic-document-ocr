import { Download, ExternalLink, File, Image as ImageIcon, X } from 'lucide-react'
import { useEffect, useState } from 'react'
import { apiFetch, runArtifactUrl, type ArtifactDescriptor, type Run } from '../api'

export function RunDetailDrawer({ run, getToken, onClose }: {
  run?: Run; getToken: () => Promise<string | undefined>; onClose: () => void
}) {
  const [artifacts, setArtifacts] = useState<ArtifactDescriptor[]>([])
  const [error, setError] = useState<string>()
  useEffect(() => {
    if (!run) return
    setArtifacts([]); setError(undefined)
    void getToken().then((token) => apiFetch<ArtifactDescriptor[]>(
      `/api/runs/${encodeURIComponent(run.runId)}/artifacts${run.conversationId ? `?conversationId=${encodeURIComponent(run.conversationId)}` : ''}`, token,
    )).then(setArtifacts).catch((reason: Error) => setError(reason.message))
  }, [getToken, run])
  if (!run) return null
  return <div className="drawer-backdrop" onMouseDown={onClose}>
    <aside className="detail-drawer" aria-label="Run details" onMouseDown={(event) => event.stopPropagation()}>
      <header><div><small>{run.mode}</small><h2>{friendly(run.name)}</h2></div><button aria-label="Close details" onClick={onClose}><X /></button></header>
      <dl><div><dt>Status</dt><dd className={run.success ? 'success-text' : 'error-text'}>{run.success ? 'Successful' : 'Failed'}</dd></div>
        <div><dt>Started</dt><dd>{new Date(run.startTimeUtc).toLocaleString()}</dd></div>
        <div><dt>Duration</dt><dd>{formatDuration(run.durationMs)}</dd></div><div><dt>Steps</dt><dd>{run.stepCount}</dd></div>
        <div><dt>Source</dt><dd>{run.source || 'Unknown'}</dd></div><div><dt>Run ID</dt><dd><code>{run.runId}</code></dd></div></dl>
      {run.error && <div className="drawer-error">{run.error}</div>}
      <h3>Artifacts</h3>
      {error && <p className="error-text">{error}</p>}
      <div className="drawer-artifacts">{artifacts.map((artifact) => {
        const url = runArtifactUrl(run.runId, artifact.name, run.conversationId)
        return <article key={artifact.name}>
          {artifact.previewable && artifact.contentType.startsWith('image/') ? <img src={url} alt="" /> : <File size={24} />}
          <span><strong>{artifact.name}</strong><small>{formatBytes(artifact.sizeBytes)}</small></span>
          <a aria-label={`View ${artifact.name}`} href={url} target="_blank" rel="noreferrer"><ExternalLink size={15} /></a>
          <a aria-label={`Download ${artifact.name}`} href={runArtifactUrl(run.runId, artifact.name, run.conversationId, true)}><Download size={15} /></a>
        </article>
      })}</div>
      {!artifacts.length && !error && <div className="drawer-empty"><ImageIcon />No artifacts recorded for this run.</div>}
    </aside>
  </div>
}
function friendly(value: string) { return value.replace(/_/g, ' ').replace(/\b\w/g, (letter: string) => letter.toUpperCase()) }
function formatDuration(ms: number) { return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s` }
function formatBytes(bytes: number) { return bytes < 1048576 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1048576).toFixed(1)} MB` }
