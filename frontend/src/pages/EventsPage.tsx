import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { eventsApi } from '../api/endpoints'
import type { EventSummary } from '../api/types'
import { ErrorAlert } from '../components/Alert'
import { Spinner } from '../components/Spinner'
import { formatDateTime, formatPriceRange, formatShortDate, salesState } from '../utils/format'

/** FR-02: etkinlik listesi (ziyaretçiye açık). */
export function EventsPage() {
  const { data, isPending, error } = useQuery({ queryKey: ['events'], queryFn: () => eventsApi.list() })

  return (
    <>
      <section className="hero">
        <h1>Yaklaşan etkinlikler</h1>
        <p>Koltuğunu seç, 10 dakika senin için ayrılsın, ödemeyi tamamla.</p>
      </section>

      {isPending && <Spinner />}
      <ErrorAlert error={error} />

      {data && data.length === 0 && <p className="muted">Şu an listelenecek etkinlik yok.</p>}

      <div className="event-grid">
        {data?.map((event) => <EventCard key={event.id} event={event} />)}
      </div>
    </>
  )
}

function EventCard({ event }: { event: EventSummary }) {
  const state = salesState(event.salesStartAt, event.salesEndAt)
  const soldOut = state === 'open' && event.availableSeats === 0
  const fill = event.totalSeats === 0 ? 0 : 1 - event.availableSeats / event.totalSeats

  return (
    <Link to={`/etkinlik/${event.id}`} className="event-card">
      <div className="event-date">
        <span className="day">{new Date(event.eventDate).getDate()}</span>
        <span className="month">
          {new Date(event.eventDate).toLocaleDateString('tr-TR', { month: 'short' })}
        </span>
      </div>
      <div className="event-body">
        <h2>{event.title}</h2>
        <p className="muted">
          {event.venueName} · {event.city}
        </p>
        <p className="muted small">{formatDateTime(event.eventDate)}</p>

        <div className="event-meta">
          <strong>{formatPriceRange(event.minPrice, event.maxPrice)}</strong>
          {state === 'upcoming' && (
            <span className="badge badge-held">Satış {formatShortDate(event.salesStartAt)}</span>
          )}
          {state === 'closed' && <span className="badge badge-cancelled">Satış kapandı</span>}
          {soldOut && <span className="badge badge-failed">Tükendi</span>}
          {state === 'open' && !soldOut && (
            <span className="badge badge-confirmed">{event.availableSeats} koltuk müsait</span>
          )}
        </div>

        <div className="fill-bar" aria-label={`Doluluk %${Math.round(fill * 100)}`}>
          <span style={{ width: `${Math.round(fill * 100)}%` }} />
        </div>
      </div>
    </Link>
  )
}
