import { Group, Text } from '@mantine/core'
import type { ChannelToday, Counters } from '../api/types'
import { APP_NAME } from '../design/Logo'
import { tokens } from '../design/tokens'

export const pageTitle = (onReview: number) => (onReview > 0 ? `(${onReview}) ${APP_NAME}` : APP_NAME)

/** One square per allowed post of the day: published, scheduled, still free. */
function Squares({ t, size }: { t: ChannelToday; size: number }) {
  const cells = Array.from({ length: Math.max(t.limit, t.published + t.scheduled) }, (_, i) =>
    i < t.published ? tokens.teal : i < t.published + t.scheduled ? tokens.amber : tokens.line)
  return (
    <span aria-hidden="true" style={{ display: 'flex', gap: 3 }}>
      {cells.map((c, i) => <span key={i} style={{ width: size, height: size, background: c, borderRadius: 2 }} />)}
    </span>
  )
}

const label = (t: ChannelToday) =>
  `${t.published} из ${t.limit}` + (t.scheduled > 0 ? `, ещё ${t.scheduled} в плане` : '')

/** «В канале сегодня»: the day's limit per channel; with several cities each gets its own row of squares. */
export function ChannelMeter({ counters, compact = false }: { counters: Counters | undefined; compact?: boolean }) {
  if (!counters || counters.today.length === 0) return null
  const many = counters.today.length > 1
  return (
    <Group gap={compact ? 10 : 18} wrap="nowrap">
      {!compact && !many && <Text size="sm">В канале сегодня</Text>}
      {counters.today.map((t) => (
        <Group key={t.channelId} gap={8} wrap="nowrap" aria-label={`${many ? t.cityName + ': ' : 'В канале сегодня '}${label(t)}`}>
          {many && <Text size="sm" c="ink.6">{t.cityName}</Text>}
          <Squares t={t} size={compact ? 10 : 14} />
          <Text className="nf-num" fw={600} size="sm" c={t.published + t.scheduled >= t.limit ? 'brick.6' : 'ink.8'}>
            {compact ? `${t.published}/${t.limit}` : `${t.published} из ${t.limit}`}
          </Text>
        </Group>
      ))}
    </Group>
  )
}

export function PausedBadge() {
  return (
    <span style={{ background: tokens.amberTint, color: tokens.amberInk, borderRadius: 4, padding: '3px 8px', fontSize: 13, whiteSpace: 'nowrap' }}>
      Сбор на паузе
    </span>
  )
}
