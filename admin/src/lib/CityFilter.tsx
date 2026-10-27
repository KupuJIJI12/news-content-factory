import { Select } from '@mantine/core'
import { useCities } from './useCities'

/** "Все города / <город>…" selector; renders nothing while there is a single city. */
export function CityFilter({ value, onChange }: { value: number | null; onChange: (cityId: number | null) => void }) {
  const { cities, multi } = useCities()
  if (!multi) return null
  return (
    <Select label="Город" w={220} allowDeselect={false} value={value === null ? 'all' : String(value)}
      data={[{ value: 'all', label: 'Все города' }, ...cities.map((c) => ({ value: String(c.id), label: c.name }))]}
      onChange={(v) => onChange(!v || v === 'all' ? null : Number(v))} />
  )
}
