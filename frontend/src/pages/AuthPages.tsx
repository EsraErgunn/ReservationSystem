import { useState, type FormEvent } from 'react'
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import { ErrorAlert } from '../components/Alert'

/** Açık yönlendirme (open redirect) olmasın: yalnızca site içi yol kabul edilir. */
function safeReturnPath(value: string | null) {
  return value && value.startsWith('/') && !value.startsWith('//') ? value : '/'
}

export function LoginPage() {
  const { login, user } = useAuth()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const returnTo = safeReturnPath(params.get('donus'))

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [pending, setPending] = useState(false)

  if (user) return <Navigate to={returnTo} replace />

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setPending(true)
    setError(null)
    try {
      await login(email, password)
      navigate(returnTo, { replace: true })
    } catch (err) {
      setError(err)
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="auth-card card">
      <h1>Giriş yap</h1>
      <form onSubmit={submit} className="form">
        <label>
          E-posta
          <input type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
        </label>
        <label>
          Parola
          <input
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </label>
        <ErrorAlert error={error} />
        <button type="submit" className="btn btn-primary btn-block" disabled={pending}>
          {pending ? 'Giriş yapılıyor...' : 'Giriş yap'}
        </button>
      </form>
      <p className="muted small">
        Hesabınız yok mu? <Link to={`/kayit?donus=${encodeURIComponent(returnTo)}`}>Kayıt olun</Link>
      </p>
    </div>
  )
}

export function RegisterPage() {
  const { register, user } = useAuth()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const returnTo = safeReturnPath(params.get('donus'))

  const [fullName, setFullName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<unknown>(null)
  const [pending, setPending] = useState(false)

  if (user) return <Navigate to={returnTo} replace />

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setPending(true)
    setError(null)
    try {
      await register(email, password, fullName)
      navigate(returnTo, { replace: true })
    } catch (err) {
      setError(err)
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="auth-card card">
      <h1>Kayıt ol</h1>
      <form onSubmit={submit} className="form">
        <label>
          Ad soyad
          <input autoComplete="name" required maxLength={100} value={fullName} onChange={(e) => setFullName(e.target.value)} />
        </label>
        <label>
          E-posta
          <input type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
        </label>
        <label>
          Parola
          <input
            type="password"
            autoComplete="new-password"
            required
            minLength={8}
            maxLength={128}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
          <small className="muted">En az 8 karakter. Uzun bir parola cümlesi en güvenlisidir.</small>
        </label>
        <ErrorAlert error={error} />
        <button type="submit" className="btn btn-primary btn-block" disabled={pending}>
          {pending ? 'Kaydediliyor...' : 'Kayıt ol'}
        </button>
      </form>
      <p className="muted small">
        Zaten hesabınız var mı? <Link to={`/giris?donus=${encodeURIComponent(returnTo)}`}>Giriş yapın</Link>
      </p>
    </div>
  )
}
