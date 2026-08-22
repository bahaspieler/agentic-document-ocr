import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { Run } from '../api'
import { ResultCard } from './ResultCard'

const run: Run = {
  runId: 'run-1',
  mode: 'tool',
  name: 'RemoveDocumentBackground',
  conversationId: 'conversation-1',
  inputReference: 'input/image',
  outputReference: 'output/image',
  success: true,
  startTimeUtc: '2026-01-01T12:00:00Z',
  durationMs: 200,
  stepCount: 1,
}

describe('ResultCard', () => {
  it('previews a single image-tool output from its output reference', () => {
    render(<ResultCard run={run} />)
    const preview = screen.getByRole('img')
    expect(preview).toHaveAttribute(
      'src',
      '/api/artifacts/output/image?conversationId=conversation-1',
    )
    expect(screen.getByRole('link', { name: /download/i })).toHaveAttribute(
      'href',
      '/api/artifacts/output/image?conversationId=conversation-1&download=true',
    )
  })

  it('shows important receipt files and the run-history action', () => {
    render(<ResultCard
      run={{ ...run, mode: 'workflow' }}
      artifacts={[{
        runId: 'run-1',
        name: 'receipt.json',
        sizeBytes: 100,
        lastModifiedUtc: '2026-01-01T12:00:01Z',
        contentType: 'application/json',
        previewable: true,
      }, {
        runId: 'run-1',
        name: 'receipt_items.csv',
        sizeBytes: 100,
        lastModifiedUtc: '2026-01-01T12:00:01Z',
        contentType: 'text/csv',
        previewable: true,
      }]}
      onViewRun={() => undefined}
    />)
    expect(screen.getByText('Data files')).toBeInTheDocument()
    expect(screen.getByText(/receipt\.json/i)).toBeInTheDocument()
    expect(screen.getByText(/receipt items\.csv/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /view all files/i })).toBeInTheDocument()
  })
})
