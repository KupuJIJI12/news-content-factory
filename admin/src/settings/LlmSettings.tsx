import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Badge, Button, Checkbox, Drawer, Group, NumberInput, Paper, SegmentedControl, Select, Stack, Table, TagsInput, Text, TextInput, Title } from '@mantine/core'
import { createProvider, getLlmTasks, getProviders, putLlmTask, updateProvider } from '../api/settings'
import type { LlmProvider, LlmProviderInput, LlmProviderKind, LlmTaskSetting } from '../api/types'
import { useSave } from './useSave'

const providersKey = ['settings', 'providers'] as const
const tasksKey = ['settings', 'llmTasks'] as const
const TASK_LABEL: Record<string, string> = { Relevance: 'Релевантность', Clustering: 'Сюжеты', Scoring: 'Оценка', Rewrite: 'Рерайт', FactCheck: 'Факт-чек', SourceSetup: 'Автонастройка' }

const empty: LlmProviderInput = {
  name: '', kind: 'OpenAiCompatible', baseUrl: 'https://', apiKeyEnvVar: '', models: [], headers: {},
  supportsJsonSchema: false, authUrl: null, authScope: null,
}

function ProviderForm({ provider, onDone }: { provider: LlmProvider | null; onDone: () => void }) {
  const [v, setV] = useState<LlmProviderInput>(() => provider ? { ...provider } : empty)
  const { save, errors, busy } = useSave(providersKey)
  const set = <K extends keyof LlmProviderInput>(k: K, value: LlmProviderInput[K]) => setV((s) => ({ ...s, [k]: value }))
  return (
    <Stack gap="sm">
      <SegmentedControl value={v.kind} onChange={(k) => set('kind', k as LlmProviderKind)}
        data={[{ value: 'OpenAiCompatible', label: 'OpenAI-совместимый' }, { value: 'GigaChat', label: 'GigaChat' }]} />
      <TextInput label="Название" value={v.name} onChange={(e) => set('name', e.currentTarget.value)} error={errors.name} />
      <TextInput label="Базовый адрес API" value={v.baseUrl} onChange={(e) => set('baseUrl', e.currentTarget.value)} error={errors.baseUrl} />
      <TextInput label="Переменная окружения с ключом" description="сам ключ хранится только на сервере" value={v.apiKeyEnvVar}
        onChange={(e) => set('apiKeyEnvVar', e.currentTarget.value)} error={errors.apiKeyEnvVar} />
      <TagsInput label="Модели" value={v.models} onChange={(m) => set('models', m)} error={errors.models} />
      {v.kind === 'GigaChat' && (
        <>
          <TextInput label="Адрес OAuth" value={v.authUrl ?? ''} onChange={(e) => set('authUrl', e.currentTarget.value || null)} error={errors.authUrl} />
          <TextInput label="Scope" value={v.authScope ?? ''} onChange={(e) => set('authScope', e.currentTarget.value || null)} />
        </>
      )}
      <Checkbox label="Поддерживает JSON Schema" checked={v.supportsJsonSchema} onChange={(e) => set('supportsJsonSchema', e.currentTarget.checked)} />
      <Group justify="flex-end">
        <Button loading={busy} onClick={async () => {
          if (await save(() => (provider ? updateProvider(provider.id, v) : createProvider(v)))) onDone()
        }}>Сохранить</Button>
      </Group>
    </Stack>
  )
}

function TaskRow({ t, providers }: { t: LlmTaskSetting; providers: LlmProvider[] }) {
  const [providerId, setProviderId] = useState(String(t.providerId))
  const [model, setModel] = useState<string | null>(t.model)
  const [temperature, setTemperature] = useState(t.temperature)
  const { save, errors, busy } = useSave(tasksKey)
  useEffect(() => { setProviderId(String(t.providerId)); setModel(t.model); setTemperature(t.temperature) }, [t])
  const models = providers.find((p) => String(p.id) === providerId)?.models ?? []
  return (
    <Table.Tr>
      <Table.Td>{TASK_LABEL[t.task] ?? t.task}</Table.Td>
      <Table.Td><Select aria-label="Провайдер" value={providerId} allowDeselect={false} error={errors.providerId}
        data={providers.map((p) => ({ value: String(p.id), label: p.name }))}
        onChange={(v) => { if (v) { setProviderId(v); setModel(null) } }} /></Table.Td>
      <Table.Td><Select aria-label="Модель" value={model} data={models} onChange={setModel} error={errors.model} /></Table.Td>
      <Table.Td><NumberInput aria-label="Температура" w={90} min={0} max={1} step={0.1} decimalScale={2} value={temperature}
        onChange={(v) => setTemperature(Number(v) || 0)} error={errors.temperature} /></Table.Td>
      <Table.Td><Button size="xs" loading={busy} disabled={!model}
        onClick={() => save(() => putLlmTask(t.task, { providerId: Number(providerId), model: model!, temperature }))}>Сохранить</Button></Table.Td>
    </Table.Tr>
  )
}

export function LlmSettings() {
  const { data: providers } = useQuery({ queryKey: providersKey, queryFn: getProviders })
  const { data: tasks } = useQuery({ queryKey: tasksKey, queryFn: getLlmTasks })
  const [editing, setEditing] = useState<LlmProvider | null | 'new'>(null)

  return (
    <Stack gap="md">
      <Group justify="space-between"><Title order={5}>Провайдеры</Title><Button size="xs" onClick={() => setEditing('new')}>Добавить</Button></Group>
      {providers?.map((p) => (
        <Paper key={p.id} withBorder p="sm" onClick={() => setEditing(p)} style={{ cursor: 'pointer' }}>
          <Group justify="space-between" wrap="nowrap">
            <Stack gap={0}>
              <Text fw={600}>{p.name}</Text>
              <Text size="xs" c="dimmed">{`${p.baseUrl} · ${p.models.join(', ')}`}</Text>
            </Stack>
            <Badge color={p.apiKeyConfigured ? 'green' : 'red'} variant="light">{p.apiKeyConfigured ? 'ключ: настроен' : 'ключ: не настроен'}</Badge>
          </Group>
        </Paper>
      ))}

      <Title order={5}>Задачи</Title>
      {tasks && providers && (
        <Table.ScrollContainer minWidth={560}>
          <Table verticalSpacing="xs">
            <Table.Thead><Table.Tr><Table.Th>Задача</Table.Th><Table.Th>Провайдер</Table.Th><Table.Th>Модель</Table.Th><Table.Th>Темп.</Table.Th><Table.Th /></Table.Tr></Table.Thead>
            <Table.Tbody>{tasks.map((t) => <TaskRow key={t.task} t={t} providers={providers} />)}</Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}

      <Drawer opened={editing !== null} onClose={() => setEditing(null)} position="right" title={editing === 'new' ? 'Новый провайдер' : 'Провайдер'}>
        {editing !== null && <ProviderForm key={editing === 'new' ? 'new' : editing.id} provider={editing === 'new' ? null : editing} onDone={() => setEditing(null)} />}
      </Drawer>
    </Stack>
  )
}
