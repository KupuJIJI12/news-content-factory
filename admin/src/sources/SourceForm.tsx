import { useEffect, useState } from 'react'
import { Alert, Button, Checkbox, Collapse, Divider, Group, NumberInput, Paper, SegmentedControl, Stack, Switch, Text, TextInput } from '@mantine/core'
import { useQueryClient } from '@tanstack/react-query'
import { notifications } from '@mantine/notifications'
import { ApiError } from '../api/client'
import { fieldErrors } from '../api/settings'
import { autodetectSource, createSource, sourcesKey, testSource, updateSource } from '../api/sources'
import type { AutoSetupResult, HtmlSourceConfig, SourceDto, SourceInput, SourceTestResult as Result, SourceType } from '../api/types'
import { useCities } from '../lib/useCities'
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

const METHOD_LABEL = { Rss: 'Найдена RSS-лента', Heuristic: 'Определено по структуре страницы', Llm: 'Подобрано LLM' } as const

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
  const { cities, loaded } = useCities()
  // cityId → "every item is about this city" for the cities the source is linked to
  const [links, setLinks] = useState<Record<number, boolean>>(
    () => Object.fromEntries((source?.cities ?? []).map((c) => [c.cityId, c.isDedicated])))
  useEffect(() => {
    // A new source in a single-city setup belongs to that city.
    if (!source && loaded && cities.length === 1) setLinks((l) => (Object.keys(l).length === 0 ? { [cities[0].id]: false } : l))
  }, [source, loaded, cities])
  const [html, setHtml] = useState<Partial<HtmlSourceConfig>>(source?.type === 'Html' ? parse<HtmlSourceConfig>(source.config) : {})
  const [screenName, setScreenName] = useState(source?.type === 'Vk' ? (parse<{ screenName: string }>(source.config).screenName ?? '') : '')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState<'save' | 'test' | 'auto' | null>(null)
  const [result, setResult] = useState<Result | null>(null)
  const [siteUrl, setSiteUrl] = useState('')
  const [auto, setAuto] = useState<AutoSetupResult | null>(null)
  const [manual, setManual] = useState(source?.type === 'Html')

  const input = (): SourceInput => ({
    name: name.trim(), type, url: url.trim(), config: buildConfig(type, html, screenName), pollIntervalMinutes: poll,
    expectedFrequencyHours: freq === '' ? null : freq, photosAllowedWithCredit: photosAllowed,
    photoCredit: credit.trim() || null, isEnabled: enabled,
    cities: cities.filter((c) => c.id in links).map((c) => ({ cityId: c.id, isDedicated: links[c.id] })),
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

  async function autodetect() {
    setBusy('auto')
    setErrors({})
    setAuto(null)
    setResult(null)
    try {
      const r = await autodetectSource(siteUrl.trim())
      setAuto(r)
      if (r.ok && r.type && r.url) {
        setType(r.type)
        setUrl(r.url)
        if (r.type === 'Html') setHtml(parse<HtmlSourceConfig>(r.config))
        if (!name.trim()) setName(new URL(r.url).hostname.replace(/^www\./, ''))
        setResult({ ok: true, error: null, items: r.items })
      } else {
        // Nothing found automatically: continue by hand from the same address.
        setType('Html')
        setUrl((u) => u || siteUrl.trim())
        setHtml((h) => ({ ...h, listUrl: h.listUrl || siteUrl.trim() }))
        setManual(true)
      }
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
      <Paper withBorder p="sm" bg="var(--mantine-color-default-hover)">
        <Stack gap={6}>
          <Text size="sm" fw={500}>Автонастройка</Text>
          <Text size="xs" c="dimmed">
            Укажите страницу сайта со списком новостей. Система поищет RSS-ленту, затем определит список по структуре
            страницы, а если не выйдет — попросит LLM подобрать селекторы. Найденные новости появятся ниже для проверки.
          </Text>
          <Group align="flex-end" gap="xs" wrap="nowrap">
            <TextInput style={{ flex: 1 }} label="Сайт для автонастройки" placeholder="https://…" value={siteUrl}
              onChange={(e) => setSiteUrl(e.currentTarget.value)} />
            <Button variant="light" loading={busy === 'auto'} disabled={busy !== null || !siteUrl.trim()} onClick={autodetect}>
              Настроить автоматически
            </Button>
          </Group>
          {auto && (
            <Alert color={auto.ok ? 'green' : 'orange'} p="xs">
              <Text size="sm">{auto.ok && auto.method ? `${METHOD_LABEL[auto.method]}. ${auto.message}` : auto.message}</Text>
            </Alert>
          )}
        </Stack>
      </Paper>
      <SegmentedControl value={type} onChange={(v) => setType(v as SourceType)}
        data={[{ value: 'Rss', label: 'RSS' }, { value: 'Html', label: 'HTML' }, { value: 'Vk', label: 'VK' }]} />
      <TextInput label="Название" value={name} onChange={(e) => setName(e.currentTarget.value)} error={errors.name} />
      <TextInput label={type === 'Rss' ? 'Адрес RSS-ленты' : 'Адрес сайта'} value={url} onChange={(e) => setUrl(e.currentTarget.value)}
        error={errors.url} placeholder="https://…" />

      {type === 'Html' && (
        <Stack gap="xs">
          <Group justify="space-between">
            <Divider label="Селекторы страницы" labelPosition="left" style={{ flex: 1 }} />
            <Button size="compact-xs" variant="subtle" onClick={() => setManual((m) => !m)}>Ручная настройка</Button>
          </Group>
          {!manual && html.itemSelector && (
            <Text size="xs" c="dimmed">{`Список: ${html.itemSelector} · ссылка: ${html.linkSelector ?? '—'}${html.dateSelector ? ` · дата: ${html.dateSelector}` : ''}`}</Text>
          )}
          <Collapse in={manual}>
            <Stack gap="xs">
              {HTML_FIELDS.map((f) => (
                <TextInput key={f.key} label={f.label} withAsterisk={f.required} description={f.hint} value={(html[f.key] ?? '') as string}
                  onChange={(e) => { const v = e.currentTarget.value; setHtml((h) => ({ ...h, [f.key]: v })) }} />
              ))}
            </Stack>
          </Collapse>
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
      <Paper withBorder p="sm">
        <Stack gap={6}>
          <Text size="sm" fw={500}>Города источника</Text>
          <Text size="xs" c="dimmed">
            Для сайта или паблика самого города включите «Все новости источника — про город»: каждая его новость пойдёт в работу.
            Для областных СМИ оставьте выключенным: из их новостей возьмутся только те, где есть ключевые слова города,
            а LLM подтвердит, что новость про этот город.
          </Text>
          {cities.map((c) => (
            <Group key={c.id} gap="md" wrap="wrap">
              <Checkbox label={c.name} checked={c.id in links} w={160}
                onChange={(e) => {
                  const on = e.currentTarget.checked
                  setLinks((l) => { const n = { ...l }; if (on) n[c.id] = false; else delete n[c.id]; return n })
                }} />
              {c.id in links && (
                <Switch size="xs" label={`Все новости источника — про ${c.name}`} checked={links[c.id]}
                  onChange={(e) => { const on = e.currentTarget.checked; setLinks((l) => ({ ...l, [c.id]: on })) }} />
              )}
            </Group>
          ))}
          {errors.cities && <Text size="xs" c="red">{errors.cities}</Text>}
        </Stack>
      </Paper>
      <Checkbox label="Включён" checked={enabled} onChange={(e) => setEnabled(e.currentTarget.checked)} />

      <Group justify="flex-end">
        <Button variant="default" loading={busy === 'test'} disabled={busy !== null} onClick={test}>Проверить</Button>
        <Button loading={busy === 'save'} disabled={busy !== null} onClick={save}>Сохранить</Button>
      </Group>
      {result && <SourceTestResult result={result} tz={tz} />}
    </Stack>
  )
}
