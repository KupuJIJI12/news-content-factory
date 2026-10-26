import { useEffect, useMemo, useRef, useState } from 'react'
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
  const tz = useCityTimeZone()
  const mobile = useMediaQuery('(max-width: 48em)')
  const { data: story, isLoading, error } = useQuery({
    queryKey: storyKey(id),
    queryFn: () => getStory(id),
    refetchInterval: (q) => (q.state.data?.status === 'Processing' || q.state.data?.status === 'New' ? 5_000 : 30_000),
  })
  const { data: counters } = useQuery({ queryKey: countersKey, queryFn: getCounters })
  const refresh = useStoryRefresh(id)

  const [baseline, setBaseline] = useState<DraftInput>(toInput(null))
  const [value, setValue] = useState<DraftInput>(toInput(null))
  const [selectedImageId, setSelectedImageId] = useState<number | null>(null)
  const [restorable, setRestorable] = useState<DraftInput | null>(null)
  const loadedDraftId = useRef<number | null | undefined>(undefined)
  const dirty = !same(value, baseline)
  const autosave = useDraftAutosave(id, value, dirty)

  // Take a new server draft only when it changed and the moderator has no unsaved edits.
  useEffect(() => {
    if (!story) return
    const draftId = story.currentDraft?.id ?? null
    if (loadedDraftId.current === draftId) return
    const first = loadedDraftId.current === undefined
    if (!first && dirty) return
    loadedDraftId.current = draftId
    const next = toInput(story.currentDraft)
    setBaseline(next)
    setValue(next)
    if (first) {
      const saved = autosave.restore()
      if (saved && !same(saved, next)) setRestorable(saved)
    }
  }, [story, dirty, autosave])

  const selectedImage = story?.images.find((i) => i.id === selectedImageId) ?? null
  const [debounced] = useDebouncedValue(value, 400)
  const { data: previews } = useQuery({
    queryKey: ['preview', debounced, selectedImage?.credit ?? null],
    queryFn: () => preview({ ...debounced, imageCredit: selectedImage?.credit ?? null }),
    enabled: !!story && (debounced.title !== '' || debounced.body !== ''),
    placeholderData: (prev) => prev,
  })
  const maxPreview = previews?.find((p) => p.platform === 'Max') ?? previews?.[0]
  const today = useMemo(() => counters?.today[0], [counters])

  if (isLoading) return <Center h={200}><Loader /></Center>
  if (error || !story) return <Alert color="red">Сюжет не найден. <Anchor component={Link} to="/">К очереди</Anchor></Alert>

  const onSaved = (d: DraftDto) => {
    const next = toInput(d)
    loadedDraftId.current = d.id
    setBaseline(next)
    setValue(next)
    autosave.clear()
  }

  const sourcesCol = <SourcesColumn sources={story.sources} tz={tz} />
  const editorCol = (
    <Stack gap="sm">
      {story.currentDraft
        ? <DraftEditor story={story} value={value} onChange={setValue} dirty={dirty} onSaved={onSaved} />
        : <Text c="dimmed">{story.status === 'Processing' || story.status === 'New' ? 'Черновик готовится…' : 'Черновика нет.'}</Text>}
    </Stack>
  )
  const previewCol = (
    <Stack gap="md">
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
      {restorable && (
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
