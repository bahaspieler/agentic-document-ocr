import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { PublicClientApplication } from '@azure/msal-browser'
import { MsalProvider } from '@azure/msal-react'
import './index.css'
import App from './App.tsx'
import type { RuntimeConfig } from './api'

async function bootstrap() {
  const runtimeConfig: RuntimeConfig = await fetch('/api/config')
    .then((response) => response.ok ? response.json() : Promise.reject())
    .catch(() => ({
      auth: { enabled: false, clientId: '', authority: 'https://login.microsoftonline.com/common' },
      sqlEnabled: false,
      agentEnabled: false,
    }))

  const msal = new PublicClientApplication({
    auth: {
      clientId: runtimeConfig.auth.clientId || '00000000-0000-0000-0000-000000000000',
      authority: runtimeConfig.auth.authority,
      redirectUri: window.location.origin,
    },
    cache: { cacheLocation: 'localStorage' },
  })
  await msal.initialize()

  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <MsalProvider instance={msal}>
        <App config={runtimeConfig} />
      </MsalProvider>
    </StrictMode>,
  )
}

void bootstrap()
