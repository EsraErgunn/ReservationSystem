import type { ProblemDetails } from './types'

const TOKEN_KEY = 'rs.accessToken'

/** Sunucudan dönen hata. `code` backend'deki AppException.Code (ör. "seat.taken"). */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly traceId?: string

  constructor(status: number, code: string, message: string, traceId?: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.traceId = traceId
  }
}

export const tokenStore = {
  get: (): string | null => {
    try {
      return localStorage.getItem(TOKEN_KEY)
    } catch {
      return null
    }
  },
  set: (token: string | null) => {
    try {
      if (token) localStorage.setItem(TOKEN_KEY, token)
      else localStorage.removeItem(TOKEN_KEY)
    } catch {
      /* gizli sekme vb. — oturum bellekte kalmaz, sorun değil */
    }
  },
}

let onUnauthorized: (() => void) | null = null

/** AuthProvider, süresi dolmuş token ile gelen 401'de oturumu kapatmak için kaydolur. */
export function setUnauthorizedHandler(handler: (() => void) | null) {
  onUnauthorized = handler
}

const FALLBACK_MESSAGES: Record<number, string> = {
  401: 'Oturumunuzun süresi doldu, lütfen tekrar giriş yapın.',
  403: 'Bu işlem için yetkiniz yok.',
  404: 'Aradığınız kayıt bulunamadı.',
  429: 'Çok fazla istek gönderdiniz. Lütfen bir dakika sonra tekrar deneyin.',
}

async function toApiError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails = {}
  try {
    problem = (await response.json()) as ProblemDetails
  } catch {
    /* gövdesiz yanıt (ör. rate limiter 429) */
  }

  // ASP.NET model doğrulama hatası: errors sözlüğü
  const validation = problem.errors
    ? Object.values(problem.errors).flat().join(' ')
    : undefined

  const message =
    problem.detail ??
    validation ??
    FALLBACK_MESSAGES[response.status] ??
    'Beklenmeyen bir hata oluştu.'

  return new ApiError(response.status, problem.title ?? 'error', message, problem.traceId)
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  const token = tokenStore.get()
  if (token) headers.set('Authorization', `Bearer ${token}`)
  if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')

  let response: Response
  try {
    response = await fetch(path, { ...init, headers })
  } catch {
    throw new ApiError(0, 'network', 'Sunucuya ulaşılamadı. Bağlantınızı kontrol edin.')
  }

  if (!response.ok) {
    if (response.status === 401 && token) onUnauthorized?.()
    throw await toApiError(response)
  }

  if (response.status === 204) return undefined as T
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const post = <T>(path: string, body?: unknown) =>
  api<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) })
