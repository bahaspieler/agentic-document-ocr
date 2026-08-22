import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { ActivityTimeline } from './ActivityTimeline'

describe('ActivityTimeline persistence', () => {
  beforeEach(() => localStorage.clear())

  it('restores its collapsed state from localStorage', () => {
    const events = [{ type: 'tool', toolName: 'extract_receipt' }]
    const first = render(<ActivityTimeline events={events} />)
    fireEvent.click(screen.getByRole('button', { name: /activity/i }))
    expect(localStorage.getItem('ocr-activity-open')).toBe('false')
    first.unmount()
    render(<ActivityTimeline events={events} />)
    expect(screen.getByRole('button', { name: /activity/i })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByText('Using extract_receipt')).not.toBeInTheDocument()
  })

  it('groups file events instead of listing raw artifact labels', () => {
    render(<ActivityTimeline events={[
      { type: 'step', step: { toolName: 'RunOcr', durationMs: 120 } },
      { type: 'run' },
      { type: 'artifact' },
      { type: 'artifact' },
    ]} />)
    expect(screen.getByText('2 output files ready')).toBeInTheDocument()
    expect(screen.queryByText(/^artifact$/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/^run$/i)).not.toBeInTheDocument()
  })
})
