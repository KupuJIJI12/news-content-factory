import { useState } from 'react'
import { Button, Group, Modal, Stack, Switch, Text } from '@mantine/core'
import { useQueryClient } from '@tanstack/react-query'
import { notifications } from '@mantine/notifications'
import { ApiError } from '../api/client'
import { setCollectionPaused } from '../api/collection'
import { countersKey } from '../api/counters'

/** "Freeze" switch for scheduled news collection; pausing asks first, resuming does not. */
export function CollectionToggle({ paused }: { paused: boolean }) {
  const queryClient = useQueryClient()
  const [confirm, setConfirm] = useState(false)
  const [busy, setBusy] = useState(false)

  async function apply(next: boolean) {
    setConfirm(false)
    setBusy(true)
    try {
      await setCollectionPaused(next)
      notifications.show({ color: next ? 'orange' : 'green', message: next ? 'Сбор новостей на паузе' : 'Сбор новостей возобновлён' })
      await queryClient.invalidateQueries({ queryKey: countersKey })
    } catch (err) {
      notifications.show({ color: 'red', message: err instanceof ApiError ? err.message : 'Сервер недоступен' })
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <Switch label="Сбор новостей" checked={!paused} disabled={busy} color="green" size="sm"
        onChange={() => (paused ? apply(false) : setConfirm(true))} />
      <Modal opened={confirm} onClose={() => setConfirm(false)} title="Поставить сбор на паузу?" centered>
        <Stack>
          <Text size="sm">
            Источники перестанут опрашиваться, новые новости не появятся. Уже собранные новости дообработаются, очередь
            и публикации работают как обычно. Включить сбор можно в любой момент.
          </Text>
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setConfirm(false)}>Отмена</Button>
            <Button color="orange" onClick={() => apply(true)}>Поставить на паузу</Button>
          </Group>
        </Stack>
      </Modal>
    </>
  )
}
