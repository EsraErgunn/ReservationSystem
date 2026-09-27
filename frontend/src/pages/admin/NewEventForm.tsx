import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { eventsApi, venuesApi } from '../../api/endpoints'
import type { RowPriceInput } from '../../api/types'
import { Alert, ErrorAlert } from '../../components/Alert'
import { Spinner } from '../../components/Spinner'
import { localInputToIso, toLocalInput } from '../../utils/format'

const inDays = (days: number, hour = 20) => {
  const d = new Date()
  d.setDate(d.getDate() + days)
  d.setHours(hour, 0, 0, 0)
  return toLocalInput(d)
}

export function NewEventForm({ onCreated }: { onCreated: () => void }) {
  const queryClient = useQueryClient()
  const venues = useQuery({ queryKey: ['venues'], queryFn: venuesApi.list })

  const [venueId, setVenueId] = useState('')
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [eventDate, setEventDate] = useState(() => inDays(30))
  const [salesStartAt, setSalesStartAt] = useState(() => toLocalInput(new Date()))
  const [salesEndAt, setSalesEndAt] = useState(() => inDays(30, 18))
  const [defaultPrice, setDefaultPrice] = useState(400)
  const [rowPrices, setRowPrices] = useState<RowPriceInput[]>([])

  const create = useMutation({
    mutationFn: () =>
      eventsApi.create({
        venueId: venueId || venues.data?.[0]?.id || '',
        title,
        description: description || null,
        eventDate: localInputToIso(eventDate),
        salesStartAt: localInputToIso(salesStartAt),
        salesEndAt: localInputToIso(salesEndAt),
        defaultPrice,
        rowPrices: rowPrices.length ? rowPrices : null,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['events'] })
      onCreated()
    },
  })

  if (venues.isPending) return <Spinner />
  if (venues.error) return <ErrorAlert error={venues.error} />
  if (venues.data.length === 0) return <Alert tone="info">Önce bir mekân oluşturun.</Alert>

  const submit = (e: FormEvent) => {
    e.preventDefault()
    create.mutate()
  }

  return (
    <form className="card form" onSubmit={submit}>
      <h2>Yeni etkinlik</h2>
      <div className="form-grid">
        <label className="span-2">
          Mekân
          <select value={venueId || venues.data[0].id} onChange={(e) => setVenueId(e.target.value)}>
            {venues.data.map((v) => (
              <option key={v.id} value={v.id}>
                {v.name} — {v.city} ({v.seatCount} koltuk)
              </option>
            ))}
          </select>
        </label>
        <label className="span-2">
          Başlık
          <input required maxLength={200} value={title} onChange={(e) => setTitle(e.target.value)} />
        </label>
        <label className="span-2">
          Açıklama
          <textarea maxLength={2000} rows={3} value={description} onChange={(e) => setDescription(e.target.value)} />
        </label>
        <label>
          Etkinlik tarihi
          <input type="datetime-local" required value={eventDate} onChange={(e) => setEventDate(e.target.value)} />
        </label>
        <label>
          Varsayılan fiyat (₺)
          <input
            type="number"
            min={1}
            step="0.01"
            required
            value={defaultPrice}
            onChange={(e) => setDefaultPrice(e.target.valueAsNumber)}
          />
        </label>
        <label>
          Satış başlangıcı
          <input type="datetime-local" required value={salesStartAt} onChange={(e) => setSalesStartAt(e.target.value)} />
        </label>
        <label>
          Satış bitişi
          <input type="datetime-local" required value={salesEndAt} onChange={(e) => setSalesEndAt(e.target.value)} />
        </label>
      </div>

      <h3>Sıra bazlı fiyat (isteğe bağlı)</h3>
      {rowPrices.map((rp, i) => (
        <div className="row-editor-item" key={i}>
          <input
            aria-label="Sıra"
            placeholder="Sıra"
            required
            maxLength={8}
            value={rp.rowLabel}
            onChange={(e) =>
              setRowPrices((list) => list.map((x, j) => (j === i ? { ...x, rowLabel: e.target.value.toUpperCase() } : x)))
            }
          />
          <input
            aria-label="Fiyat"
            type="number"
            min={1}
            step="0.01"
            required
            value={rp.price}
            onChange={(e) =>
              setRowPrices((list) => list.map((x, j) => (j === i ? { ...x, price: e.target.valueAsNumber } : x)))
            }
          />
          <button
            type="button"
            className="btn btn-ghost btn-sm"
            aria-label="Kaldır"
            onClick={() => setRowPrices((list) => list.filter((_, j) => j !== i))}
          >
            ✕
          </button>
        </div>
      ))}
      <button
        type="button"
        className="btn btn-ghost btn-sm"
        onClick={() => setRowPrices((list) => [...list, { rowLabel: '', price: defaultPrice }])}
      >
        + Sıra fiyatı ekle
      </button>

      <ErrorAlert error={create.error} />
      <button type="submit" className="btn btn-primary" disabled={create.isPending}>
        {create.isPending ? 'Oluşturuluyor...' : 'Etkinliği oluştur'}
      </button>
    </form>
  )
}
