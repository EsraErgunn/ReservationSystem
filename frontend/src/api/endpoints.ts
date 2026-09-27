import { api, post } from './client'
import type {
  AuthResult,
  CreateEventInput,
  CreateVenueInput,
  EventCreated,
  EventDetail,
  EventSales,
  EventSummary,
  Me,
  Reservation,
  ReservationDetail,
  ReservationSummary,
  SeatMap,
  StartPaymentResult,
  Venue,
} from './types'

export const authApi = {
  login: (email: string, password: string) =>
    post<AuthResult>('/api/auth/login', { email, password }),
  register: (email: string, password: string, fullName: string) =>
    post<AuthResult>('/api/auth/register', { email, password, fullName }),
  me: () => api<Me>('/api/auth/me'),
}

export const eventsApi = {
  list: (includePast = false) =>
    api<EventSummary[]>(`/api/events${includePast ? '?includePast=true' : ''}`),
  get: (id: string) => api<EventDetail>(`/api/events/${id}`),
  // Sunucu 5 sn'lik Cache-Control gönderiyor; SignalR bildirimi sonrası yeniden
  // çekerken tarayıcı cache'ine takılmamak için no-store.
  seatMap: (id: string) => api<SeatMap>(`/api/events/${id}/seats`, { cache: 'no-store' }),
  sales: (id: string) => api<EventSales>(`/api/events/${id}/sales`),
  create: (input: CreateEventInput) => post<EventCreated>('/api/events', input),
}

export const reservationsApi = {
  create: (eventId: string, eventSeatIds: string[]) =>
    post<Reservation>('/api/reservations', { eventId, eventSeatIds }),
  mine: () => api<ReservationSummary[]>('/api/reservations'),
  get: (id: string) => api<ReservationDetail>(`/api/reservations/${id}`),
  cancel: (id: string) => post<void>(`/api/reservations/${id}/cancel`),
}

export const paymentsApi = {
  // BR-09: tutar gönderilmez, sunucu rezervasyondan hesaplar.
  start: (reservationId: string) =>
    post<StartPaymentResult>('/api/payments/start', { reservationId }),
}

export const venuesApi = {
  list: () => api<Venue[]>('/api/venues'),
  create: (input: CreateVenueInput) => post<Venue>('/api/venues', input),
}
