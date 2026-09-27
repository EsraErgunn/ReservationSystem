import { useEffect, useState } from 'react'

/** FR-05: hedef zamana kalan milisaniye; saniyede bir güncellenir. */
export function useCountdown(targetIso: string | null | undefined): number {
  const target = targetIso ? Date.parse(targetIso) : null
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    if (target === null) return
    const id = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(id)
  }, [target])

  return target === null ? 0 : Math.max(0, target - now)
}
