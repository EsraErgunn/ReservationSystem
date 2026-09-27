import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { venuesApi } from '../../api/endpoints'
import type { VenueRowInput } from '../../api/types'
import { Alert, ErrorAlert } from '../../components/Alert'
import { rowLabel } from '../../utils/rows'

export function NewVenueForm({ onCreated }: { onCreated: () => void }) {
  const queryClient = useQueryClient()
  const [name, setName] = useState('')
  const [address, setAddress] = useState('')
  const [city, setCity] = useState('')
  const [rows, setRows] = useState<VenueRowInput[]>(() =>
    Array.from({ length: 5 }, (_, i) => ({ rowLabel: rowLabel(i), seatCount: 12 })),
  )

  const create = useMutation({
    mutationFn: () => venuesApi.create({ name, address, city, rows }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['venues'] })
      onCreated()
    },
  })

  const total = rows.reduce((sum, r) => sum + (Number.isFinite(r.seatCount) ? r.seatCount : 0), 0)

  const updateRow = (index: number, patch: Partial<VenueRowInput>) =>
    setRows((list) => list.map((r, i) => (i === index ? { ...r, ...patch } : r)))

  const submit = (e: FormEvent) => {
    e.preventDefault()
    create.mutate()
  }

  return (
    <form className="card form" onSubmit={submit}>
      <h2>Yeni mekân</h2>
      <div className="form-grid">
        <label>
          Mekân adı
          <input required maxLength={128} value={name} onChange={(e) => setName(e.target.value)} />
        </label>
        <label>
          Şehir
          <input required maxLength={64} value={city} onChange={(e) => setCity(e.target.value)} />
        </label>
        <label className="span-2">
          Adres
          <input required maxLength={512} value={address} onChange={(e) => setAddress(e.target.value)} />
        </label>
      </div>

      <h3>Koltuk düzeni</h3>
      <div className="row-editor">
        {rows.map((row, i) => (
          <div className="row-editor-item" key={i}>
            <input
              aria-label="Sıra etiketi"
              required
              maxLength={8}
              value={row.rowLabel}
              onChange={(e) => updateRow(i, { rowLabel: e.target.value.toUpperCase() })}
            />
            <input
              aria-label={`${row.rowLabel} sırasındaki koltuk sayısı`}
              type="number"
              min={1}
              max={200}
              required
              value={row.seatCount}
              onChange={(e) => updateRow(i, { seatCount: e.target.valueAsNumber })}
            />
            <button
              type="button"
              className="btn btn-ghost btn-sm"
              disabled={rows.length === 1}
              onClick={() => setRows((list) => list.filter((_, j) => j !== i))}
              aria-label={`${row.rowLabel} sırasını sil`}
            >
              ✕
            </button>
          </div>
        ))}
      </div>
      <button
        type="button"
        className="btn btn-ghost btn-sm"
        onClick={() =>
          setRows((list) => [...list, { rowLabel: rowLabel(list.length), seatCount: list.at(-1)?.seatCount ?? 10 }])
        }
      >
        + Sıra ekle
      </button>

      <Alert tone="info">
        {rows.length} sıra, toplam <strong>{total}</strong> koltuk.
      </Alert>
      <ErrorAlert error={create.error} />

      <button type="submit" className="btn btn-primary" disabled={create.isPending}>
        {create.isPending ? 'Kaydediliyor...' : 'Mekânı oluştur'}
      </button>
    </form>
  )
}
