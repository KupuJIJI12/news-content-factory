import { useState } from 'react'
import { Alert, Button, Group, Modal, Paper, Stack, Text, TextInput } from '@mantine/core'
import { ApiError } from '../api/client'
import { cancel, generate, publish, reject, retry } from '../api/stories'
import type { ChannelToday, PublishInput, StoryDetail } from '../api/types'
import { isPastInCity, toCityIso } from '../lib/cityTime'
import { notifyError, notifyOk, useStoryRefresh } from './useStoryActions'

type Props = {
  story: StoryDetail
  previewTooLong: boolean
  selectedImageId: number | null
  tz: string
  today?: ChannelToday
  sticky?: boolean
}

export function PublishPanel({ story, previewTooLong, selectedImageId, tz, today, sticky }: Props) {
  const [when, setWhen] = useState('') // 'YYYY-MM-DDTHH:mm' from <input type="datetime-local">
  const [busy, setBusy] = useState(false)
  const [limitPrompt, setLimitPrompt] = useState<{ input: PublishInput; text: string } | null>(null)
  const refresh = useStoryRefresh(story.id)

  const wall = when ? when.replace('T', ' ') : ''
  const past = wall !== '' && isPastInCity(wall, tz)
  const willExceed = !!today && today.published + today.scheduled + 1 > today.limit
  const canPublish = story.status === 'ReadyForReview' || story.status === 'PublishFailed'

  async function run(action: () => Promise<unknown>, ok: string) {
    setBusy(true)
    try {
      await action()
      notifyOk(ok)
      await refresh()
    } catch (err) {
      notifyError(err)
    } finally {
      setBusy(false)
    }
  }

  async function send(input: PublishInput) {
    setBusy(true)
    try {
      await publish(story.id, input)
      notifyOk(input.at ? 'Публикация запланирована' : 'Публикация поставлена в очередь')
      setLimitPrompt(null)
      await refresh()
    } catch (err) {
      if (err instanceof ApiError && err.code === 'daily_limit_exceeded') {
        const { published = 0, scheduled = 0, limit = 0 } = err.extra as Record<string, number>
        setLimitPrompt({
          input: { ...input, overrideLimit: true },
          text: `Дневной лимит исчерпан (опубликовано ${published}, запланировано ${scheduled}, лимит ${limit}). Опубликовать всё равно?`,
        })
      } else notifyError(err)
    } finally {
      setBusy(false)
    }
  }

  const input = (at?: string): PublishInput => ({
    ...(at ? { at } : {}),
    overrideLimit: false,
    ...(selectedImageId !== null ? { imageId: selectedImageId } : {}),
  })

  return (
    <Paper withBorder p="sm" style={sticky ? { position: 'sticky', bottom: 0, zIndex: 10 } : undefined}>
      <Stack gap="xs">
        {today && (
          <Text size="sm" c={willExceed ? 'orange' : 'dimmed'}>
            {`Сегодня ${today.published}/${today.limit}` + (today.scheduled ? ` (+${today.scheduled} запланировано)` : '')}
          </Text>
        )}
        {canPublish && willExceed && today && (
          <Alert color="orange" p="xs">{`Дневной лимит будет превышен: ${today.published}/${today.limit} (+${today.scheduled})`}</Alert>
        )}

        {canPublish && (
          <>
            <Group gap="xs" wrap="wrap">
              <Button disabled={previewTooLong} loading={busy} onClick={() => send(input())}>Опубликовать сейчас</Button>
              <Button variant="default" color="red" disabled={busy} onClick={() => run(() => reject(story.id), 'Сюжет отклонён')}>
                Отклонить
              </Button>
            </Group>
            <Group gap="xs" align="flex-end" wrap="wrap">
              <TextInput type="datetime-local" label="Дата и время (по городу)" value={when}
                onChange={(e) => setWhen(e.currentTarget.value)} error={past ? 'Это время уже прошло' : undefined} />
              <Button variant="light" disabled={!when || past || previewTooLong} loading={busy}
                onClick={() => send(input(toCityIso(wall, tz)))}>
                Запланировать
              </Button>
            </Group>
          </>
        )}

        {story.status === 'Scheduled' && (
          <Button color="orange" variant="light" loading={busy} onClick={() => run(() => cancel(story.id), 'Публикация отменена')}>
            Отменить публикацию
          </Button>
        )}
        {story.status === 'ProcessingFailed' && (
          <Button loading={busy} onClick={() => run(() => retry(story.id), 'Обработка запущена заново')}>Повторить</Button>
        )}
        {story.status === 'LowInterest' && (
          <Group gap="xs">
            <Button loading={busy} onClick={() => run(() => generate(story.id), 'Черновик готовится')}>Всё равно сгенерировать</Button>
            <Button variant="default" color="red" disabled={busy} onClick={() => run(() => reject(story.id), 'Сюжет отклонён')}>Отклонить</Button>
          </Group>
        )}
      </Stack>

      <Modal opened={limitPrompt !== null} onClose={() => setLimitPrompt(null)} title="Дневной лимит" centered>
        <Stack>
          <Text>{limitPrompt?.text}</Text>
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setLimitPrompt(null)}>Не публиковать</Button>
            <Button color="orange" loading={busy} onClick={() => limitPrompt && send(limitPrompt.input)}>Опубликовать всё равно</Button>
          </Group>
        </Stack>
      </Modal>
    </Paper>
  )
}
