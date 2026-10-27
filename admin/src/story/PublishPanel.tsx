import { useState } from 'react'
import { Alert, Button, Group, Modal, Paper, Stack, Text, TextInput } from '@mantine/core'
import { ApiError } from '../api/client'
import { cancel, generate, publish, reject, retry } from '../api/stories'
import type { ChannelToday, PublicationStatus, PublishInput, StoryDetail } from '../api/types'
import { formatInCity, isPastInCity, toCityIso } from '../lib/cityTime'
import { notifyError, notifyOk, useStoryRefresh } from './useStoryActions'

const PUB_LABEL: Record<PublicationStatus, string> = {
  Scheduled: 'запланирована', Sending: 'отправляется', Published: 'опубликована', Failed: 'ошибка',
  Unknown: 'статус неизвестен', Cancelled: 'отменена',
}
const PUB_COLOR: Record<PublicationStatus, string> = {
  Scheduled: 'blue', Sending: 'blue', Published: 'green', Failed: 'red', Unknown: 'orange', Cancelled: 'gray',
}

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
  const [republish, setRepublish] = useState<PublishInput | null>(null)
  const refresh = useStoryRefresh(story.id)

  const wall = when ? when.replace('T', ' ') : ''
  const past = wall !== '' && isPastInCity(wall, tz)
  const willExceed = !!today && today.published + today.scheduled + 1 > today.limit
  const canPublish = story.status === 'ReadyForReview' || story.status === 'PublishFailed'
  // A send that timed out may still have reached the channel: never re-publish without an explicit yes.
  const unknown = story.publications.some((p) => p.status === 'Unknown')
  const publications = [...story.publications].sort((a, b) => b.id - a.id)

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

  function request(input: PublishInput) {
    if (unknown) setRepublish(input)
    else void send(input)
  }

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

  const input = (at?: string): PublishInput => ({
    ...(at ? { at } : {}),
    overrideLimit: false,
    ...(selectedImageId !== null ? { imageId: selectedImageId } : {}),
  })

  return (
    <Paper withBorder p="sm" style={sticky ? { position: 'sticky', bottom: 0, zIndex: 10 } : undefined}>
      <Stack gap="xs">
        {unknown && (
          <Alert color="orange" p="xs" title="Пост мог уже выйти">
            Отправка не подтвердилась — проверьте канал вручную, прежде чем публиковать снова.
          </Alert>
        )}
        {publications.length > 0 && (
          <Stack gap={2}>
            {publications.map((p) => (
              <Text key={p.id} size="xs">
                <Text span c={PUB_COLOR[p.status]} fw={600} size="xs">{`${p.channelName}: ${PUB_LABEL[p.status]}`}</Text>
                {` · ${formatInCity(p.publishedAt ?? p.scheduledAt, tz)}`}
                {p.lastError && <Text span c="red" size="xs">{` · ${p.lastError}`}</Text>}
              </Text>
            ))}
          </Stack>
        )}
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
              <Button disabled={previewTooLong} loading={busy} onClick={() => request(input())}>Опубликовать сейчас</Button>
              {story.status === 'ReadyForReview' ? (
                <Button variant="default" color="red" disabled={busy} onClick={() => run(() => reject(story.id), 'Сюжет отклонён')}>
                  Отклонить
                </Button>
              ) : (
                <Button variant="default" disabled={busy} onClick={() => run(() => cancel(story.id), 'Сюжет возвращён на проверку')}>
                  Вернуть на проверку
                </Button>
              )}
            </Group>
            <Group gap="xs" align="flex-end" wrap="wrap">
              <TextInput type="datetime-local" label="Дата и время (по городу)" value={when}
                onChange={(e) => setWhen(e.currentTarget.value)} error={past ? 'Это время уже прошло' : undefined} />
              <Button variant="light" disabled={!when || past || previewTooLong} loading={busy}
                onClick={() => request(input(toCityIso(wall, tz)))}>
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

      <Modal opened={republish !== null} onClose={() => setRepublish(null)} title="Опубликовать снова?" centered>
        <Stack>
          <Text>Прошлая отправка не подтвердилась, пост мог уже появиться в канале. Вы проверили канал?</Text>
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setRepublish(null)}>Отмена</Button>
            <Button color="orange" loading={busy} onClick={() => republish && send(republish)}>Да, опубликовать снова</Button>
          </Group>
        </Stack>
      </Modal>

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
