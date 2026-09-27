import { act, fireEvent, render, renderHook, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { SeatMapItem, SeatStatus } from '../api/types'
import { MAX_SEATS, useSeatSelection } from '../hooks/useSeatSelection'
import { SeatMap } from './SeatMap'

const seat = (row: string, n: number, status: SeatStatus = 'Available', price = 100): SeatMapItem => ({
  eventSeatId: `${row}${n}`,
  rowLabel: row,
  seatNumber: n,
  price,
  status,
})

describe('SeatMap', () => {
  const seats = [seat('B', 2), seat('A', 2, 'Sold'), seat('A', 1), seat('B', 1, 'Held')]

  it('sıraları ve koltukları sıralı çizer, dolu koltukları devre dışı bırakır', () => {
    render(<SeatMap seats={seats} selectedIds={new Set()} onToggle={() => {}} />)

    const buttons = screen.getAllByRole('button')
    expect(buttons.map((b) => b.getAttribute('title')?.split(' ')[0])).toEqual(['A-1', 'A-2', 'B-1', 'B-2'])
    expect(screen.getByTitle(/^A-2/)).toBeDisabled()
    expect(screen.getByTitle(/^B-1/)).toBeDisabled()
    expect(screen.getByTitle(/^A-1/)).toBeEnabled()
  })

  it('müsait koltuğa tıklanınca onToggle çağrılır', () => {
    const onToggle = vi.fn()
    render(<SeatMap seats={seats} selectedIds={new Set()} onToggle={onToggle} />)

    fireEvent.click(screen.getByTitle(/^A-1/))

    expect(onToggle).toHaveBeenCalledWith(expect.objectContaining({ eventSeatId: 'A1' }))
  })

  it('disabled iken hiçbir koltuk seçilemez', () => {
    render(<SeatMap seats={seats} selectedIds={new Set()} onToggle={() => {}} disabled />)
    screen.getAllByRole('button').forEach((b) => expect(b).toBeDisabled())
  })
})

describe('useSeatSelection', () => {
  it(`en fazla ${MAX_SEATS} koltuk seçilebilir (BR-03)`, () => {
    const seats = Array.from({ length: MAX_SEATS + 1 }, (_, i) => seat('A', i + 1))
    const { result } = renderHook(() => useSeatSelection(seats))

    for (const s of seats.slice(0, MAX_SEATS)) act(() => void result.current.toggle(s))

    let outcome: string | undefined
    act(() => void (outcome = result.current.toggle(seats[MAX_SEATS])))

    expect(outcome).toBe('limit')
    expect(result.current.selected).toHaveLength(MAX_SEATS)
    expect(result.current.total).toBe(MAX_SEATS * 100)
  })

  it('başkası tarafından alınan koltuk seçimden düşer (FR-09)', () => {
    let seats = [seat('A', 1), seat('A', 2)]
    const { result, rerender } = renderHook(() => useSeatSelection(seats))

    act(() => void result.current.toggle(seats[0]))
    act(() => void result.current.toggle(seats[1]))
    expect(result.current.selected).toHaveLength(2)

    seats = [seat('A', 1, 'Held'), seat('A', 2)]
    rerender()

    expect(result.current.selected.map((s) => s.eventSeatId)).toEqual(['A2'])
    expect(result.current.lostCount).toBe(1)
  })
})
