import { Auth0Provider, useAuth0 } from '@auth0/auth0-react'
import { useCallback, type ReactNode } from 'react'
import type { RuntimeConfig } from '../api'
import { anonymousAuth, AuthContext } from './useAppAuth'

export function AppAuthProvider({ config, children }: {
  config: RuntimeConfig
  children: ReactNode
}) {
  if (!config.auth.enabled)
    return <AuthContext.Provider value={anonymousAuth}>{children}</AuthContext.Provider>

  return <Auth0Provider
    domain={new URL(config.auth.authority).hostname}
    clientId={config.auth.clientId}
    authorizationParams={{
      redirect_uri: window.location.origin,
      audience: config.auth.audience,
      scope: `openid profile email offline_access ${config.auth.scope}`,
    }}
    cacheLocation="localstorage"
    useRefreshTokens
    onRedirectCallback={() => window.history.replaceState({}, document.title, window.location.pathname)}
  >
    <Auth0Bridge config={config}>{children}</Auth0Bridge>
  </Auth0Provider>
}

function Auth0Bridge({ config, children }: {
  config: RuntimeConfig
  children: ReactNode
}) {
  const {
    user,
    isAuthenticated,
    isLoading,
    getAccessTokenSilently,
    loginWithRedirect,
    logout: auth0Logout,
  } = useAuth0()

  const getToken = useCallback(async () => {
    if (!isAuthenticated)
      throw new Error('Sign in is required.')
    return getAccessTokenSilently({
      authorizationParams: {
        audience: config.auth.audience,
        scope: config.auth.scope,
      },
    })
  }, [config.auth.audience, config.auth.scope, getAccessTokenSilently, isAuthenticated])

  const login = useCallback(
    () => loginWithRedirect({
      authorizationParams: {
        audience: config.auth.audience,
        scope: `openid profile email offline_access ${config.auth.scope}`,
      },
    }),
    [config.auth.audience, config.auth.scope, loginWithRedirect],
  )

  const logout = useCallback(async () => {
    await auth0Logout({
      logoutParams: { returnTo: window.location.origin },
    })
  }, [auth0Logout])

  return <AuthContext.Provider value={{
    user,
    isAuthenticated,
    isLoading,
    getToken,
    login,
    logout,
  }}>{children}</AuthContext.Provider>
}
