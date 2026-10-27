import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Badge, Button, Checkbox, Divider, Drawer, Group, NumberInput, Paper, Select, SimpleGrid, Stack, TagsInput, Text, TextInput, Title } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { citiesKey, createCity, listCities, putCityChannel, updateCity } from '../api/cities'
import { getSecrets } from '../api/settings'
import type { City } from '../api/types'
import { useSave } from './useSave'

const ZONES = [
  ['Europe/Kaliningrad', 'Калининград (UTC+2)'], ['Europe/Moscow', 'Москва (UTC+3)'], ['Europe/Samara', 'Самара (UTC+4)'],
  ['Asia/Yekaterinburg', 'Екатеринбург (UTC+5)'], ['Asia/Omsk', 'Омск (UTC+6)'], ['Asia/Novosibirsk', 'Новосибирск (UTC+7)'],
  ['Asia/Krasnoyarsk', 'Красноярск (UTC+7)'], ['Asia/Irkutsk', 'Иркутск (UTC+8)'], ['Asia/Yakutsk', 'Якутск (UTC+9)'],
  ['Asia/Vladivostok', 'Владивосток (UTC+10)'], ['Asia/Magadan', 'Магадан (UTC+11)'], ['Asia/Kamchatka', 'Камчатка (UTC+12)'],
].map(([value, label]) => ({ value, label }))

function CityForm({ city, onSaved }: { city: City | null; onSaved: (c: City) => void }) {
  const [name, setName] = useState(city?.name ?? '')
  const [timeZone, setTimeZone] = useState(city?.timeZone ?? 'Europe/Samara')
  const [keywords, setKeywords] = useState<string[]>(city?.keywords ?? [])
  const [threshold, setThreshold] = useState(city?.scoreThreshold ?? 6)
  const { save, errors, busy } = useSave(citiesKey)
  const zones = ZONES.some((z) => z.value === timeZone) ? ZONES : [...ZONES, { value: timeZone, label: timeZone }]

  return (
    <Stack gap="sm">
      <TextInput label="Название города" value={name} onChange={(e) => setName(e.currentTarget.value)} error={errors.name} />
      <Select label="Часовой пояс" data={zones} value={timeZone} allowDeselect={false} error={errors.timeZone}
        description="В нём показывается время публикаций и считается дневной лимит"
        onChange={(v) => v && setTimeZone(v)} />
      <TagsInput label="Ключевые слова" value={keywords} onChange={setKeywords} error={errors.keywords}
        description="Начало слова в нижнем регистре, Enter — добавить. Из областных источников берутся только новости, где встречается одно из слов (например «отрадн» найдёт «Отрадный», «в Отрадном»)." />
      <NumberInput label="Порог интереса" min={1} max={10} value={threshold} onChange={(v) => setThreshold(Number(v) || 1)}
        error={errors.scoreThreshold}
        description="Сюжеты с оценкой ниже порога уходят во вкладку «Низкий интерес» без рерайта" />
      <Group justify="flex-end">
        <Button loading={busy} onClick={async () => {
          const input = { name: name.trim(), timeZone, keywords, scoreThreshold: threshold }
          let saved: City | null = null
          if (await save(async () => { saved = city ? await updateCity(city.id, input) : await createCity(input) }, 'Город сохранён') && saved)
            onSaved(saved)
        }}>Сохранить город</Button>
      </Group>
    </Stack>
  )
}

function ChannelForm({ city }: { city: City }) {
  const channel = city.channel
  const [chatId, setChatId] = useState(channel?.externalChatId ?? '')
  const [limit, setLimit] = useState(channel?.dailyLimit ?? 4)
  const [enabled, setEnabled] = useState(channel?.isEnabled ?? false)
  const { save, errors, busy } = useSave(citiesKey)
  const { data: secrets } = useQuery({ queryKey: ['settings', 'secrets'], queryFn: getSecrets })
  useEffect(() => {
    setChatId(channel?.externalChatId ?? ''); setLimit(channel?.dailyLimit ?? 4); setEnabled(channel?.isEnabled ?? false)
  }, [channel])

  return (
    <Stack gap="sm">
      <Group gap="xs">
        <Title order={6}>Канал Max</Title>
        <Badge variant="light" color={channel?.isEnabled ? 'green' : 'gray'}>{channel?.isEnabled ? 'Канал включён' : 'Канал выключен'}</Badge>
      </Group>
      <TextInput label="ID чата канала" value={chatId} onChange={(e) => setChatId(e.currentTarget.value)} error={errors.externalChatId}
        description="Числовой ID канала Max, куда бот публикует посты (бот должен быть администратором канала)" />
      <NumberInput label="Постов в день, максимум" min={1} max={20} value={limit} onChange={(v) => setLimit(Number(v) || 1)} error={errors.dailyLimit} />
      <Checkbox label="Публиковать в этот канал" checked={enabled} onChange={(e) => setEnabled(e.currentTarget.checked)} />
      {secrets && (
        <Text size="xs" c={secrets.maxBotToken ? 'dimmed' : 'red'}>
          {secrets.maxBotToken ? 'Токен бота Max задан на сервере' : 'Токен бота Max не задан: укажите MAX_BOT_TOKEN в переменных окружения сервера'}
        </Text>
      )}
      <Group justify="flex-end">
        <Button variant="light" loading={busy} onClick={() => save(() => putCityChannel(city.id,
          { name: channel?.name ?? `${city.name} — Max`, externalChatId: chatId.trim(), dailyLimit: limit, isEnabled: enabled }), 'Канал сохранён')}>
          Сохранить канал
        </Button>
      </Group>
    </Stack>
  )
}

export function CitiesSettings() {
  const { data } = useQuery({ queryKey: citiesKey, queryFn: listCities })
  const mobile = useMediaQuery('(max-width: 48em)')
  const [editing, setEditing] = useState<number | 'new' | null>(null)
  const city = typeof editing === 'number' ? data?.find((c) => c.id === editing) ?? null : null

  return (
    <Stack gap="sm">
      <Group justify="space-between">
        <Text size="sm" c="dimmed">У каждого города свои источники, ключевые слова, порог интереса и канал для публикации.</Text>
        <Button onClick={() => setEditing('new')}>Добавить город</Button>
      </Group>
      <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }}>
        {data?.map((c) => (
          <Paper key={c.id} withBorder p="sm" onClick={() => setEditing(c.id)} style={{ cursor: 'pointer' }}>
            <Group justify="space-between" wrap="nowrap">
              <Text fw={600}>{c.name}</Text>
              <Badge variant="light" color={c.channel?.isEnabled ? 'green' : 'gray'}>
                {c.channel?.isEnabled ? `Max · ${c.channel.dailyLimit} в день` : 'канал не настроен'}
              </Badge>
            </Group>
            <Text size="xs" c="dimmed" mt={4}>
              {`Источников: ${c.sourcesCount} · порог ${c.scoreThreshold} · слова: ${c.keywords.join(', ')}`}
            </Text>
          </Paper>
        ))}
      </SimpleGrid>

      <Drawer opened={editing !== null} onClose={() => setEditing(null)} position="right" size={mobile ? '100%' : 'md'}
        title={editing === 'new' ? 'Новый город' : city?.name}>
        {editing === 'new' && <CityForm key="new" city={null} onSaved={(c) => setEditing(c.id)} />}
        {city && (
          <Stack gap="lg">
            <CityForm key={city.id} city={city} onSaved={() => {}} />
            <Divider />
            <ChannelForm city={city} />
          </Stack>
        )}
      </Drawer>
    </Stack>
  )
}
