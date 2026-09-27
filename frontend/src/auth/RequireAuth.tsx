import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { Spinner } from '../components/Spinner'
import { useAuth } from './useAuth'

/**
 * Yalnızca yönlendirme — gerçek yetki kontrolü sunucuda (NFR-08). Bu bileşen
 * kullanıcıyı boşuna 401/403 alacağı bir sayfaya sokmamak için var.
 */
export function RequireAuth({ children, admin = false }: { children: ReactNode; admin?: boolean }) {
  const { user, initializing, isAdmin } = useAuth()
  const location = useLocation()

  if (initializing) return <Spinner label="Oturum doğrulanıyor..." />

  if (!user) {
    const returnTo = encodeURIComponent(location.pathname + location.search)
    return <Navigate to={`/giris?donus=${returnTo}`} replace />
  }

  if (admin && !isAdmin) return <Navigate to="/" replace />

  return <>{children}</>
}
