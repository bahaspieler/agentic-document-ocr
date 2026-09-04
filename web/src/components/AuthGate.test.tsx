import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { AuthGate } from './AuthGate'

describe('AuthGate', () => {
  it('starts Auth0 sign-in and explains available connections', () => {
    const onLogin = vi.fn()
    render(<AuthGate busy={false} onLogin={onLogin} />)

    fireEvent.click(screen.getByRole('button', { name: /continue to secure sign-in/i }))

    expect(onLogin).toHaveBeenCalledOnce()
    expect(screen.getByText(/google, or github account/i)).toBeInTheDocument()
  })

  it('shows sign-in errors and disables duplicate attempts', () => {
    render(<AuthGate busy error="The sign-in window was closed." onLogin={() => undefined} />)

    expect(screen.getByRole('alert')).toHaveTextContent('sign-in window was closed')
    expect(screen.getByRole('button', { name: /checking secure sign-in/i })).toBeDisabled()
  })
})
