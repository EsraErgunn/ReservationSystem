import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useState } from 'react'
import { useLocation, useNavigate, useParams } from 'react-router-dom'
import { ApiError } from '../api/client'
import { eventsApi, reservationsApi } from '../api/endpoints'
import { useAuth } from '../auth/useAuth'
import { Alert, ErrorAlert } from '../components/Alert'
import { SeatMap } from '../components/SeatMap'
import { Spinner } from '../components/Spinner'
import { useSeatHub } from '../hooks/useSeatHub'
import { MAX_SEATS, useSeatSelection } from '../hooks/useSeatSelection'
import { formatDateTime, formatPrice, formatShortDate, salesState } from '../utils/format'

/** FR-03 / FR-04 / FR-09: koltuk haritası, seçim ve hold oluşturma. */
export function EventDetailPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()
  const { user } = useAuth()
  const [notice, setNotice] = useState<string | null>(null)

  const eventQuery = useQuery({ queryKey: ['event', id], queryFn: () => eventsApi.get(id) })
  const seatQuery = useQuery({ queryKey: ['seats', id], queryFn: () => eventsApi.seatMap(id) })

  const selection = useSeatSelection(seatQuery.data?.seats)

  // Başka bir kullanıcı koltuk aldığında/bıraktığında haritayı yenile
  const onSeatsChanged = useCallback(() => {
    queryClient.invalidateQueries({ queryKey: ['seats', id] })
    queryClient.invalidateQueries({ queryKey: ['event', id] })
  }, [queryClient, id])
  const hubStatus = useSeatHub(id, onSeatsChanged)

  const reserve = useMutation({
    mutationFn: () => reservationsApi.create(id, selection.selected.map((s) => s.eventSeatId)),
    onSuccess: (reservation) => {
      queryClient.invalidateQueries({ queryKey: ['reservations'] })
      navigate(`/rezervasyon/${reservation.id}`)
    },
    onError: (error) => {
      // BR-06: koltuk başkasına geçtiyse haritayı hemen tazele
      if (error instanceof ApiError && error.status === 409) {
        onSeatsChanged()
        selection.prune()
      }
    },
  })

  if (eventQuery.isPending) return <Spinner />
  if (eventQuery.error) return <ErrorAlert error={eventQuery.error} />
  const event = eventQuery.data

  const state = salesState(event.salesStartAt, event.salesEndAt)
  const canSelect = state === 'open' && !reserve.isPending

  const handleToggle = (seat: Parameters<typeof selection.toggle>[0]) => {
    const result = selection.toggle(seat)
    setNotice(result === 'limit' ? `Tek rezervasyonda en fazla ${MAX_SEATS} koltuk seçebilirsiniz.` : null)
  }

  const handleReserve = () => {
    if (!user) {
      navigate(`/giris?donus=${encodeURIComponent(location.pathname)}`)
      return
    }
    reserve.mutate()
  }

  return (
    <div className="detail-layout">
      <section className="detail-main">
        <header className="detail-header">
          <h1>{event.title}</h1>
          <p className="muted">
            {formatDateTime(event.eventDate)} · {event.venueName}, {event.city}
          </p>
          {event.description && <p>{event.description}</p>}
          <p className="small muted">{event.venueAddress}</p>
        </header>

        {state === 'upcoming' && (
          <Alert tone="info">
            Biletler {formatDateTime(event.salesStartAt)} tarihinde satışa açılacak. Koltuk haritasını
            şimdiden inceleyebilirsiniz.
          </Alert>
        )}
        {state === 'closed' && <Alert tone="warning">Bu etkinlik için bilet satışı sona erdi.</Alert>}

        <div className="card">
          <div className="card-head">
            <h2>Koltuk seçimi</h2>
            <span className={`live live-${hubStatus}`} title="Gerçek zamanlı doluluk">
              {hubStatus === 'live' ? 'Canlı' : hubStatus === 'connecting' ? 'Bağlanıyor' : 'Çevrimdışı'}
            </span>
          </div>
          {seatQuery.isPending && <Spinner label="Koltuklar yükleniyor..." />}
          <ErrorAlert error={seatQuery.error} />
          {seatQuery.data && (
            <SeatMap
              seats={seatQuery.data.seats}
              selectedIds={selection.selectedIds}
              onToggle={handleToggle}
              disabled={!canSelect}
            />
          )}
        </div>
      </section>

      <aside className="detail-side">
        <div className="card sticky">
          <h2>Seçiminiz</h2>

          {selection.lostCount > 0 && (
            <Alert tone="warning">
              Seçtiğiniz {selection.lostCount} koltuk az önce başka biri tarafından ayrıldı.
            </Alert>
          )}
          {notice && <Alert tone="warning">{notice}</Alert>}

          {selection.selected.length === 0 ? (
            <p className="muted">
              {state === 'open'
                ? `Haritadan en fazla ${MAX_SEATS} koltuk seçin.`
                : state === 'upcoming'
                  ? `Satış ${formatShortDate(event.salesStartAt)} tarihinde başlar.`
                  : 'Satış kapalı.'}
            </p>
          ) : (
            <ul className="selection-list">
              {selection.selected.map((s) => (
                <li key={s.eventSeatId}>
                  <span>
                    {s.rowLabel} sırası, {s.seatNumber} no
                  </span>
                  <span>{formatPrice(s.price)}</span>
                </li>
              ))}
            </ul>
          )}

          <div className="total-row">
            <span>Toplam</span>
            <strong>{formatPrice(selection.total)}</strong>
          </div>

          <ErrorAlert error={reserve.error} />

          <button
            type="button"
            className="btn btn-primary btn-block"
            disabled={!canSelect || selection.selected.length === 0}
            onClick={handleReserve}
          >
            {reserve.isPending ? 'Ayrılıyor...' : user ? 'Koltukları ayır' : 'Giriş yap ve ayır'}
          </button>
          <p className="small muted">Koltuklar 10 dakika boyunca sizin için tutulur.</p>
        </div>
      </aside>
    </div>
  )
}
