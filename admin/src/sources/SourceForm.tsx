import { useState } from 'react'
import { Button, Checkbox, Divider, Group, NumberInput, SegmentedControl, Stack, Text, TextInput } from '@mantine/core'
import { useQueryClient } from '@tanstack/react-query'
import { notifications } from '@mantine/notifications'
import { ApiError } from '../api/client'
import { fieldErrors } from '../api/settings'
import { createSource, sourcesKey, testSource, updateSource } from '../api/sources'
import type { HtmlSourceConfig, SourceDto, SourceInput, SourceTestResult as Result, SourceType } from '../api/types'
import { useCityTimeZone } from '../lib/useCityTimeZone'
import { SourceTestResult } from './SourceTestResult'

const HTML_FIELDS: { key: keyof HtmlSourceConfig; label: string; required?: boolean; hint?: string }[] = [
  { key: 'listUrl', label: 'Страница списка новостей', required: true },
  { key: 'itemSelector', label: 'Селектор элемента списка', required: true, hint: 'например .news-item' },
  { key: 'linkSelector', label: 'Селектор ссылки', required: true, hint: 'внутри элемента, например a.title' },
  { key: 'titleSelector', label: 'Селектор заголовка' },
  { key: 'dateSelector', label: 'Селектор даты' },
  { key: 'dateFormat', label: 'Формат даты', hint: 'например dd.MM.yyyy HH:mm' },
  { key: 'bodySelector', label: 'Селектор текста статьи' },
  { key: 'imageSelector', label: 'Селектор фото статьи' },
]

function parse<T>(json: string | null): Partial<T> {
  if (!json) return {}
  try { return JSON.parse(json) as Partial<T> } catch { return {} }
}

/** Builds the JSON config the backend expects; empty optional fields are left out. */
function buildConfig(type: SourceType, html: Partial<HtmlSourceConfig>, screenName: string): string | null {
  if (type === 'Rss') return null
  if (type === 'Vk') return JSON.stringify({ screenName: screenName.trim() })
  const out: Record<string, string> = {}
  for (const f of HTML_FIELDS) {
    const v = (html[f.key] ?? '').toString().trim()
    if (v) out[f.key] = v
  }
  return JSON.stringify(out)
}

export function SourceForm({ source, onSaved }: { source: SourceDto | null; onSaved: (s: SourceDto) => void }) {
  const tz = useCityTimeZone()
  const queryClient = useQueryClient()
  const [type, setType] = useState<SourceType>(source?.type ?? 'Rss')
  const [name, setName] = useState(source?.name ?? '')
  const [url, setUrl] = useState(source?.url ?? '')
  const [poll, setPoll] = useState<number>(source?.pollIntervalMinutes ?? 60)
  const [freq, setFreq] = useState<number | ''>(source?.expectedFrequencyHours ?? '')
  const [photosAllowed, setPhotosAllowed] = useState(source?.photosAllowedWithCredit ?? false)
  const [credit, setCredit] = useState(source?.photoCredit ?? '')
  const [enabled, setEnabled] = useState(source?.isEnabled ?? true)
  const [dedicated, setDedicated] = useState(source?.isDedicated ?? false)
  const [html, setHtml] = useState<Partial<HtmlSourceConfig>>(source?.type === 'Html' ? parse<HtmlSourceConfig>(source.config) : {})
  const [screenName, setScreenName] = useState(source?.type === 'Vk' ? (parse<{ screenName: string }>(source.config).screenName ?? '') : '')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState<'save' | 'test' | null>(null)
  const [result, setResult] = useState<Result | null>(null)

  const input = (): SourceInput => ({
    name: name.trim(), type, url: url.trim(), config: buildConfig(type, html, screenName), pollIntervalMinutes: poll,
    expectedFrequencyHours: freq === '' ? null : freq, photosAllowedWithCredit: photosAllowed,
    photoCredit: credit.trim() || null, isEnabled: enabled, isDedicated: dedicated,
  })

  function fail(err: unknown) {
    setErrors(fieldErrors(err))
    notifications.show({ color: 'red', message: err instanceof ApiError ? err.message : 'Сервер недоступен' })
  }

  async function save() {
    setBusy('save')
    setErrors({})
    try {
      const saved = source ? await updateSource(source.id, input()) : await createSource(input())
      notifications.show({ color: 'green', message: 'Источник сохранён' })
      await queryClient.invalidateQueries({ queryKey: sourcesKey })
      onSaved(saved)
    } catch (err) {
      fail(err)
    } finally {
      setBusy(null)
    }
  }

  async function test() {
    setBusy('test')
    setErrors({})
    setResult(null)
    try {
      setResult(await testSource(input()))
    } catch (err) {
      fail(err)
    } finally {
      setBusy(null)
    }
  }

  return (
    <Stack gap="sm">
      <SegmentedControl value={type} onChange={(v) => setType(v as SourceType)}
        data={[{ value: 'Rss', label: 'RSS' }, { value: 'Html', label: 'HTML' }, { value: 'Vk', label: 'VK' }]} />
      <TextInput label="Название" value={name} onChange={(e) => setName(e.currentTarget.value)} error={errors.name} />
      <TextInput label={type === 'Rss' ? 'Адрес RSS-ленты' : 'Адрес сайта'} value={url} onChange={(e) => setUrl(e.currentTarget.value)}
        error={errors.url} placeholder="https://…" />

      {type === 'Html' && (
        <Stack gap="xs">
          <Divider label="Селекторы страницы" labelPosition="left" />
          {HTML_FIELDS.map((f) => (
            <TextInput key={f.key} label={f.label} withAsterisk={f.required} description={f.hint} value={(html[f.key] ?? '') as string}
              onChange={(e) => { const v = e.currentTarget.value; setHtml((h) => ({ ...h, [f.key]: v })) }} />
          ))}
        </Stack>
      )}
      {type === 'Vk' && (
        <TextInput label="Короткое имя сообщества" description="например trkotradny" value={screenName}
          onChange={(e) => setScreenName(e.currentTarget.value)} />
      )}
      {errors.config && <Text size="sm" c="red">{errors.config}</Text>}

      <Group grow>
        <NumberInput label="Опрос, минут" min={15} max={1440} value={poll} onChange={(v) => setPoll(Number(v) || 60)}
          error={errors.pollIntervalMinutes} />
        <NumberInput label="Новости хотя бы раз в N часов" min={1} value={freq} onChange={(v) => setFreq(v === '' ? '' : Number(v))}
          error={errors.expectedFrequencyHours} description="пусто — не следить" />
      </Group>
      <Checkbox label="Фото можно брать с подписью" checked={photosAllowed} onChange={(e) => setPhotosAllowed(e.currentTarget.checked)} />
      {photosAllowed && <TextInput label="Подпись к фото" placeholder="Фото: …" value={credit} onChange={(e) => setCredit(e.currentTarget.value)} />}
      <Checkbox label="Источник только про наш город (без фильтра по ключевым словам)" checked={dedicated}
        onChange={(e) => setDedicated(e.currentTarget.checked)} />
      <Checkbox label="Включён" checked={enabled} onChange={(e) => setEnabled(e.currentTarget.checked)} />

      <Group justify="flex-end">
        <Button variant="default" loading={busy === 'test'} disabled={busy !== null} onClick={test}>Проверить</Button>
        <Button loading={busy === 'save'} disabled={busy !== null} onClick={save}>Сохранить</Button>
      </Group>
      {result && <SourceTestResult result={result} tz={tz} />}
    </Stack>
  )
}
