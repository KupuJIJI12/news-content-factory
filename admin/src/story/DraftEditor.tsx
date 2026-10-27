import { useState } from 'react'
import { Badge, Button, Group, Highlight, List, Modal, Paper, Select, Stack, TagsInput, Text, Textarea, TextInput, Title } from '@mantine/core'
import { regenerate, saveDraft } from '../api/stories'
import type { DraftDto, DraftInput, StoryDetail } from '../api/types'
import { notifyError, notifyOk, useStoryRefresh } from './useStoryActions'

const EDITABLE = ['ReadyForReview', 'PublishFailed']
const REGENERATABLE = ['ReadyForReview', 'LowInterest', 'ProcessingFailed']

type Props = {
  story: StoryDetail
  value: DraftInput
  onChange: (value: DraftInput) => void
  dirty: boolean
  onSaved: (draft: DraftDto) => void
  /** Called when the moderator confirmed a regeneration that drops their unsaved edits. */
  onDiscard?: () => void
}

export function DraftEditor({ story, value, onChange, dirty, onSaved, onDiscard }: Props) {
  const [hint, setHint] = useState('')
  const [confirm, setConfirm] = useState(false)
  const [busy, setBusy] = useState(false)
  const [viewVersion, setViewVersion] = useState<string | null>(null)
  const refresh = useStoryRefresh(story.id)
  const canEdit = EDITABLE.includes(story.status)
  const canRegenerate = REGENERATABLE.includes(story.status)
  const issues = story.currentDraft?.factCheckIssues ?? []
  const claims = issues.map((i) => i.claim)
  const viewed = story.versions.find((v) => String(v.id) === viewVersion)

  async function save() {
    setBusy(true)
    try {
      const saved = await saveDraft(story.id, value)
      notifyOk(`Сохранено как версия ${saved.version}`)
      onSaved(saved)
      await refresh()
    } catch (err) {
      notifyError(err)
    } finally {
      setBusy(false)
    }
  }

  async function doRegenerate() {
    setConfirm(false)
    setBusy(true)
    try {
      await regenerate(story.id, hint.trim() || null)
      if (dirty) onDiscard?.()
      notifyOk('Новая версия готовится, страница обновится сама')
      setHint('')
      await refresh()
    } catch (err) {
      notifyError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Stack gap="sm">
      <TextInput label="Заголовок" value={value.title} disabled={!canEdit}
        onChange={(e) => onChange({ ...value, title: e.currentTarget.value })} />
      <Textarea label="Текст" autosize minRows={8} value={value.body} disabled={!canEdit}
        onChange={(e) => onChange({ ...value, body: e.currentTarget.value })} />
      <TagsInput label="Хэштеги" value={value.hashtags} disabled={!canEdit} onChange={(hashtags) => onChange({ ...value, hashtags })} />
      <Group justify="space-between">
        <Text size="xs" c="dimmed">{story.currentDraft ? `Версия ${story.currentDraft.version}${story.currentDraft.byHuman ? ' (правка)' : ''}` : 'Черновика ещё нет'}</Text>
        <Button onClick={save} disabled={!canEdit || !dirty} loading={busy}>Сохранить</Button>
      </Group>

      {issues.length > 0 && (
        <Paper withBorder p="sm" bg="var(--mantine-color-yellow-light)">
          <Title order={6} mb={4}>Замечания проверки фактов</Title>
          <List size="sm" spacing={4}>
            {issues.map((i, n) => (
              <List.Item key={n}>
                <Highlight component="span" highlight={i.claim} size="sm">{i.claim}</Highlight>
                {' — '}<span>{i.problem}</span>{' '}
                <Badge size="xs" variant="light">{i.source === 'Llm' ? 'LLM' : 'проверка чисел'}</Badge>
              </List.Item>
            ))}
          </List>
          <Text size="xs" c="dimmed" mt="xs">Текст с подсветкой:</Text>
          <Highlight highlight={claims} size="sm" style={{ whiteSpace: 'pre-wrap' }}>{value.body}</Highlight>
        </Paper>
      )}

      {canRegenerate && (
        <Group align="flex-end" gap="xs" wrap="nowrap">
          <TextInput style={{ flex: 1 }} label="Пожелание к новой версии" placeholder="например: короче, без кликбейта"
            value={hint} onChange={(e) => setHint(e.currentTarget.value)} />
          <Button variant="light" loading={busy} onClick={() => (dirty ? setConfirm(true) : doRegenerate())}>Перегенерировать</Button>
        </Group>
      )}

      {story.versions.length > 1 && (
        <Stack gap={4}>
          <Select label="История версий" placeholder="Выберите версию" value={viewVersion} onChange={setViewVersion} clearable
            data={story.versions.map((v) => ({ value: String(v.id),
              label: `Версия ${v.version}${v.byHuman ? ' (правка)' : ''}${v.isCurrent ? ' — текущая' : ''}${v.regenerationHint ? ` · «${v.regenerationHint}»` : ''}` }))} />
          {viewed && (
            <Paper withBorder p="xs">
              <Text fw={600} size="sm">{viewed.title}</Text>
              <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>{viewed.body}</Text>
              {canEdit && (
                <Button size="xs" variant="subtle" mt="xs"
                  onClick={() => onChange({ title: viewed.title, body: viewed.body, hashtags: viewed.hashtags })}>
                  Взять за основу
                </Button>
              )}
            </Paper>
          )}
        </Stack>
      )}

      <Modal opened={confirm} onClose={() => setConfirm(false)} title="Перегенерировать?" centered>
        <Stack>
          <Text>Есть несохранённые правки. Новая версия от LLM их не учтёт.</Text>
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setConfirm(false)}>Отмена</Button>
            <Button color="orange" onClick={doRegenerate}>Перегенерировать всё равно</Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  )
}
