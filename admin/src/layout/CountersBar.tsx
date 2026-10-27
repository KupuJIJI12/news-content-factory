import { ActionIcon, Badge, Group, Indicator, Menu, Text } from '@mantine/core'
import { IconInbox } from '@tabler/icons-react'
import { useNavigate } from 'react-router'
import type { Counters } from '../api/types'

export const pageTitle = (onReview: number) => (onReview > 0 ? `(${onReview}) NewsFactory` : 'NewsFactory')

function todayText(c: Counters): string | null {
  const t = c.today[0]
  if (!t) return null
  return `Сегодня ${t.published}/${t.limit}` + (t.scheduled > 0 ? ` (+${t.scheduled})` : '')
}

export function CountersBar({ counters, compact }: { counters: Counters | undefined; compact: boolean }) {
  const navigate = useNavigate()
  if (!counters) return null
  const today = todayText(counters)
  const items = [
    { label: `На проверке ${counters.onReview}`, color: 'blue', to: '/?tab=Review' },
    { label: `Обновления ${counters.withUpdates}`, color: 'grape', to: '/?tab=Review' },
    { label: `Ошибки ${counters.errors.total}`, color: counters.errors.total > 0 ? 'red' : 'gray', to: '/?tab=Errors' },
  ]
  const full = counters.today[0] && counters.today[0].published + counters.today[0].scheduled >= counters.today[0].limit

  if (compact) {
    return (
      <Menu position="bottom-end" withinPortal>
        <Menu.Target>
          <Indicator label={counters.onReview} size={18} disabled={counters.onReview === 0}>
            <ActionIcon variant="light" size="lg" aria-label={`На проверке: ${counters.onReview}`}>
              <IconInbox size={20} />
            </ActionIcon>
          </Indicator>
        </Menu.Target>
        <Menu.Dropdown>
          {items.map((i) => (
            <Menu.Item key={i.label} onClick={() => navigate(i.to)}>{i.label}</Menu.Item>
          ))}
          {today && <Menu.Item disabled>{today}</Menu.Item>}
          {counters.collectionPaused && <Menu.Item disabled c="orange">Сбор на паузе</Menu.Item>}
        </Menu.Dropdown>
      </Menu>
    )
  }

  return (
    <Group gap="xs" wrap="nowrap">
      {counters.collectionPaused && <Badge color="orange" variant="filled" size="lg">Сбор на паузе</Badge>}
      {items.map((i) => (
        <Badge key={i.label} color={i.color} variant="light" size="lg" style={{ cursor: 'pointer' }} onClick={() => navigate(i.to)}>
          {i.label}
        </Badge>
      ))}
      {today && <Text size="sm" c={full ? 'orange' : 'dimmed'} fw={500}>{today}</Text>}
    </Group>
  )
}
