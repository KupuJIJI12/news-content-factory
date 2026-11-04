import { tokens } from './tokens'

/** Interest 8–10 is dark, 6–7 amber, lower grey: the queue reads as a heat strip before the titles are read. */
export function scoreColor(score: number | null): string {
  if (score === null) return tokens.faint
  return score >= 8 ? tokens.ink : score >= 6 ? tokens.amber : tokens.faint
}

/** Vertical bar whose filled height is the interest score out of 10. */
export function ScoreRail({ score }: { score: number | null }) {
  const height = `${Math.max(0, Math.min(10, score ?? 0)) * 10}%`
  return (
    <span aria-hidden="true" style={{
      alignSelf: 'stretch', width: 6, minHeight: 28, display: 'flex', alignItems: 'flex-end', background: tokens.lineSoft, borderRadius: 2,
    }}>
      <span style={{ width: '100%', height, background: scoreColor(score), borderRadius: 2 }} />
    </span>
  )
}

/** The score itself, in the display face; dimmed when the story is of low interest. */
export function ScoreNumber({ score, size = 22 }: { score: number | null; size?: number }) {
  return (
    <span className="nf-num" aria-label={score === null ? 'Без оценки' : `Интерес ${score} из 10`}
      style={{ fontSize: size, fontWeight: 600, lineHeight: 1, color: score !== null && score >= 6 ? tokens.ink : tokens.muted, minWidth: size * 1.1 }}>
      {score ?? '—'}
    </span>
  )
}
