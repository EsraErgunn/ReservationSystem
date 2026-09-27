import type { ReactNode } from 'react'
import { ApiError } from '../api/client'

type Tone = 'error' | 'warning' | 'success' | 'info'

export function Alert({ tone = 'info', children }: { tone?: Tone; children: ReactNode }) {
  return (
    <div className={`alert alert-${tone}`} role={tone === 'error' ? 'alert' : 'status'}>
      {children}
    </div>
  )
}

/** Sunucu hatasını kullanıcıya gösterir; destek için traceId'yi de (NFR-09). */
export function ErrorAlert({ error }: { error: unknown }) {
  if (!error) return null
  const message = error instanceof Error ? error.message : 'Beklenmeyen bir hata oluştu.'
  const traceId = error instanceof ApiError ? error.traceId : undefined

  return (
    <Alert tone="error">
      {message}
      {traceId && <small className="trace">Hata kodu: {traceId}</small>}
    </Alert>
  )
}
