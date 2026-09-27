import { describe, expect, it } from 'vitest'
import { formatCountdown, formatPriceRange, salesState } from './format'
import { isTokenExpired, readJwtPayload } from './jwt'
import { rowLabel } from './rows'

const b64url = (obj: object) =>
  btoa(String.fromCharCode(...new TextEncoder().encode(JSON.stringify(obj))))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '')
const token = (payload: object) => `${b64url({ alg: 'HS256' })}.${b64url(payload)}.sig`

describe('formatCountdown', () => {
  it('dakika:saniye biçiminde', () => {
    expect(formatCountdown(600_000)).toBe('10:00')
    expect(formatCountdown(61_000)).toBe('01:01')
  })
  it('kısmi saniyeyi yukarı yuvarlar, negatifte sıfır', () => {
    expect(formatCountdown(500)).toBe('00:01')
    expect(formatCountdown(-5)).toBe('00:00')
  })
})

describe('formatPriceRange', () => {
  it('tek fiyatı aralık olarak göstermez', () => {
    expect(formatPriceRange(100, 100)).not.toContain('–')
    expect(formatPriceRange(100, 200)).toContain('–')
  })
})

describe('salesState (BR-17)', () => {
  const start = '2026-01-10T00:00:00Z'
  const end = '2026-01-20T00:00:00Z'
  it.each([
    ['2026-01-09T23:59:59Z', 'upcoming'],
    ['2026-01-10T00:00:00Z', 'open'],
    ['2026-01-20T00:00:00Z', 'open'],
    ['2026-01-20T00:00:01Z', 'closed'],
  ])('%s → %s', (now, expected) => {
    expect(salesState(start, end, Date.parse(now))).toBe(expected)
  })
})

describe('jwt', () => {
  it('UTF-8 içeren yükü okur', () => {
    expect(readJwtPayload(token({ unique_name: 'Şule Ö.' }))?.unique_name).toBe('Şule Ö.')
  })
  it('bozuk token için null', () => {
    expect(readJwtPayload('bozuk')).toBeNull()
  })
  it('exp geçmişse süresi dolmuş sayılır', () => {
    const now = Date.parse('2026-01-01T00:00:00Z')
    expect(isTokenExpired(token({ exp: now / 1000 - 1 }), now)).toBe(true)
    expect(isTokenExpired(token({ exp: now / 1000 + 60 }), now)).toBe(false)
    expect(isTokenExpired(token({}), now)).toBe(true)
  })
})

describe('rowLabel', () => {
  it.each([
    [0, 'A'],
    [25, 'Z'],
    [26, 'AA'],
    [27, 'AB'],
  ])('%i → %s', (i, expected) => expect(rowLabel(i)).toBe(expected))
})
