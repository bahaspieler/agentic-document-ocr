import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Message } from './Message'

describe('Message markdown', () => {
  it('renders GFM and removes unsafe HTML', () => {
    const { container } = render(<Message message={{
      id: '1', role: 'assistant', createdUtc: '2026-01-01T12:00:00Z',
      text: '**Ready**\n\n| Item | Total |\n|---|---:|\n| Tea | 2 |\n\n<script>alert(1)</script><img src=x onerror="alert(2)">',
    }} />)
    expect(screen.getByText('Ready').tagName).toBe('STRONG')
    expect(screen.getByRole('table')).toBeInTheDocument()
    expect(container.querySelector('script')).not.toBeInTheDocument()
    expect(container.querySelector('[onerror]')).not.toBeInTheDocument()
  })
})
