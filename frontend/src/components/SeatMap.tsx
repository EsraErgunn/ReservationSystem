import { useMemo } from 'react'
import type { SeatMapItem } from '../api/types'
import { formatPrice } from '../utils/format'

interface Props {
  seats: SeatMapItem[]
  selectedIds: Set<string>
  onToggle: (seat: SeatMapItem) => void
  disabled?: boolean
}

const statusLabel = { Available: 'Müsait', Held: 'Ayrılmış', Sold: 'Satıldı' } as const

/** FR-03: sıra bazlı koltuk haritası. Klavye ile de gezilebilir (her koltuk bir düğme). */
export function SeatMap({ seats, selectedIds, onToggle, disabled = false }: Props) {
  const rows = useMemo(() => {
    const map = new Map<string, SeatMapItem[]>()
    for (const seat of seats) {
      const row = map.get(seat.rowLabel) ?? []
      row.push(seat)
      map.set(seat.rowLabel, row)
    }
    return [...map.entries()]
      .sort(([a], [b]) => a.localeCompare(b, 'tr'))
      .map(([label, list]) => [label, list.sort((a, b) => a.seatNumber - b.seatNumber)] as const)
  }, [seats])

  return (
    <div className="seatmap">
      <div className="stage" aria-hidden="true">SAHNE</div>

      <div className="seatmap-scroll">
        <div className="seat-rows">
          {rows.map(([label, rowSeats]) => (
            <div className="seat-row" key={label}>
              <span className="row-label">{label}</span>
              <div className="row-seats">
                {rowSeats.map((seat) => {
                  const selected = selectedIds.has(seat.eventSeatId)
                  const state = selected ? 'selected' : seat.status.toLowerCase()
                  const clickable = !disabled && (seat.status === 'Available' || selected)
                  return (
                    <button
                      key={seat.eventSeatId}
                      type="button"
                      className={`seat seat-${state}`}
                      disabled={!clickable}
                      aria-pressed={selected}
                      aria-label={`${seat.rowLabel} sırası ${seat.seatNumber} numara, ${formatPrice(seat.price)}, ${
                        selected ? 'seçildi' : statusLabel[seat.status]
                      }`}
                      title={`${seat.rowLabel}-${seat.seatNumber} · ${formatPrice(seat.price)}`}
                      onClick={() => onToggle(seat)}
                    >
                      {seat.seatNumber}
                    </button>
                  )
                })}
              </div>
              <span className="row-label">{label}</span>
            </div>
          ))}
        </div>
      </div>

      <ul className="legend" aria-label="Koltuk durumları">
        <li><span className="seat seat-available" aria-hidden="true" /> Müsait</li>
        <li><span className="seat seat-selected" aria-hidden="true" /> Seçiminiz</li>
        <li><span className="seat seat-held" aria-hidden="true" /> Ayrılmış</li>
        <li><span className="seat seat-sold" aria-hidden="true" /> Satıldı</li>
      </ul>
    </div>
  )
}
