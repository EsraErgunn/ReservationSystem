import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { useEffect, useState } from 'react'

export type HubStatus = 'connecting' | 'live' | 'offline'

/**
 * FR-09: etkinliğin SignalR grubuna katılır; sunucu "SeatsChanged" yayınladığında
 * `onChanged` çağrılır. Bağlantı koparsa otomatik yeniden bağlanır ve gruba tekrar
 * katılır (grup üyeliği bağlantıya bağlı, yeniden bağlanınca kaybolur).
 *
 * Hub anonim erişime açık — ziyaretçi de canlı doluluk görür.
 */
export function useSeatHub(eventId: string | undefined, onChanged: (seatIds: string[]) => void): HubStatus {
  const [status, setStatus] = useState<HubStatus>('connecting')

  useEffect(() => {
    if (!eventId) return

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/seats')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('SeatsChanged', (payload: { eventId: string; seatIds: string[] }) => {
      if (payload.eventId === eventId) onChanged(payload.seatIds)
    })

    const join = () => connection.invoke('JoinEvent', eventId)

    connection.onreconnecting(() => setStatus('connecting'))
    connection.onreconnected(async () => {
      await join()
      setStatus('live')
      onChanged([]) // kopukken kaçan değişiklikler için tam yenileme
    })
    connection.onclose(() => setStatus('offline'))

    let disposed = false
    connection
      .start()
      .then(join)
      .then(() => !disposed && setStatus('live'))
      .catch(() => !disposed && setStatus('offline'))

    return () => {
      disposed = true
      if (connection.state === HubConnectionState.Connected) {
        connection.invoke('LeaveEvent', eventId).catch(() => {})
      }
      connection.stop()
    }
    // onChanged bilinçli olarak bağımlılık değil: her render'da yeni bağlantı açılmasın.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [eventId])

  return status
}
