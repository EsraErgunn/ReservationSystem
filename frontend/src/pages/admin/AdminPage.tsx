import { useState } from 'react'
import { EventsAdmin } from './EventsAdmin'
import { NewEventForm } from './NewEventForm'
import { NewVenueForm } from './NewVenueForm'

const tabs = [
  { id: 'events', label: 'Etkinlikler & satış' },
  { id: 'new-event', label: 'Yeni etkinlik' },
  { id: 'new-venue', label: 'Yeni mekân' },
] as const

type TabId = (typeof tabs)[number]['id']

/** FR-12 / FR-13. Erişim hem burada (RequireAuth admin) hem sunucuda (Admin politikası) kontrol ediliyor. */
export function AdminPage() {
  const [tab, setTab] = useState<TabId>('events')

  return (
    <>
      <h1>Yönetim</h1>
      <div className="tabs" role="tablist">
        {tabs.map((t) => (
          <button
            key={t.id}
            type="button"
            role="tab"
            aria-selected={tab === t.id}
            className={`tab ${tab === t.id ? 'tab-active' : ''}`}
            onClick={() => setTab(t.id)}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === 'events' && <EventsAdmin />}
      {tab === 'new-event' && <NewEventForm onCreated={() => setTab('events')} />}
      {tab === 'new-venue' && <NewVenueForm onCreated={() => setTab('new-event')} />}
    </>
  )
}
