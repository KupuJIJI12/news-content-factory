import { describe, expect, it } from 'vitest'
import { formatInCity, isPastInCity, toCityIso } from './cityTime'

// The browser is in another zone: results must still follow the city zone.
process.env.TZ = 'America/New_York'

describe('cityTime', () => {
  it('toCityIso interprets wall time in city zone', () => {
    expect(toCityIso('2026-10-27 09:00', 'Europe/Samara')).toBe('2026-10-27T09:00:00+04:00')
  })

  it('formatInCity shows Samara wall time for UTC instant', () => {
    expect(formatInCity('2026-10-26T21:30:00Z', 'Europe/Samara', 'DD.MM HH:mm')).toBe('27.10 01:30')
  })

  it('isPastInCity compares in city zone', () => {
    const now = Date.parse('2026-10-27T05:30:00Z') // 09:30 in Samara
    expect(isPastInCity('2026-10-27 09:00', 'Europe/Samara', now)).toBe(true)
    expect(isPastInCity('2026-10-27 10:00', 'Europe/Samara', now)).toBe(false)
  })
})
