import { Check, ChevronDown, CircleAlert, LoaderCircle, Wrench } from 'lucide-react'
import type { ActivityEvent } from '../api'
import { usePersistentDisclosure } from '../hooks/usePersistentDisclosure'

export function ActivityTimeline({ events }: { events: ActivityEvent[] }) {
  const [open, setOpen] = usePersistentDisclosure('ocr-activity-open', true)
  const artifacts = events.filter((event) => event.type === 'artifact')
  const activity = events.filter((event) => event.type !== 'artifact' && event.type !== 'run')
  const count = activity.length + (artifacts.length ? 1 : 0)
  if (!count) return null
  return (
    <section className="activity" aria-label="Agent activity">
      <button type="button" aria-expanded={open} onClick={() => setOpen((value) => !value)}>
        <span><Wrench size={14} /> Activity <small>{count}</small></span>
        <ChevronDown className={open ? 'rotated' : ''} size={15} />
      </button>
      {open && <ol>
        {activity.map((event, index) => {
          const step = event.step
          const failed = Boolean(step?.error || event.type === 'error')
          const label = step
            ? `${step.toolName ?? 'Workflow step'}${step.durationMs != null ? ` · ${formatDuration(step.durationMs)}` : ''}`
            : event.toolName ? `Using ${event.toolName}` : event.message ?? event.type ?? 'Activity'
          return <li key={`${label}-${index}`} className={failed ? 'failed' : ''}>
            {failed ? <CircleAlert size={13} /> : event.type === 'tool' ? <LoaderCircle size={13} /> : <Check size={13} />}
            <span>{label}{step?.error && <small>{step.error}</small>}</span>
          </li>
        })}
        {artifacts.length > 0 && <li>
          <Check size={13} />
          <span>{artifacts.length} output {artifacts.length === 1 ? 'file' : 'files'} ready</span>
        </li>}
      </ol>}
    </section>
  )
}

function formatDuration(ms: number) {
  return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s`
}
