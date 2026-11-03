import { useState, type ReactNode } from 'react'
import { Button, Group, Modal, Stack, Text } from '@mantine/core'
import { ApiError } from '../api/client'
import { publish } from '../api/stories'
import type { PublishInput, StoryDetail } from '../api/types'
import { notifyError, notifyOk, useStoryRefresh } from './useStoryActions'

/**
 * Publishing with its two safety questions: the daily limit is reached (publish anyway?) and the previous send
 * was not confirmed (the post may already be in the channel). Used by the story page and the queue's side panel.
 */
export function usePublish(story: StoryDetail): {
  busy: boolean
  unknown: boolean
  request: (input: PublishInput) => void
  dialogs: ReactNode
} {
  const [busy, setBusy] = useState(false)
  const [limitPrompt, setLimitPrompt] = useState<{ input: PublishInput; text: string } | null>(null)
  const [republish, setRepublish] = useState<PublishInput | null>(null)
  const refresh = useStoryRefresh(story.id)
  // A send that timed out may still have reached the channel: never re-publish without an explicit yes.
  const unknown = story.publications.some((p) => p.status === 'Unknown')

  async function send(input: PublishInput) {
    setRepublish(null)
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

  const request = (input: PublishInput) => {
    if (unknown) setRepublish(input)
    else void send(input)
  }

  const dialogs = (
    <>
      <Modal opened={republish !== null} onClose={() => setRepublish(null)} title="Опубликовать снова?" centered>
        <Stack>
          <Text>Прошлая отправка не подтвердилась, пост мог уже появиться в канале. Вы проверили канал?</Text>
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setRepublish(null)}>Отмена</Button>
            <Button color="amber.8" loading={busy} onClick={() => republish && send(republish)}>Да, опубликовать снова</Button>
          </Group>
        </Stack>
      </Modal>
      <Modal opened={limitPrompt !== null} onClose={() => setLimitPrompt(null)} title="Дневной лимит" centered>
        <Stack>
          <Text>{limitPrompt?.text}</Text>
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setLimitPrompt(null)}>Не публиковать</Button>
            <Button color="amber.8" loading={busy} onClick={() => limitPrompt && send(limitPrompt.input)}>Опубликовать всё равно</Button>
          </Group>
        </Stack>
      </Modal>
    </>
  )

  return { busy, unknown, request, dialogs }
}
