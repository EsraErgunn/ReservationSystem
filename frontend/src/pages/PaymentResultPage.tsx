import { useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { Link, useSearchParams } from 'react-router-dom'

/**
 * Ödeme sağlayıcısı callback'inden sonra API'nin yönlendirdiği sayfa
 * (PaymentsController: /odeme/sonuc?durum=...&rezervasyon=...).
 * Durum yalnızca bilgi amaçlı: kesin sonuç rezervasyon detayında, sunucudan okunur (BR-10).
 */
const messages = {
  basarili: { tone: 'success', title: 'Ödeme başarılı', text: 'Biletiniz onaylandı. İyi eğlenceler!' },
  basarisiz: {
    tone: 'error',
    title: 'Ödeme tamamlanamadı',
    text: 'Ödemeniz onaylanmadı ve koltuklar serbest bırakıldı. Tekrar koltuk seçerek deneyebilirsiniz.',
  },
  beklemede: {
    tone: 'warning',
    title: 'Ödeme doğrulanıyor',
    text: 'Ödeme sağlayıcısından yanıt bekleniyor. Birkaç dakika içinde rezervasyon durumunuz güncellenecek.',
  },
  gecersiz: { tone: 'error', title: 'Geçersiz istek', text: 'Ödeme bilgisi alınamadı.' },
} as const

export function PaymentResultPage() {
  const [params] = useSearchParams()
  const queryClient = useQueryClient()
  const status = (params.get('durum') ?? 'gecersiz') as keyof typeof messages
  const reservationId = params.get('rezervasyon')
  const message = messages[status] ?? messages.gecersiz

  useEffect(() => {
    queryClient.invalidateQueries({ queryKey: ['reservations'] })
  }, [queryClient])

  return (
    <div className="narrow">
      <div className={`card result result-${message.tone}`}>
        <div className="result-icon" aria-hidden="true">
          {message.tone === 'success' ? '✓' : message.tone === 'warning' ? '…' : '✕'}
        </div>
        <h1>{message.title}</h1>
        <p>{message.text}</p>
        <div className="actions center">
          {reservationId && (
            <Link to={`/rezervasyon/${reservationId}`} className="btn btn-primary">
              Rezervasyonu görüntüle
            </Link>
          )}
          <Link to="/" className="btn btn-ghost">
            Etkinliklere dön
          </Link>
        </div>
      </div>
    </div>
  )
}
