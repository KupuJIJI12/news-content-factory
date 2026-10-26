import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Badge, Button, Checkbox, Group, NumberInput, Paper, Stack, Text, TextInput } from '@mantine/core'
import { getChannels, getSecrets, putChannel } from '../api/settings'
import type { ChannelSettings as Channel } from '../api/types'
import { useSave } from './useSave'

const key = ['settings', 'channels'] as const

function Configured({ label, ok }: { label: string; ok: boolean }) {
  return (
    <Group gap="xs">
      <Text size="sm">{label}:</Text>
      <Badge color={ok ? 'green' : 'red'} variant="light">{ok ? 'настроен' : 'не настроен'}</Badge>
    </Group>
  )
}

function ChannelForm({ channel }: { channel: Channel }) {
  const [chatId, setChatId] = useState(channel.externalChatId)
  const [limit, setLimit] = useState(channel.dailyLimit)
  const [enabled, setEnabled] = useState(channel.isEnabled)
  const { save, errors, busy } = useSave(key)
  useEffect(() => { setChatId(channel.externalChatId); setLimit(channel.dailyLimit); setEnabled(channel.isEnabled) }, [channel])

  return (
    <Paper withBorder p="sm">
      <Stack gap="sm">
        <Group gap="xs"><Text fw={600}>{channel.name}</Text><Badge variant="outline">{channel.platform}</Badge></Group>
        <TextInput label="ID чата/канала" value={chatId} onChange={(e) => setChatId(e.currentTarget.value)} error={errors.externalChatId} />
        <NumberInput label="Постов в день, максимум" min={1} max={20} value={limit} onChange={(v) => setLimit(Number(v) || 1)}
          error={errors.dailyLimit} />
        <Checkbox label="Публиковать в этот канал" checked={enabled} onChange={(e) => setEnabled(e.currentTarget.checked)} />
        <Group justify="flex-end">
          <Button loading={busy} onClick={() => save(() => putChannel(channel.id,
            { name: channel.name, externalChatId: chatId.trim(), dailyLimit: limit, isEnabled: enabled }))}>Сохранить</Button>
        </Group>
      </Stack>
    </Paper>
  )
}

export function ChannelSettings() {
  const { data } = useQuery({ queryKey: key, queryFn: getChannels })
  const { data: secrets } = useQuery({ queryKey: ['settings', 'secrets'], queryFn: getSecrets })
  return (
    <Stack gap="sm" maw={560}>
      {secrets && (
        <Paper withBorder p="sm">
          <Stack gap={4}>
            <Configured label="Токен бота Max" ok={secrets.maxBotToken} />
            <Configured label="Сервисный ключ VK" ok={secrets.vkServiceToken} />
            <Text size="xs" c="dimmed">Секреты задаются переменными окружения на сервере, в интерфейсе их не видно.</Text>
          </Stack>
        </Paper>
      )}
      {data?.map((c) => <ChannelForm key={c.id} channel={c} />)}
    </Stack>
  )
}
