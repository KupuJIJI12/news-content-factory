import { tokens } from './tokens'

/** «Контент завод»: a saw-tooth workshop with a chimney; the lit windows use the "on review" amber. */
export function LogoMark({ size = 24 }: { size?: number }) {
  return (
    <svg width={size * 1.25} height={size} viewBox="0 0 30 24" aria-hidden="true">
      <path d="M1 23V12l7-6v6l7-6v6l7-6v17z" fill={tokens.ink} />
      <rect x="24" y="1" width="5" height="22" fill={tokens.ink} />
      <rect x="4" y="16" width="3" height="3" fill={tokens.amber} />
      <rect x="11" y="16" width="3" height="3" fill={tokens.amber} />
      <rect x="18" y="16" width="3" height="3" fill={tokens.amber} />
    </svg>
  )
}

export function Logo({ size = 18 }: { size?: number }) {
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 10, color: tokens.ink }}>
      <LogoMark size={Math.round(size * 1.33)} />
      <span style={{ fontFamily: tokens.display, fontWeight: 700, fontSize: size, letterSpacing: '-0.02em', whiteSpace: 'nowrap' }}>
        Контент завод
      </span>
    </span>
  )
}

export const APP_NAME = 'Контент завод'
