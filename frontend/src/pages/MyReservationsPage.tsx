import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { reservationsApi } from '../api/endpoints'
import { ErrorAlert } from '../components/Alert'
import { Spinner } from '../components/Spinner'
import { StatusBadge } from '../components/StatusBadge'
import { formatDateTime, formatPrice, formatShortDate } from '../utils/format'

/** FR-10: kullanıcının rezervasyon / bilet geçmişi. */
export function MyReservationsPage() {
  const { data, isPending, error } = useQuery({ queryKey: ['reservations'], queryFn: reservationsApi.mine })

  return (
    <>
      <h1>Biletlerim</h1>
      {isPending && <Spinner />}
      <ErrorAlert error={error} />

      {data?.length === 0 && (
        <p className="muted">
          Henüz rezervasyonunuz yok. <Link to="/">Etkinliklere göz atın</Link>.
        </p>
      )}

      <ul className="reservation-list">
        {data?.map((r) => (
          <li key={r.id}>
            <Link to={`/rezervasyon/${r.id}`} className="reservation-item">
              <div>
                <strong>{r.eventTitle}</strong>
                <span className="muted small">{formatDateTime(r.eventDate)}</span>
              </div>
              <div className="reservation-meta">
                <StatusBadge status={r.status} />
                <span>{r.seatCount > 0 ? `${r.seatCount} koltuk · ` : ''}{formatPrice(r.totalAmount)}</span>
                <span className="muted small">
                  {r.heldUntil ? `Son ödeme: ${formatShortDate(r.heldUntil)}` : formatShortDate(r.createdAt)}
                </span>
              </div>
            </Link>
          </li>
        ))}
      </ul>
    </>
  )
}
