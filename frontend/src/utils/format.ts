import type { ReservationStatus } from '../api/types'

const currency = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY' })
const dateTime = new Intl.DateTimeFormat('tr-TR', {
  day: 'numeric',
  month: 'long',
  year: 'numeric',
  weekday: 'long',
  hour: '2-digit',
  minute: '2-digit',
})
const shortDate = new Intl.DateTimeFormat('tr-TR', {
  day: '2-digit',
  month: 'short',
  hour: '2-digit',
  minute: '2-digit',
})

export const formatPrice = (value: number) => currency.format(value)
export const formatDateTime = (iso: string) => dateTime.format(new Date(iso))
export const formatShortDate = (iso: string) => shortDate.format(new Date(iso))

export function formatPriceRange(min: number, max: number) {
  return min === max ? formatPrice(min) : `${formatPrice(min)} – ${formatPrice(max)}`
}

/** mm:ss — hold geri sayımı (FR-05). */
export function formatCountdown(ms: number) {
  const total = Math.max(0, Math.ceil(ms / 1000))
  const m = Math.floor(total / 60)
  const s = total % 60
  return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
}

export type SalesState = 'upcoming' | 'open' | 'closed'

/** BR-17: satış penceresi istemcide yalnızca gösterim için; asıl kontrol sunucuda. */
export function salesState(salesStartAt: string, salesEndAt: string, now = Date.now()): SalesState {
  if (now < Date.parse(salesStartAt)) return 'upcoming'
  if (now > Date.parse(salesEndAt)) return 'closed'
  return 'open'
}

export const reservationStatusLabel: Record<ReservationStatus, string> = {
  Held: 'Ödeme bekleniyor',
  Confirmed: 'Onaylandı',
  Expired: 'Süresi doldu',
  Failed: 'Ödeme başarısız',
  Cancelled: 'İptal edildi',
}

/** <input type="datetime-local"> değeri (yerel saat) → UTC ISO. */
export function localInputToIso(value: string) {
  return new Date(value).toISOString()
}

/** Date → <input type="datetime-local"> değeri (yerel saat). */
export function toLocalInput(date: Date) {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}
