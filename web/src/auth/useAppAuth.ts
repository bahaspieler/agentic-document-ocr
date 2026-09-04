import { createContext, useContext } from 'react'
import type { User } from '@auth0/auth0-react'

export type AppAuth = {
  user?: User
  isAuthenticated: boolean
  isLoading: boolean
  getToken: () => Promise<string | undefined>
  login: () => Promise<void>
  logout: () => Promise<void>
}

export const anonymousAuth: AppAuth = {
  isAuthenticated: false,
  isLoading: false,
  getToken: async () => undefined,
  login: async () => undefined,
  logout: async () => undefined,
}

export const AuthContext = createContext<AppAuth>(anonymousAuth)

export function useAppAuth(): AppAuth {
  return useContext(AuthContext)
}
