import { Button, Group, Modal, Stack, Text } from '@mantine/core'

type Props = { opened: boolean; title: string; text: string; busy: boolean; onConfirm: () => void; onClose: () => void }

/** One confirmation for every delete in the admin. */
export function ConfirmDelete({ opened, title, text, busy, onConfirm, onClose }: Props) {
  return (
    <Modal opened={opened} onClose={onClose} title={title} centered>
      <Stack>
        <Text size="sm">{text}</Text>
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>Отмена</Button>
          <Button color="brick" loading={busy} onClick={onConfirm}>Удалить</Button>
        </Group>
      </Stack>
    </Modal>
  )
}
