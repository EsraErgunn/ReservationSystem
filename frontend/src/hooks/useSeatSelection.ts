import { useCallback, useMemo, useState } from 'react'
import type { SeatMapItem } from '../api/types'

/** BR-03 — backend'deki ReservationRules.MaxSeatsPerReservation ile aynı. */
export const MAX_SEATS = 6

export type ToggleResult = 'added' | 'removed' | 'limit' | 'unavailable'

/**
 * Koltuk seçimi. Seçim, koltuk haritasının güncel hâliyle her render'da kesiştirilir:
 * başka bir kullanıcı bir koltuğu alırsa (SignalR → harita yenilenir) o koltuk
 * seçimden kendiliğinden düşer.
 */
export function useSeatSelection(seats: SeatMapItem[] | undefined) {
  const [selectedIds, setSelectedIds] = useState<string[]>([])

  const available = useMemo(
    () => new Map((seats ?? []).filter((s) => s.status === 'Available').map((s) => [s.eventSeatId, s])),
    [seats],
  )

  const selected = useMemo(
    () => selectedIds.map((id) => available.get(id)).filter((s): s is SeatMapItem => s !== undefined),
    [selectedIds, available],
  )

  const lostCount = selectedIds.length - selected.length

  const toggle = useCallback(
    (seat: SeatMapItem): ToggleResult => {
      if (selectedIds.includes(seat.eventSeatId)) {
        setSelectedIds((ids) => ids.filter((id) => id !== seat.eventSeatId))
        return 'removed'
      }
      if (!available.has(seat.eventSeatId)) return 'unavailable'
      if (selected.length >= MAX_SEATS) return 'limit'
      setSelectedIds((ids) => [...ids.filter((id) => available.has(id)), seat.eventSeatId])
      return 'added'
    },
    [selectedIds, available, selected.length],
  )

  const clear = useCallback(() => setSelectedIds([]), [])

  /** Kaybolan (başkasının aldığı) koltukları seçim listesinden de temizler. */
  const prune = useCallback(
    () => setSelectedIds((ids) => ids.filter((id) => available.has(id))),
    [available],
  )

  const total = selected.reduce((sum, s) => sum + s.price, 0)

  return { selected, selectedIds: new Set(selected.map((s) => s.eventSeatId)), toggle, clear, prune, total, lostCount }
}
