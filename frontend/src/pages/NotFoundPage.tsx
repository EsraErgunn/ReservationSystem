import { Link } from 'react-router-dom'

export function NotFoundPage() {
  return (
    <div className="narrow center-text">
      <h1>Sayfa bulunamadı</h1>
      <p className="muted">Aradığınız sayfa taşınmış ya da hiç var olmamış olabilir.</p>
      <Link to="/" className="btn btn-primary">Etkinliklere dön</Link>
    </div>
  )
}
