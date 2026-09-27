import { useQueryClient } from '@tanstack/react-query'
import { createContext, useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { setUnauthorizedHandler, tokenStore } from '../api/client'
import { authApi } from '../api/endpoints'
import type { Me } from '../api/types'
import { isTokenExpired } from '../utils/jwt'

export interface AuthState {
  user: Me | null
  /** İlk açılışta kayıtlı token doğrulanırken true. */
  initializing: boolean
  isAdmin: boolean
  login: (email: string, password: string) => Promise<Me>
  register: (email: string, password: string, fullName: string) => Promise<Me>
  logout: () => void
}

// eslint-disable-next-line react-refresh/only-export-components
export const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [user, setUser] = useState<Me | null>(null)
  // Süresi dolmuş token açılışta atılır; geçerli token varsa kimlik sunucudan doğrulanana kadar bekle
  const [initializing, setInitializing] = useState(() => {
    const token = tokenStore.get()
    if (token && isTokenExpired(token)) tokenStore.set(null)
    return tokenStore.get() !== null
  })

  const logout = useCallback(() => {
    tokenStore.set(null)
    setUser(null)
    // Önceki kullanıcının rezervasyonları ekranda kalmasın
    queryClient.removeQueries({ queryKey: ['reservations'] })
  }, [queryClient])

  useEffect(() => {
    setUnauthorizedHandler(logout)
    return () => setUnauthorizedHandler(null)
  }, [logout])

  // Sayfa yenilendiğinde kayıtlı token ile kimliği sunucudan yeniden al
  useEffect(() => {
    if (!tokenStore.get()) return
    authApi
      .me()
      .then(setUser)
      .catch(() => tokenStore.set(null))
      .finally(() => setInitializing(false))
  }, [])

  const completeLogin = useCallback(async (accessToken: string) => {
    tokenStore.set(accessToken)
    const me = await authApi.me()
    setUser(me)
    return me
  }, [])

  const value = useMemo<AuthState>(
    () => ({
      user,
      initializing,
      isAdmin: user?.role === 'Admin',
      login: async (email, password) => completeLogin((await authApi.login(email, password)).accessToken),
      register: async (email, password, fullName) =>
        completeLogin((await authApi.register(email, password, fullName)).accessToken),
      logout,
    }),
    [user, initializing, completeLogin, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
