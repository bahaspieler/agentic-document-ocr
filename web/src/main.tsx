import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import type { RuntimeConfig } from './api'
import { AppAuthProvider } from './auth/AuthContext'

async function bootstrap() {
  const runtimeConfig: RuntimeConfig = await fetch('/api/config')
    .then((response) => response.ok ? response.json() : Promise.reject())
    .catch(() => ({
      auth: {
        enabled: false,
        clientId: '',
        authority: '',
        audience: '',
        scope: '',
      },
      sqlEnabled: false,
      agentEnabled: false,
    }))

  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <AppAuthProvider config={runtimeConfig}>
        <App config={runtimeConfig} />
      </AppAuthProvider>
    </StrictMode>,
  )
}

void bootstrap()
