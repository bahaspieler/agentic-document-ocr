import { LockKeyhole, LogIn } from 'lucide-react'

type AuthGateProps = {
  busy: boolean
  error?: string
  onLogin: () => void
}

export function AuthGate({ busy, error, onLogin }: AuthGateProps) {
  return <main className="auth-gate">
    <section className="auth-card" aria-labelledby="auth-title">
      <div className="auth-mark"><LockKeyhole size={25} /></div>
      <span className="eyebrow">PROTECTED OCR WORKSPACE</span>
      <h1 id="auth-title">Sign in to Agentic Document OCR</h1>
      <p>Auth0 protects the Web API, saved conversations, artifacts, and MCP tools.</p>
      {error && <div className="auth-error" role="alert">{error}</div>}
      <button type="button" onClick={onLogin} disabled={busy}>
        <LogIn size={18} />
        {busy ? 'Checking secure sign-in…' : 'Continue to secure sign-in'}
      </button>
      <small>Continue with an approved Auth0, Google, or GitHub account.</small>
    </section>
  </main>
}
