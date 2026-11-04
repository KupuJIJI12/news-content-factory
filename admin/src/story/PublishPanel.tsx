import { useState } from 'react'
import { Alert, Button, Group, Stack, Text, TextInput } from '@mantine/core'
import { useNavigate } from 'react-router'
import { useQueryClient } from '@tanstack/react-query'
import { cancel, deleteStory, generate, reject, retry } from '../api/stories'
import type { ChannelToday, PublicationStatus, PublishInput, StoryDetail } from '../api/types'
import { ConfirmDelete } from '../design/ConfirmDelete'
import { tokens } from '../design/tokens'
import { formatInCity, isPastInCity, toCityIso } from '../lib/cityTime'
import { usePublish } from './usePublish'
import { notifyError, notifyOk, useStoryRefresh } from './useStoryActions'

const PUB_LABEL: Record<PublicationStatus, string> = {
  Scheduled: 'запланирована', Sending: 'отправляется', Published: 'опубликована', Failed: 'ошибка',
  Unknown: 'статус неизвестен', Cancelled: 'отменена',
}
const PUB_COLOR: Record<PublicationStatus, string> = {
  Scheduled: 'amber.9', Sending: 'amber.9', Published: 'teal.7', Failed: 'brick.6', Unknown: 'amber.9', Cancelled: 'ink.6',
}

type Props = {
  story: StoryDetail
  previewTooLong: boolean
  selectedImageId: number | null
  tz: string
  today?: ChannelToday
  sticky?: boolean
}

/** The story's action bar: what happened to its publications, today's limit, and the decision buttons. */
export function PublishPanel({ story, previewTooLong, selectedImageId, tz, today, sticky = true }: Props) {
  const [when, setWhen] = useState('') // 'YYYY-MM-DDTHH:mm' from <input type="datetime-local">
  const [busy, setBusy] = useState(false)
  const refresh = useStoryRefresh(story.id)
  const pub = usePublish(story)
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [confirmDelete, setConfirmDelete] = useState(false)

  async function remove() {
    setBusy(true)
    try {
      await deleteStory(story.id)
      notifyOk('Сюжет удалён')
      setConfirmDelete(false)
      await queryClient.invalidateQueries({ queryKey: ['stories'] })
      navigate('/?tab=Rejected')
    } catch (err) {
      notifyError(err)
    } finally {
      setBusy(false)
    }
  }

  const wall = when ? when.replace('T', ' ') : ''
  const past = wall !== '' && isPastInCity(wall, tz)
  const willExceed = !!today && today.published + today.scheduled + 1 > today.limit
  const canPublish = story.status === 'ReadyForReview' || story.status === 'PublishFailed'
  const publications = [...story.publications].sort((a, b) => b.id - a.id)
  const working = busy || pub.busy

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

  const input = (at?: string): PublishInput => ({
    ...(at ? { at } : {}),
    overrideLimit: false,
    ...(selectedImageId !== null ? { imageId: selectedImageId } : {}),
  })

  return (
    <div style={{
      ...(sticky ? { position: 'sticky', bottom: 0, zIndex: 10 } : {}),
      background: tokens.surface, borderTop: `1px solid ${tokens.line}`, padding: '12px 24px',
    }}>
      <Stack gap="xs">
        {pub.unknown && (
          <Alert color="amber" p="xs" title="Пост мог уже выйти">
            Отправка не подтвердилась — проверьте канал вручную, прежде чем публиковать снова.
          </Alert>
        )}
        {publications.length > 0 && (
          <Stack gap={2}>
            {publications.map((p) => (
              <Text key={p.id} size="sm">
                <Text span c={PUB_COLOR[p.status]} fw={600} size="sm">{`${p.channelName}: ${PUB_LABEL[p.status]}`}</Text>
                {`, ${formatInCity(p.publishedAt ?? p.scheduledAt, tz)}`}
                {p.lastError && <Text span c="brick.6" size="sm">{`. ${p.lastError}`}</Text>}
              </Text>
            ))}
          </Stack>
        )}
        {canPublish && willExceed && today && (
          <Alert color="amber" p="xs">{`Дневной лимит будет превышен: ${today.published}/${today.limit} (+${today.scheduled})`}</Alert>
        )}

        <Group gap="sm" wrap="wrap" align="flex-start">
          {today && (
            <Text size="sm" c="ink.6" style={{ alignSelf: 'center' }}>
              {'В канале сегодня '}
              <Text span className="nf-num" fw={600} c={willExceed ? 'brick.6' : 'ink.8'}>{`${today.published} из ${today.limit}`}</Text>
              {today.scheduled ? `, ещё ${today.scheduled} в плане` : ''}
            </Text>
          )}
          <span style={{ flex: 1 }} />

          {canPublish && (
            <>
              {story.status === 'ReadyForReview' ? (
                <Button variant="subtle" color="brick" disabled={working} onClick={() => run(() => reject(story.id), 'Сюжет отклонён')}>
                  Отклонить
                </Button>
              ) : (
                <Button variant="subtle" color="ink" disabled={working} onClick={() => run(() => cancel(story.id), 'Сюжет возвращён на проверку')}>
                  Вернуть на проверку
                </Button>
              )}
              <Group gap={6} wrap="nowrap" align="flex-start">
                <TextInput type="datetime-local" aria-label="Дата и время (по городу)" value={when} w={210}
                  onChange={(e) => setWhen(e.currentTarget.value)} error={past ? 'Это время уже прошло' : undefined} />
                <Button variant="default" disabled={!when || past || previewTooLong} loading={working}
                  onClick={() => pub.request(input(toCityIso(wall, tz)))}>
                  Запланировать
                </Button>
              </Group>
              <Button disabled={previewTooLong} loading={working} onClick={() => pub.request(input())}>Опубликовать сейчас</Button>
            </>
          )}

          {story.status === 'Scheduled' && (
            <Button color="amber.8" variant="light" loading={working} onClick={() => run(() => cancel(story.id), 'Публикация отменена')}>
              Отменить публикацию
            </Button>
          )}
          {story.status === 'ProcessingFailed' && (
            <Button loading={working} onClick={() => run(() => retry(story.id), 'Обработка запущена заново')}>Повторить</Button>
          )}
          {story.status === 'Rejected' && (
            <Button variant="subtle" color="brick" loading={working} onClick={() => setConfirmDelete(true)}>Удалить сюжет</Button>
          )}
          {story.status === 'LowInterest' && (
            <>
              <Button variant="subtle" color="brick" disabled={working} onClick={() => run(() => reject(story.id), 'Сюжет отклонён')}>Отклонить</Button>
              <Button loading={working} onClick={() => run(() => generate(story.id), 'Черновик готовится')}>Всё равно сгенерировать</Button>
            </>
          )}
        </Group>
      </Stack>
      {pub.dialogs}
      <ConfirmDelete opened={confirmDelete} busy={busy} onClose={() => setConfirmDelete(false)} onConfirm={remove}
        title="Удалить сюжет?"
        text="Сюжет пропадёт из «Отклонено». Те же новости не вернутся в очередь: собранные статьи остаются в базе, чтобы их не собирать повторно." />
    </div>
  )
}
