import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Badge, Button, Code, Group, Paper, SegmentedControl, Spoiler, Stack, Table, Text, Textarea } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { ApiError } from '../api/client'
import { activatePrompt, createPrompt, fieldErrors, getPrompts } from '../api/settings'
import type { LlmTask } from '../api/types'
import { formatInCity } from '../lib/cityTime'
import { useCityTimeZone } from '../lib/useCityTimeZone'

const TASK_LABEL: Record<LlmTask, string> = {
  Relevance: 'Релевантность', Clustering: 'Сюжеты', Scoring: 'Оценка', Rewrite: 'Рерайт', FactCheck: 'Факт-чек',
}
const promptsKey = ['settings', 'prompts'] as const

export function PromptSettings() {
  const tz = useCityTimeZone()
  const queryClient = useQueryClient()
  const { data, error } = useQuery({ queryKey: promptsKey, queryFn: getPrompts })
  const [task, setTask] = useState<LlmTask | null>(null)
  const [drafts, setDrafts] = useState<Partial<Record<LlmTask, string>>>({})
  const [textError, setTextError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  if (error) return <Alert color="red">Не удалось загрузить промпты</Alert>
  if (!data || data.length === 0) return null

  const current = data.find((p) => p.task === task) ?? data[0]
  const active = current.versions.find((v) => v.isActive)
  const text = drafts[current.task] ?? active?.text ?? ''

  async function save() {
    setBusy(true)
    setTextError(null)
    try {
      await createPrompt(current.task, text)
      notifications.show({ color: 'green', message: 'Новая версия сохранена и уже используется' })
      setDrafts((d) => ({ ...d, [current.task]: undefined }))
      await queryClient.invalidateQueries({ queryKey: promptsKey })
    } catch (err) {
      setTextError(fieldErrors(err).text ?? (err instanceof ApiError ? err.message : 'Сервер недоступен'))
    } finally {
      setBusy(false)
    }
  }

  async function activate(id: number) {
    try {
      await activatePrompt(id)
      notifications.show({ color: 'green', message: 'Версия сделана активной' })
      await queryClient.invalidateQueries({ queryKey: promptsKey })
    } catch (err) {
      notifications.show({ color: 'red', message: err instanceof ApiError ? err.message : 'Сервер недоступен' })
    }
  }

  return (
    <Stack gap="sm">
      <SegmentedControl value={current.task} onChange={(v) => { setTask(v as LlmTask); setTextError(null) }}
        data={data.map((p) => ({ value: p.task, label: TASK_LABEL[p.task] ?? p.task }))} />
      <Text size="sm">{current.description}</Text>
      <Paper withBorder p="xs">
        <Text size="xs" c="dimmed" mb={4}>
          Переменные в фигурных скобках система заменяет данными при каждом обращении к LLM. Обязательные переменные нельзя удалять из текста.
        </Text>
        <Table withRowBorders={false} verticalSpacing={2}>
          <Table.Tbody>
            {current.variables.map((v) => (
              <Table.Tr key={v.name}>
                <Table.Td w={120}><Code>{`{{${v.name}}}`}</Code></Table.Td>
                <Table.Td><Text size="xs">{v.description}</Text></Table.Td>
                <Table.Td w={110}><Text size="xs" c="dimmed">{v.required ? 'обязательная' : 'необязательная'}</Text></Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Paper>
      <Textarea label="Новая версия" autosize minRows={8} maxRows={24} value={text} error={textError}
        onChange={(e) => { const v = e.currentTarget.value; setDrafts((d) => ({ ...d, [current.task]: v })) }} />
      <Group justify="flex-end">
        <Button loading={busy} disabled={!text.trim()} onClick={save}>Сохранить как новую версию</Button>
      </Group>

      <Text fw={600} size="sm">Версии</Text>
      {current.versions.map((v) => (
        <Paper key={v.id} withBorder p="xs">
          <Group justify="space-between" wrap="nowrap">
            <Group gap="xs">
              <Text size="sm" fw={600}>{`Версия ${v.version}`}</Text>
              <Text size="xs" c="dimmed">{formatInCity(v.createdAt, tz, 'DD.MM.YYYY HH:mm')}</Text>
              {v.isActive && <Badge color="green" variant="light">активна</Badge>}
            </Group>
            <Group gap="xs" wrap="nowrap">
              <Button size="xs" variant="subtle" onClick={() => setDrafts((d) => ({ ...d, [current.task]: v.text }))}>В редактор</Button>
              {!v.isActive && <Button size="xs" variant="light" onClick={() => activate(v.id)}>Сделать активной</Button>}
            </Group>
          </Group>
          <Spoiler maxHeight={60} showLabel="Показать" hideLabel="Свернуть">
            <Text size="xs" style={{ whiteSpace: 'pre-wrap' }}>{v.text}</Text>
          </Spoiler>
        </Paper>
      ))}
    </Stack>
  )
}
