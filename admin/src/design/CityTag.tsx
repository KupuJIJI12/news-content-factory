import { tokens } from './tokens'

/** Which city a story or source belongs to; shown on every row because more cities are coming. */
export function CityTag({ name }: { name: string }) {
  return (
    <span style={{
      fontSize: 12, lineHeight: '18px', border: `1px solid ${tokens.field}`, borderRadius: 4, padding: '0 6px',
      color: tokens.inkSoft, background: tokens.panel, whiteSpace: 'nowrap', alignSelf: 'flex-start', width: 'fit-content',
    }}>
      {name}
    </span>
  )
}
