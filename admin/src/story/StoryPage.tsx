import { useEffect, useMemo, useState } from 'react'
import { useParams, Link } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { useDebouncedValue, useMediaQuery } from '@mantine/hooks'
import { Alert, Anchor, Badge, Button, Center, Grid, Group, Loader, Stack, Tabs, Text, Title } from '@mantine/core'
import { countersKey, getCounters } from '../api/counters'
import { ackUpdates, getStory, preview, storyKey } from '../api/stories'
import type { DraftDto, DraftInput } from '../api/types'
import { useCityTimeZone } from '../lib/useCityTimeZone'
import { ScoreBadge } from '../queue/ScoreBadge'
import { DraftEditor } from './DraftEditor'
import { ImagePicker } from './ImagePicker'
import { MaxPostPreview } from './MaxPostPreview'
import { PublishPanel } from './PublishPanel'
import { SourcesColumn } from './SourcesColumn'
import { useDraftAutosave } from './useDraftAutosave'
import { useCities } from '../lib/useCities'
import { notifyError, useStoryRefresh } from './useStoryActions'

const STATUS_LABEL: Record<string, string> = {
  New: 'Новый', Processing: 'Обрабатывается', LowInterest: 'Низкий интерес', ReadyForReview: 'На проверке',
  ProcessingFailed: 'Ошибка обработки', Scheduled: 'Запланирован', Published: 'Опубликован',
  PublishFailed: 'Ошибка публикации', Rejected: 'Отклонён',
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
  const { multi } = useCities()
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
  if (error || !story) return <Alert color="red">Сюжет не найден. <Anchor component={Link} to="/">К очереди</Anchor></Alert>

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
        : <Text c="dimmed">{story.status === 'Processing' || story.status === 'New' ? 'Черновик готовится…' : 'Черновика нет.'}</Text>}
    </Stack>
  )
  const previewCol = (
    <Stack gap="md">
      {maxPreview && !maxPreview.channelConfigured && (
        <Alert color="orange" p="xs">
          <Text size="sm">Канал Max не настроен или выключен — публикация не сработает.</Text>
          <Anchor component={Link} to="/settings" size="sm">Настроить канал</Anchor>
        </Alert>
      )}
      {maxPreview
        ? <MaxPostPreview text={maxPreview.text} imageUrl={selectedImage?.url} tooLong={maxPreview.tooLong} length={maxPreview.length} max={maxPreview.max} />
        : <Text c="dimmed" size="sm">Превью появится, когда будет текст.</Text>}
      <ImagePicker story={story} selectedImageId={selectedImageId} onSelect={setSelectedImageId} />
    </Stack>
  )

  return (
    <Stack gap="sm" pb={mobile ? 0 : 'md'}>
      <Group gap="xs" wrap="wrap">
        <Anchor component={Link} to="/" size="sm">← Очередь</Anchor>
        {multi && <Badge variant="outline" color="gray">{story.cityName}</Badge>}
        <Badge variant="light">{STATUS_LABEL[story.status] ?? story.status}</Badge>
        <ScoreBadge score={story.score} />
        {story.scoreReason && <Text size="xs" c="dimmed">{story.scoreReason}</Text>}
      </Group>
      <Title order={3}>{story.currentDraft?.title || story.summary || `Сюжет #${story.id}`}</Title>

      {story.hasUpdates && (
        <Alert color="blue" p="xs">
          <Group justify="space-between" wrap="nowrap">
            <Text size="sm">Появились новые источники по сюжету — сверьтесь с левой колонкой.</Text>
            <Button size="xs" variant="light" onClick={async () => {
              try { await ackUpdates(story.id); await refresh() } catch (err) { notifyError(err) }
            }}>Просмотрено</Button>
          </Group>
        </Alert>
      )}
      {story.lastError && <Alert color="red" p="xs" title="Последняя ошибка">{story.lastError}</Alert>}
      {newerVersion && (
        <Alert color="blue" p="xs">
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
        <Alert color="yellow" p="xs">
          <Group justify="space-between" wrap="nowrap">
            <Text size="sm">Есть несохранённые правки с прошлого раза.</Text>
            <Group gap="xs" wrap="nowrap">
              <Button size="xs" onClick={() => { setValue(restorable); setRestorable(null) }}>Восстановить</Button>
              <Button size="xs" variant="default" onClick={() => { autosave.clear(); setRestorable(null) }}>Отбросить</Button>
            </Group>
          </Group>
        </Alert>
      )}

      {mobile ? (
        <Tabs defaultValue="editor" keepMounted={false}>
          <Tabs.List grow>
            <Tabs.Tab value="sources">Источники</Tabs.Tab>
            <Tabs.Tab value="editor">Редактор</Tabs.Tab>
            <Tabs.Tab value="preview">Превью</Tabs.Tab>
          </Tabs.List>
          <Tabs.Panel value="sources" pt="sm">{sourcesCol}</Tabs.Panel>
          <Tabs.Panel value="editor" pt="sm">{editorCol}</Tabs.Panel>
          <Tabs.Panel value="preview" pt="sm">{previewCol}</Tabs.Panel>
        </Tabs>
      ) : (
        <Grid gutter="md">
          <Grid.Col span={{ base: 12, md: 3.5 }}>{sourcesCol}</Grid.Col>
          <Grid.Col span={{ base: 12, md: 4.5 }}>{editorCol}</Grid.Col>
          <Grid.Col span={{ base: 12, md: 4 }}>{previewCol}</Grid.Col>
        </Grid>
      )}

      <PublishPanel story={story} previewTooLong={!!maxPreview?.tooLong || dirty} selectedImageId={selectedImageId}
        tz={tz} today={today} sticky={!!mobile} />
      {dirty && (story.status === 'ReadyForReview' || story.status === 'PublishFailed') &&
        <Text size="xs" c="orange">Сохраните правки перед публикацией.</Text>}
    </Stack>
  )
}
