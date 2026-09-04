import { CheckCircle2, Clipboard, Download, ExternalLink, FileText, XCircle } from 'lucide-react'
import { artifactUrl, runArtifactUrl, type ArtifactDescriptor, type Run } from '../api'
import { AuthenticatedArtifactAction, AuthenticatedArtifactImage } from './AuthenticatedArtifact'

export function ResultCard({ run, artifacts = [], onViewRun, getToken }: {
  run: Run; artifacts?: ArtifactDescriptor[]; onViewRun?: (run: Run) => void
  getToken?: () => Promise<string | undefined>
}) {
  const images = artifacts.filter((item) => item.contentType.startsWith('image/'))
  const primary = images[images.length - 1] ?? artifacts.find((item) => item.previewable) ?? artifacts[0]
  const url = primary
    ? runArtifactUrl(run.runId, primary.name, run.conversationId)
    : run.outputReference ? artifactUrl(run.outputReference, run.conversationId) : undefined
  const downloadUrl = primary
    ? runArtifactUrl(run.runId, primary.name, run.conversationId, true)
    : run.outputReference ? artifactUrl(run.outputReference, run.conversationId, true) : undefined
  const important = artifacts.filter((item) =>
    /\.(json|csv|txt)$/i.test(item.name) &&
    /receipt|ocr_result|summary|items/i.test(item.name))
  return (
    <section className="result-card">
      <header>
        {run.success ? <CheckCircle2 size={17} /> : <XCircle size={17} />}
        <span><strong>{friendlyName(run.name)}</strong><small>{run.mode === 'workflow' ? 'Workflow result' : 'Tool result'} · {formatDuration(run.durationMs)}</small></span>
      </header>
      {run.error && <p className="error-text">{run.error}</p>}
      {url && (images.length > 0 || (!primary && run.outputReference)) && (
        <AuthenticatedArtifactImage className="result-preview" url={url} getToken={getToken} alt={`${primary?.name ?? run.name} preview`} />
      )}
      <div className="result-actions">
        {url && <AuthenticatedArtifactAction url={url} fileName={primary?.name ?? run.outputReference} getToken={getToken}><ExternalLink size={14} /> View</AuthenticatedArtifactAction>}
        {downloadUrl && <AuthenticatedArtifactAction download url={downloadUrl} fileName={primary?.name ?? run.outputReference} getToken={getToken}><Download size={14} /> Download</AuthenticatedArtifactAction>}
        <button type="button" onClick={() => void navigator.clipboard.writeText(run.outputReference || run.runId)}><Clipboard size={14} /> Copy reference</button>
        {!url && <span><FileText size={14} /> {run.stepCount} steps</span>}
      </div>
      {important.length > 0 && <div className="result-files">
        <strong>Data files</strong>
        {important.slice(0, 6).map((artifact) => <div key={artifact.name}>
          <span><FileText size={13} /> {friendlyName(artifact.name)}</span>
          <AuthenticatedArtifactAction url={runArtifactUrl(run.runId, artifact.name, run.conversationId)} fileName={artifact.name} getToken={getToken}>View</AuthenticatedArtifactAction>
          <AuthenticatedArtifactAction download url={runArtifactUrl(run.runId, artifact.name, run.conversationId, true)} fileName={artifact.name} getToken={getToken}>Download</AuthenticatedArtifactAction>
        </div>)}
      </div>}
      {onViewRun && <button className="view-all-files" type="button" onClick={() => onViewRun(run)}>
        View all files in run history
      </button>}
    </section>
  )
}

function friendlyName(value: string) { return value.replace(/_/g, ' ').replace(/\b\w/g, (letter: string) => letter.toUpperCase()) }
function formatDuration(ms: number) { return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s` }
