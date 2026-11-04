import { useEffect, useMemo, useState } from 'react'
import { useParams, Link } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { useDebouncedValue, useMediaQuery } from '@mantine/hooks'
import { Alert, Anchor, Button, Center, Group, Loader, Stack, Tabs, Text, Title } from '@mantine/core'
import type { ReactNode } from 'react'
import { countersKey, getCounters } from '../api/counters'
import { ackUpdates, getStory, preview, storyKey } from '../api/stories'
import type { DraftDto, DraftInput } from '../api/types'
import { useCityTimeZone } from '../lib/useCityTimeZone'
import { CityTag } from '../design/CityTag'
import { ScoreNumber } from '../design/ScoreRail'
import { tokens } from '../design/tokens'
import { DraftEditor } from './DraftEditor'
import { ImagePicker } from './ImagePicker'
import { MaxPostPreview } from './MaxPostPreview'
import { PublishPanel } from './PublishPanel'
import { SourcesColumn } from './SourcesColumn'
import { useDraftAutosave } from './useDraftAutosave'
import { notifyError, useStoryRefresh } from './useStoryActions'

const STATUS_LABEL: Record<string, string> = {
  New: 'Новый', Processing: 'Обрабатывается', LowInterest: 'Низкий интерес', ReadyForReview: 'На проверке',
  ProcessingFailed: 'Ошибка обработки', Scheduled: 'Запланирован', Published: 'Опубликован',
  PublishFailed: 'Ошибка публикации', Rejected: 'Отклонён',
}

function Column({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section aria-label={title} style={{ background: tokens.panel, padding: '16px 18px', minWidth: 0 }}>
      <Title order={2} fz={15} fw={600} c="ink.8" mb="sm">{title}</Title>
      {children}
    </section>
  )
}

const toInput = (d: DraftDto | null): DraftInput => ({ title: d?.title ?? '', body: d?.body ?? '', hashtags: d?.hashtags ?? [] })
const same = (a: DraftInput, b: DraftInput) =>
  a.title === b.title && a.body === b.body && a.hashtags.join('\u0001') === b.hashtags.join('\u0001')

export function StoryPage() {
  const id = Number(useParams().id)
  const defaultTz = useCityTimeZone()
  const mobile = useMediaQuery('(max-width: 48em)')
  const { data: story, isLoading, error } = useQuery({
    queryKey: storyKey(id),
    queryFn: () => getStory(id),
    refetchInterval: (q) => (q.state.data?.status === 'Processing' || q.state.data?.status === 'New' ? 5_000 : 30_000),
  })
  const { data: counters } = useQuery({ queryKey: countersKey, queryFn: getCounters })
  const tz = story?.timeZone ?? defaultTz
  const refresh = useStoryRefresh(id)

  const [baseline, setBaseline] = useState<DraftInput>(toInput(null))
  const [value, setValue] = useState<DraftInput>(toInput(null))
  const [selectedImageId, setSelectedImageId] = useState<number | null>(null)
  const [restorable, setRestorable] = useState<DraftInput | null>(null)
  const [loadedDraftId, setLoadedDraftId] = useState<number | null | undefined>(undefined)
  const [keptOver, setKeptOver] = useState<number | null>(null)
  const dirty = !same(value, baseline)
  const autosave = useDraftAutosave(id, value, dirty)

  const adopt = (d: DraftDto | null) => {
    const next = toInput(d)
    setLoadedDraftId(d?.id ?? null)
    setBaseline(next)
    setValue(next)
  }

  // Take a new server draft only when the moderator has no unsaved edits; otherwise offer it in a banner.
  const serverDraft = story?.currentDraft ?? null
  const serverDraftId = serverDraft?.id ?? null
  useEffect(() => {
    if (!story || loadedDraftId === serverDraftId) return
    if (loadedDraftId === undefined) {
      adopt(serverDraft)
      const saved = autosave.restore()
      if (saved && !same(saved, toInput(serverDraft))) setRestorable(saved)
      return
    }
    if (!dirty) adopt(serverDraft)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [story, serverDraftId, loadedDraftId, dirty])
  const newerVersion = story && loadedDraftId !== undefined && serverDraftId !== loadedDraftId && dirty && keptOver !== serverDraftId
    ? serverDraft : null

  const selectedImage = story?.images.find((i) => i.id === selectedImageId) ?? null
  const [debounced] = useDebouncedValue(value, 400)
  const { data: previews } = useQuery({
    queryKey: ['preview', debounced, selectedImage?.credit ?? null, story?.cityId],
    queryFn: () => preview({ ...debounced, imageCredit: selectedImage?.credit ?? null, cityId: story?.cityId }),
    enabled: !!story && (debounced.title !== '' || debounced.body !== ''),
    placeholderData: (prev) => prev,
  })
  const maxPreview = previews?.find((p) => p.platform === 'Max') ?? previews?.[0]
  // Only the story's own city channel: another city's limit says nothing about this publication.
  const today = useMemo(() => counters?.today.find((t) => t.cityId === story?.cityId), [counters, story])

  if (isLoading) return <Center h={200}><Loader /></Center>
  if (error || !story) return <Alert color="brick">Сюжет не найден. <Anchor component={Link} to="/">К очереди</Anchor></Alert>

  const onSaved = (d: DraftDto) => {
    adopt(d)
    autosave.clear()
  }
  const onDiscard = () => {
    setValue(baseline)
    autosave.clear()
  }

  const canEditNow = story.status === 'ReadyForReview' || story.status === 'PublishFailed'
  const sourcesCol = <SourcesColumn sources={story.sources} tz={tz} />
  const editorCol = (
    <Stack gap="sm">
      {story.currentDraft
        ? <DraftEditor story={story} value={value} onChange={setValue} dirty={dirty} onSaved={onSaved} onDiscard={onDiscard} />
        : <Text c="ink.6">{story.status === 'Processing' || story.status === 'New' ? 'Черновик готовится…' : 'Черновика нет.'}</Text>}
    </Stack>
  )
  const previewCol = (
    <Stack gap="md">
      {maxPreview && !maxPreview.channelConfigured && (
        <Alert color="amber" p="xs">
          <Text size="sm">Канал Max не настроен или выключен — публикация не сработает.</Text>
          <Anchor component={Link} to="/settings" size="sm">Настроить канал</Anchor>
        </Alert>
      )}
      {maxPreview
        ? <MaxPostPreview text={maxPreview.text} imageUrl={selectedImage?.url} tooLong={maxPreview.tooLong} length={maxPreview.length} max={maxPreview.max} />
        : <Text c="ink.6" size="sm">Превью появится, когда будет текст.</Text>}
      <ImagePicker story={story} selectedImageId={selectedImageId} onSelect={setSelectedImageId} />
    </Stack>
  )

  const heading = story.currentDraft?.title || story.summary || `Сюжет #${story.id}`

  return (
    <Stack gap="md">
      <header style={{ display: 'flex', gap: 18, alignItems: 'flex-start', justifyContent: 'space-between', flexWrap: 'wrap' }}>
        <Stack gap={8} style={{ flex: '1 1 420px', minWidth: 0 }}>
          <Group gap="sm" wrap="wrap">
            <Anchor component={Link} to="/" size="sm" c="ink.6">← Очередь</Anchor>
            <span style={{
              fontSize: 12, fontWeight: 600, padding: '2px 8px', borderRadius: 4, background: tokens.amberTint, color: tokens.amberInk,
            }}>{STATUS_LABEL[story.status] ?? story.status}</span>
            <CityTag name={story.cityName} />
          </Group>
          <Title order={1} fz={{ base: 20, sm: 24 }} lh={1.25} c="ink.8">{heading}</Title>
        </Stack>
        <Group gap="sm" wrap="nowrap" align="center" style={{ flex: '0 1 320px' }}>
          <ScoreNumber score={story.score} size={44} />
          {story.scoreReason && <Text size="sm" c="ink.6" lh={1.35}>{story.scoreReason}</Text>}
        </Group>
      </header>

      {story.hasUpdates && (
        <Alert color="amber" p="xs">
          <Group justify="space-between" wrap="nowrap">
            <Text size="sm">Появились новые источники по сюжету — сверьтесь с колонкой «Источники».</Text>
            <Button size="xs" variant="default" onClick={async () => {
              try { await ackUpdates(story.id); await refresh() } catch (err) { notifyError(err) }
            }}>Просмотрено</Button>
          </Group>
        </Alert>
      )}
      {story.lastError && <Alert color="brick" p="xs" title="Последняя ошибка">{story.lastError}</Alert>}
      {newerVersion && (
        <Alert color="ink" p="xs">
          <Group justify="space-between" wrap="nowrap">
            <Text size="sm">{`Появилась версия ${newerVersion.version}. Ваши несохранённые правки сделаны поверх предыдущей.`}</Text>
            <Group gap="xs" wrap="nowrap">
              <Button size="xs" onClick={() => { adopt(newerVersion); autosave.clear() }}>Показать новую</Button>
              <Button size="xs" variant="default" onClick={() => setKeptOver(newerVersion.id)}>Оставить мои правки</Button>
            </Group>
          </Group>
        </Alert>
      )}
      {restorable && canEditNow && (
        <Alert color="amber" p="xs">
          <Group justify="space-between" wrap="nowrap">
            <Text size="sm">Есть несохранённые правки с прошлого раза.</Text>
            <Group gap="xs" wrap="nowrap">
              <Button size="xs" onClick={() => { setValue(restorable); setRestorable(null) }}>Восстановить</Button>
              <Button size="xs" variant="default" onClick={() => { autosave.clear(); setRestorable(null) }}>Отбросить</Button>
            </Group>
          </Group>
        </Alert>
      )}

      <div style={{ border: `1px solid ${tokens.line}`, borderRadius: 8, overflow: 'hidden' }}>
        {mobile ? (
          <Tabs defaultValue="editor" keepMounted={false} color="ink" style={{ background: tokens.panel }}>
            <Tabs.List grow>
              <Tabs.Tab value="sources">Источники</Tabs.Tab>
              <Tabs.Tab value="editor">Текст</Tabs.Tab>
              <Tabs.Tab value="preview">Превью</Tabs.Tab>
            </Tabs.List>
            <Tabs.Panel value="sources" p="sm">{sourcesCol}</Tabs.Panel>
            <Tabs.Panel value="editor" p="sm">{editorCol}</Tabs.Panel>
            <Tabs.Panel value="preview" p="sm">{previewCol}</Tabs.Panel>
          </Tabs>
        ) : (
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))', gap: 1, background: tokens.line }}>
            <Column title="Источники">{sourcesCol}</Column>
            <Column title="Текст поста">{editorCol}</Column>
            <Column title="Так выйдет в Max">{previewCol}</Column>
          </div>
        )}
        <PublishPanel story={story} previewTooLong={!!maxPreview?.tooLong || dirty} selectedImageId={selectedImageId}
          tz={tz} today={today} />
      </div>
      {dirty && canEditNow && <Text size="sm" c="amber.9">Сохраните правки перед публикацией.</Text>}
    </Stack>
  )
}
