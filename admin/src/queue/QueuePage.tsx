import { Badge, Card, Center, Group, Loader, Pagination, Stack, Table, Tabs, Text, Tooltip } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { IconBell } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useNavigate, useSearchParams } from 'react-router'
import { listStories, PAGE_SIZE, storiesKey, storyCounts, storyCountsKey } from '../api/stories'
import type { QueueTab, StoryListItem } from '../api/types'
import { formatInCity } from '../lib/cityTime'
import { plural } from '../lib/format'
import { useCityTimeZone } from '../lib/useCityTimeZone'
import { ScoreBadge } from './ScoreBadge'

const TABS: { value: QueueTab; label: string }[] = [
  { value: 'Review', label: 'На проверке' },
  { value: 'LowInterest', label: 'Низкий интерес' },
  { value: 'Scheduled', label: 'Запланировано' },
  { value: 'Published', label: 'Опубликовано' },
  { value: 'Rejected', label: 'Отклонено' },
  { value: 'Errors', label: 'Ошибки' },
]

const isTab = (v: string | null): v is QueueTab => TABS.some((t) => t.value === v)

export function QueuePage() {
  const [params, setParams] = useSearchParams()
  const tab: QueueTab = isTab(params.get('tab')) ? (params.get('tab') as QueueTab) : 'Review'
  const page = Math.max(1, Number(params.get('page') ?? 1) || 1)
  const isMobile = useMediaQuery('(max-width: 48em)') ?? false
  const tz = useCityTimeZone()
  const navigate = useNavigate()
  const query = useQuery({
    queryKey: storiesKey(tab, page),
    queryFn: () => listStories(tab, (page - 1) * PAGE_SIZE, PAGE_SIZE),
  })

  const counts = useQuery({ queryKey: storyCountsKey(), queryFn: () => storyCounts() })

  const open = (s: StoryListItem) => navigate(`/stories/${s.id}`)
  const when = (s: StoryListItem) =>
    formatInCity(tab === 'Scheduled' && s.scheduledAt ? s.scheduledAt : s.updatedAt, tz)
  const sources = (n: number) => `${n} ${plural(n, 'источник', 'источника', 'источников')}`
  const updates = (s: StoryListItem) => s.hasUpdates && (
    <Tooltip label="Появились новые подробности">
      <IconBell size={16} color="var(--mantine-color-grape-6)" aria-label="Есть обновления" />
    </Tooltip>
  )

  return (
    <Stack>
      <Tabs value={tab} onChange={(v) => setParams(v ? { tab: v } : {})} keepMounted={false}>
        <Tabs.List style={{ overflowX: 'auto', flexWrap: 'nowrap' }}>
          {TABS.map((t) => (
            <Tabs.Tab key={t.value} value={t.value}
              rightSection={counts.data && (
                <Badge size="sm" variant={t.value === tab ? 'filled' : 'light'}
                  color={t.value === 'Errors' && counts.data.Errors > 0 ? 'red' : t.value === 'Review' ? 'blue' : 'gray'}>
                  {counts.data[t.value]}
                </Badge>
              )}>
              {t.label}
            </Tabs.Tab>
          ))}
        </Tabs.List>
      </Tabs>

      {query.isPending ? (
        <Center p="xl"><Loader /></Center>
      ) : query.isError ? (
        <Text c="red">Не удалось загрузить очередь: {query.error.message}</Text>
      ) : query.data.items.length === 0 ? (
        <Center p="xl"><Text c="dimmed">Здесь пока пусто</Text></Center>
      ) : isMobile ? (
        <Stack gap="xs">
          {query.data.items.map((s) => (
            <Card key={s.id} withBorder padding="sm" onClick={() => open(s)} style={{ cursor: 'pointer' }}>
              <Group justify="space-between" wrap="nowrap" align="flex-start">
                <Text fw={500}>{s.title}</Text>
                <ScoreBadge score={s.score} />
              </Group>
              <Group gap="xs" mt={4}>
                <Text size="xs" c="dimmed">{sources(s.sourcesCount)} · {when(s)}</Text>
                {updates(s)}
              </Group>
            </Card>
          ))}
        </Stack>
      ) : (
        <Table highlightOnHover verticalSpacing="sm">
          <Table.Thead>
            <Table.Tr>
              <Table.Th w={70}>Оценка</Table.Th>
              <Table.Th>Заголовок</Table.Th>
              <Table.Th w={130}>Источники</Table.Th>
              <Table.Th w={130}>{tab === 'Scheduled' ? 'Публикация' : 'Обновлено'}</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {query.data.items.map((s) => (
              <Table.Tr key={s.id} onClick={() => open(s)} style={{ cursor: 'pointer' }}>
                <Table.Td><ScoreBadge score={s.score} /></Table.Td>
                <Table.Td><Group gap="xs" wrap="nowrap"><Text>{s.title}</Text>{updates(s)}</Group></Table.Td>
                <Table.Td><Text size="sm" c="dimmed">{sources(s.sourcesCount)}</Text></Table.Td>
                <Table.Td><Text size="sm">{when(s)}</Text></Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}

      {query.data && query.data.total > PAGE_SIZE && (
        <Pagination total={Math.ceil(query.data.total / PAGE_SIZE)} value={page}
          onChange={(p) => setParams({ tab, page: String(p) })} />
      )}
    </Stack>
  )
}
