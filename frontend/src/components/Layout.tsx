import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'

export function Layout() {
  const { user, isAdmin, logout } = useAuth()
  const navigate = useNavigate()

  return (
    <div className="app">
      <header className="topbar">
        <div className="container topbar-inner">
          <Link to="/" className="brand">
            <span className="brand-mark" aria-hidden="true">◆</span> Sahne
          </Link>

          <nav className="nav" aria-label="Ana menü">
            <NavLink to="/" end>Etkinlikler</NavLink>
            {user && <NavLink to="/biletlerim">Biletlerim</NavLink>}
            {isAdmin && <NavLink to="/admin">Yönetim</NavLink>}
          </nav>

          <div className="nav-user">
            {user ? (
              <>
                <span className="user-name" title={user.email}>{user.name}</span>
                <button
                  type="button"
                  className="btn btn-ghost btn-sm"
                  onClick={() => {
                    logout()
                    navigate('/')
                  }}
                >
                  Çıkış
                </button>
              </>
            ) : (
              <>
                <NavLink to="/giris" className="btn btn-ghost btn-sm">Giriş</NavLink>
                <NavLink to="/kayit" className="btn btn-primary btn-sm">Kayıt ol</NavLink>
              </>
            )}
          </div>
        </div>
      </header>

      <main className="container main">
        <Outlet />
      </main>

      <footer className="footer">
        <div className="container">Etkinlik Rezervasyon Sistemi · Clean Architecture · .NET 9 + React 19</div>
      </footer>
    </div>
  )
}
