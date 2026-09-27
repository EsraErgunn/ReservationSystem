import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { paymentsApi, reservationsApi } from '../api/endpoints'
import { Alert, ErrorAlert } from '../components/Alert'
import { PaymentForm } from '../components/PaymentForm'
import { Spinner } from '../components/Spinner'
import { StatusBadge } from '../components/StatusBadge'
import { useCountdown } from '../hooks/useCountdown'
import { formatCountdown, formatDateTime, formatPrice } from '../utils/format'

/** FR-05 / FR-06 / FR-11: geri sayım, ödeme başlatma, iptal. */
export function ReservationPage() {
  const { id = '' } = useParams()
  const queryClient = useQueryClient()
  const [formContent, setFormContent] = useState<string | null>(null)

  const { data, isPending, error, refetch } = useQuery({
    queryKey: ['reservations', id],
    queryFn: () => reservationsApi.get(id),
  })

  const remaining = useCountdown(data?.status === 'Held' ? data.heldUntil : null)
  const expired = data?.status === 'Held' && remaining === 0

  // Süre dolunca sunucudaki durumu çek (temizlik görevi birkaç saniye içinde Expired yapar)
  useEffect(() => {
    if (!expired) return
    const t = window.setTimeout(() => refetch(), 6000)
    return () => window.clearTimeout(t)
  }, [expired, refetch])

  const startPayment = useMutation({
    mutationFn: () => paymentsApi.start(id),
    onSuccess: (result) => setFormContent(result.formContent),
  })

  const cancel = useMutation({
    mutationFn: () => reservationsApi.cancel(id),
    onSuccess: () => {
      setFormContent(null)
      queryClient.invalidateQueries({ queryKey: ['reservations'] })
    },
  })

  if (isPending) return <Spinner />
  if (error) return <ErrorAlert error={error} />

  const urgent = remaining > 0 && remaining < 60_000

  return (
    <div className="narrow">
      <Link to="/biletlerim" className="back-link">← Biletlerim</Link>

      <div className="card">
        <div className="card-head">
          <div>
            <h1>{data.eventTitle}</h1>
            <p className="muted">
              {formatDateTime(data.eventDate)} · {data.venueName}
            </p>
          </div>
          <StatusBadge status={data.status} />
        </div>

        {data.status === 'Held' && (
          <div className={`countdown ${urgent ? 'countdown-urgent' : ''}`} aria-live="polite">
            <span>Ödeme için kalan süre</span>
            <strong>{formatCountdown(remaining)}</strong>
          </div>
        )}

        <table className="seat-table">
          <thead>
            <tr>
              <th>Sıra</th>
              <th>Koltuk</th>
              <th className="num">Fiyat</th>
            </tr>
          </thead>
          <tbody>
            {data.seats.map((s) => (
              <tr key={s.eventSeatId}>
                <td>{s.rowLabel}</td>
                <td>{s.seatNumber}</td>
                <td className="num">{formatPrice(s.price)}</td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr>
              <td colSpan={2}>Toplam</td>
              <td className="num">
                <strong>{formatPrice(data.totalAmount)}</strong>
              </td>
            </tr>
          </tfoot>
        </table>

        {data.status === 'Held' && !expired && (
          <>
            {data.lastPaymentStatus === 'Failed' && (
              <Alert tone="warning">Önceki ödeme denemesi başarısız oldu. Tekrar deneyebilirsiniz.</Alert>
            )}
            <ErrorAlert error={startPayment.error ?? cancel.error} />

            {formContent ? (
              <PaymentForm content={formContent} />
            ) : (
              <div className="actions">
                <button
                  type="button"
                  className="btn btn-primary"
                  disabled={startPayment.isPending || cancel.isPending}
                  onClick={() => startPayment.mutate()}
                >
                  {startPayment.isPending ? 'Ödeme hazırlanıyor...' : `${formatPrice(data.totalAmount)} öde`}
                </button>
                <button
                  type="button"
                  className="btn btn-ghost"
                  disabled={startPayment.isPending || cancel.isPending}
                  onClick={() => cancel.mutate()}
                >
                  {cancel.isPending ? 'İptal ediliyor...' : 'Rezervasyonu iptal et'}
                </button>
              </div>
            )}
          </>
        )}

        {expired && (
          <Alert tone="warning">
            Süre doldu, koltuklar serbest bırakılıyor.{' '}
            <Link to={`/etkinlik/${data.eventId}`}>Yeniden koltuk seçin</Link>.
          </Alert>
        )}

        {data.status === 'Confirmed' && (
          <div className="ticket">
            <p>
              <strong>Biletiniz hazır!</strong> Etkinlik girişinde bu rezervasyon numarasını gösterin:
            </p>
            <code className="ticket-code">{data.id.slice(-12).toUpperCase()}</code>
          </div>
        )}

        {(data.status === 'Expired' || data.status === 'Failed' || data.status === 'Cancelled') && (
          <Alert tone="info">
            Bu rezervasyon kapandı ve koltuklar serbest bırakıldı.{' '}
            <Link to={`/etkinlik/${data.eventId}`}>Etkinliğe dön</Link>
          </Alert>
        )}
      </div>
    </div>
  )
}
