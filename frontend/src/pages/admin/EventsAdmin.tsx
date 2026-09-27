import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { eventsApi } from '../../api/endpoints'
import type { ReservationStatus } from '../../api/types'
import { ErrorAlert } from '../../components/Alert'
import { Spinner } from '../../components/Spinner'
import { formatPrice, formatShortDate, reservationStatusLabel, salesState } from '../../utils/format'

export function EventsAdmin() {
  const [selected, setSelected] = useState<string | null>(null)
  const { data, isPending, error } = useQuery({
    queryKey: ['events', 'all'],
    queryFn: () => eventsApi.list(true),
  })

  if (isPending) return <Spinner />
  if (error) return <ErrorAlert error={error} />

  return (
    <div className="admin-grid">
      <div className="card">
        <h2>Tüm etkinlikler</h2>
        <div className="table-scroll">
          <table className="data-table">
            <thead>
              <tr>
                <th>Etkinlik</th>
                <th>Tarih</th>
                <th>Satış</th>
                <th className="num">Müsait</th>
              </tr>
            </thead>
            <tbody>
              {data.map((e) => {
                const state = salesState(e.salesStartAt, e.salesEndAt)
                return (
                  <tr
                    key={e.id}
                    className={selected === e.id ? 'row-selected' : ''}
                    onClick={() => setSelected(e.id)}
                  >
                    <td>
                      <button type="button" className="link-button" onClick={() => setSelected(e.id)}>
                        {e.title}
                      </button>
                      <div className="muted small">{e.venueName}</div>
                    </td>
                    <td>{formatShortDate(e.eventDate)}</td>
                    <td>{state === 'open' ? 'Açık' : state === 'upcoming' ? 'Başlamadı' : 'Kapandı'}</td>
                    <td className="num">
                      {e.availableSeats}/{e.totalSeats}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      </div>

      <div className="card">
        {selected ? <SalesPanel eventId={selected} /> : <p className="muted">Satış durumunu görmek için bir etkinlik seçin.</p>}
      </div>
    </div>
  )
}

/** FR-13: etkinliğin satış durumu. Canlı değil; 10 sn'de bir tazelenir. */
function SalesPanel({ eventId }: { eventId: string }) {
  const { data, isPending, error } = useQuery({
    queryKey: ['sales', eventId],
    queryFn: () => eventsApi.sales(eventId),
    refetchInterval: 10_000,
  })

  if (isPending) return <Spinner />
  if (error) return <ErrorAlert error={error} />

  const pct = (n: number) => (data.totalSeats === 0 ? 0 : Math.round((n / data.totalSeats) * 100))

  return (
    <>
      <h2>{data.title}</h2>
      <div className="stats">
        <Stat label="Satılan" value={data.soldSeats} sub={`%${pct(data.soldSeats)}`} />
        <Stat label="Ödeme bekleyen" value={data.heldSeats} sub={`%${pct(data.heldSeats)}`} />
        <Stat label="Müsait" value={data.availableSeats} sub={`%${pct(data.availableSeats)}`} />
        <Stat label="Gelir" value={formatPrice(data.confirmedRevenue)} />
      </div>

      <div className="stacked-bar" aria-hidden="true">
        <span className="bar-sold" style={{ width: `${pct(data.soldSeats)}%` }} />
        <span className="bar-held" style={{ width: `${pct(data.heldSeats)}%` }} />
      </div>

      <h3>Rezervasyonlar</h3>
      <ul className="status-counts">
        {(Object.entries(data.reservationsByStatus) as [ReservationStatus, number][]).map(([status, count]) => (
          <li key={status}>
            <span>{reservationStatusLabel[status]}</span>
            <strong>{count}</strong>
          </li>
        ))}
      </ul>

      <Link to={`/etkinlik/${data.eventId}`} className="btn btn-ghost btn-sm">
        Koltuk haritasını aç
      </Link>
    </>
  )
}

function Stat({ label, value, sub }: { label: string; value: string | number; sub?: string }) {
  return (
    <div className="stat">
      <span className="stat-label">{label}</span>
      <span className="stat-value">{value}</span>
      {sub && <span className="stat-sub">{sub}</span>}
    </div>
  )
}
