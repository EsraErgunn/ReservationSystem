import type { ReservationStatus } from '../api/types'
import { reservationStatusLabel } from '../utils/format'

export function StatusBadge({ status }: { status: ReservationStatus }) {
  return <span className={`badge badge-${status.toLowerCase()}`}>{reservationStatusLabel[status]}</span>
}
