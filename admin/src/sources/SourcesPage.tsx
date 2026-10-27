import { CityFilter } from '../lib/CityFilter'
import { useCities } from '../lib/useCities'
import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useMediaQuery } from '@mantine/hooks'
import { Alert, Badge, Button, Center, Drawer, Group, Loader, Paper, Stack, Table, Text, Title } from '@mantine/core'
import { listSources, sourcesKey } from '../api/sources'
import type { SourceDto } from '../api/types'
import { formatInCity } from '../lib/cityTime'
import { useCityTimeZone } from '../lib/useCityTimeZone'
import { SourceForm } from './SourceForm'

const TYPE_LABEL = { Rss: 'RSS', Html: 'HTML', Vk: 'VK' } as const

function Health({ s }: { s: SourceDto }) {
  if (!s.isEnabled) return <Badge color="gray" variant="light">выключен</Badge>
  if (s.health === 'Failing') return <Badge color="red" variant="light">{`Ошибки подряд: ${s.consecutiveFailures}`}</Badge>
  if (s.health === 'Silent') return <Badge color="yellow" variant="light">Нет новостей дольше ожидаемого</Badge>
  return <Badge color="green" variant="light">в порядке</Badge>
}

export function SourcesPage() {
  const tz = useCityTimeZone()
  const mobile = useMediaQuery('(max-width: 48em)')
  const [cityId, setCityId] = useState<number | null>(null)
  const { multi } = useCities()
  const { data, isLoading, error } = useQuery({ queryKey: [...sourcesKey, cityId], queryFn: () => listSources(cityId) })
  const cityBadges = (s: SourceDto) => multi && s.cities.map((c) => (
    <Badge key={c.cityId} size="xs" variant="outline" color="gray">{c.cityName}</Badge>
  ))
  const [editing, setEditing] = useState<SourceDto | null | 'new'>(null)
  const when = (iso: string | null) => (iso ? formatInCity(iso, tz) : '—')

  return (
    <Stack gap="sm">
      <Group justify="space-between">
        <Group align="flex-end" gap="md">
          <Title order={3}>Источники</Title>
          <CityFilter value={cityId} onChange={setCityId} />
        </Group>
        <Button onClick={() => setEditing('new')}>Добавить</Button>
      </Group>
      {isLoading && <Center h={120}><Loader /></Center>}
      {error && <Alert color="red">Не удалось загрузить источники</Alert>}

      {data && (mobile ? (
        <Stack gap="xs">
          {data.map((s) => (
            <Paper key={s.id} withBorder p="sm" onClick={() => setEditing(s)} style={{ cursor: 'pointer' }}>
              <Group justify="space-between" wrap="nowrap">
                <Text fw={600}>{s.name}</Text>
                <Group gap={4} wrap="nowrap">{cityBadges(s)}<Badge variant="outline">{TYPE_LABEL[s.type]}</Badge></Group>
              </Group>
              <Group gap="xs" mt={4}><Health s={s} /><Text size="xs" c="dimmed">{`запуск ${when(s.lastRunAt)}`}</Text></Group>
              {s.lastError && <Text size="xs" c="red" lineClamp={2} mt={4}>{s.lastError}</Text>}
            </Paper>
          ))}
        </Stack>
      ) : (
        <Table highlightOnHover verticalSpacing="xs">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Название</Table.Th><Table.Th>Тип</Table.Th><Table.Th>Состояние</Table.Th>
              <Table.Th>Последний запуск</Table.Th><Table.Th>Последняя новость</Table.Th><Table.Th>Последняя ошибка</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {data.map((s) => (
              <Table.Tr key={s.id} onClick={() => setEditing(s)} style={{ cursor: 'pointer' }}>
                <Table.Td><Group gap={6}>{s.name}{cityBadges(s)}</Group></Table.Td>
                <Table.Td>{TYPE_LABEL[s.type]}</Table.Td>
                <Table.Td><Health s={s} /></Table.Td>
                <Table.Td>{when(s.lastRunAt)}</Table.Td>
                <Table.Td>{when(s.lastItemAt)}</Table.Td>
                <Table.Td><Text size="xs" c="red" lineClamp={2} maw={320}>{s.lastError ?? ''}</Text></Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      ))}

      <Drawer opened={editing !== null} onClose={() => setEditing(null)} position="right" size={mobile ? '100%' : 'lg'}
        title={editing === 'new' ? 'Новый источник' : editing ? editing.name : ''}>
        {editing !== null && (
          <SourceForm key={editing === 'new' ? 'new' : editing.id} source={editing === 'new' ? null : editing} onSaved={() => setEditing(null)} />
        )}
      </Drawer>
    </Stack>
  )
}
