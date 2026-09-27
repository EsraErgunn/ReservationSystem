/**
 * JWT yükünü imza doğrulamadan okur — yalnızca "token'ın süresi dolmuş mu" gibi
 * arayüz kararları için. Yetki kontrolü her istekte sunucuda yapılır (NFR-08).
 */
export function readJwtPayload(token: string): Record<string, unknown> | null {
  try {
    const part = token.split('.')[1]
    if (!part) return null
    const base64 = part.replace(/-/g, '+').replace(/_/g, '/')
    const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=')
    const json = decodeURIComponent(
      atob(padded)
        .split('')
        .map((c) => '%' + c.charCodeAt(0).toString(16).padStart(2, '0'))
        .join(''),
    )
    return JSON.parse(json) as Record<string, unknown>
  } catch {
    return null
  }
}

export function isTokenExpired(token: string, now = Date.now()): boolean {
  const exp = readJwtPayload(token)?.exp
  return typeof exp !== 'number' || exp * 1000 <= now
}
