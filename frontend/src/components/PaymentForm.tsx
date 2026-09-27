import { useEffect, useRef } from 'react'

/**
 * Ödeme sağlayıcısının döndürdüğü form içeriğini sayfaya gömer. iyzico Checkout
 * Form içeriği <script> etiketleri taşır; innerHTML ile eklenen script'ler
 * çalışmadığı için her biri yeniden oluşturulur.
 *
 * NFR-04: kart bilgisi bu formdan doğrudan sağlayıcıya gider, API'ye hiç uğramaz.
 * İçerik yalnızca kendi API'mizden (sunucunun sağlayıcıdan aldığı yanıttan) gelir.
 */
export function PaymentForm({ content }: { content: string }) {
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const host = ref.current
    if (!host) return

    host.innerHTML = content
    host.querySelectorAll('script').forEach((old) => {
      const script = document.createElement('script')
      for (const attr of Array.from(old.attributes)) script.setAttribute(attr.name, attr.value)
      script.text = old.text
      old.replaceWith(script)
    })

    return () => {
      host.innerHTML = ''
    }
  }, [content])

  return (
    <div className="payment-form">
      {/* iyzico formu bu id'li elemana yerleşir */}
      <div id="iyzipay-checkout-form" className="responsive" />
      <div ref={ref} />
    </div>
  )
}
