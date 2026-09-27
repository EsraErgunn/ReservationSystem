import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { ApiError } from './api/client'
import { AuthProvider } from './auth/AuthContext'
import { RequireAuth } from './auth/RequireAuth'
import { Layout } from './components/Layout'
import { AdminPage } from './pages/admin/AdminPage'
import { LoginPage, RegisterPage } from './pages/AuthPages'
import { EventDetailPage } from './pages/EventDetailPage'
import { EventsPage } from './pages/EventsPage'
import { MyReservationsPage } from './pages/MyReservationsPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { PaymentResultPage } from './pages/PaymentResultPage'
import { ReservationPage } from './pages/ReservationPage'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 15_000,
      // 4xx tekrar denenmez: yetki / bulunamadı hataları tekrarla düzelmez
      retry: (count, error) => !(error instanceof ApiError && error.status >= 400 && error.status < 500) && count < 2,
    },
  },
})

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthProvider>
          <Routes>
            <Route element={<Layout />}>
              <Route index element={<EventsPage />} />
              <Route path="etkinlik/:id" element={<EventDetailPage />} />
              <Route path="giris" element={<LoginPage />} />
              <Route path="kayit" element={<RegisterPage />} />
              <Route path="odeme/sonuc" element={<PaymentResultPage />} />
              <Route
                path="biletlerim"
                element={
                  <RequireAuth>
                    <MyReservationsPage />
                  </RequireAuth>
                }
              />
              <Route
                path="rezervasyon/:id"
                element={
                  <RequireAuth>
                    <ReservationPage />
                  </RequireAuth>
                }
              />
              <Route
                path="admin"
                element={
                  <RequireAuth admin>
                    <AdminPage />
                  </RequireAuth>
                }
              />
              <Route path="*" element={<NotFoundPage />} />
            </Route>
          </Routes>
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
