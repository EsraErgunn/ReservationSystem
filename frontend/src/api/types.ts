// Backend DTO'larının birebir karşılıkları (ReservationSystem.Application).

export type SeatStatus = 'Available' | 'Held' | 'Sold'
export type ReservationStatus = 'Held' | 'Confirmed' | 'Expired' | 'Failed' | 'Cancelled'
export type PaymentStatus = 'Pending' | 'Succeeded' | 'Failed' | 'Abandoned'
export type UserRole = 'User' | 'Admin'

export interface AuthResult {
  userId: string
  email: string
  fullName: string
  accessToken: string
}

export interface Me {
  id: string
  email: string
  name: string
  role: UserRole
}

export interface EventSummary {
  id: string
  title: string
  description: string | null
  eventDate: string
  salesStartAt: string
  salesEndAt: string
  venueId: string
  venueName: string
  city: string
  minPrice: number
  maxPrice: number
  availableSeats: number
  totalSeats: number
}

export interface EventDetail extends EventSummary {
  venueAddress: string
}

export interface SeatMapItem {
  eventSeatId: string
  rowLabel: string
  seatNumber: number
  price: number
  status: SeatStatus
}

export interface SeatMap {
  eventId: string
  eventTitle: string
  seats: SeatMapItem[]
}

export interface ReservationItem {
  eventSeatId: string
  price: number
}

export interface Reservation {
  id: string
  eventId: string
  status: ReservationStatus
  heldUntil: string
  totalAmount: number
  items: ReservationItem[]
}

export interface ReservationSummary {
  id: string
  eventId: string
  eventTitle: string
  eventDate: string
  status: ReservationStatus
  seatCount: number
  totalAmount: number
  heldUntil: string | null
  createdAt: string
}

export interface ReservationSeat {
  eventSeatId: string
  rowLabel: string
  seatNumber: number
  price: number
}

export interface ReservationDetail {
  id: string
  eventId: string
  eventTitle: string
  eventDate: string
  venueName: string
  status: ReservationStatus
  totalAmount: number
  heldUntil: string | null
  createdAt: string
  seats: ReservationSeat[]
  lastPaymentStatus: PaymentStatus | null
}

export interface StartPaymentResult {
  formContent: string
}

export interface Venue {
  id: string
  name: string
  address: string
  city: string
  seatCount: number
}

export interface VenueRowInput {
  rowLabel: string
  seatCount: number
}

export interface CreateVenueInput {
  name: string
  address: string
  city: string
  rows: VenueRowInput[]
}

export interface RowPriceInput {
  rowLabel: string
  price: number
}

export interface CreateEventInput {
  venueId: string
  title: string
  description: string | null
  eventDate: string
  salesStartAt: string
  salesEndAt: string
  defaultPrice: number
  rowPrices: RowPriceInput[] | null
}

export interface EventCreated {
  id: string
  title: string
  seatCount: number
}

export interface EventSales {
  eventId: string
  title: string
  eventDate: string
  totalSeats: number
  availableSeats: number
  heldSeats: number
  soldSeats: number
  confirmedRevenue: number
  reservationsByStatus: Record<ReservationStatus, number>
}

/** RFC 9457 ProblemDetails — AppExceptionHandler'ın döndürdüğü gövde. */
export interface ProblemDetails {
  title?: string
  status?: number
  detail?: string
  traceId?: string
  errors?: Record<string, string[]>
}
