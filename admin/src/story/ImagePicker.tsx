import { useState } from 'react'
import { ActionIcon, Badge, Button, FileButton, Group, Image, Modal, Paper, Radio, SimpleGrid, Stack, Text, TextInput, Title } from '@mantine/core'
import { IconTrash } from '@tabler/icons-react'
import { ApiError } from '../api/client'
import { deleteImage, imageFromSource, uploadImage } from '../api/stories'
import type { StoryDetail } from '../api/types'
import { notifyError, notifyOk, useStoryRefresh } from './useStoryActions'

type Props = { story: StoryDetail; selectedImageId: number | null; onSelect: (id: number | null) => void }

export const MAX_UPLOAD_BYTES = 15 * 1024 * 1024

export function ImagePicker({ story, selectedImageId, onSelect }: Props) {
  const [credit, setCredit] = useState('')
  const [busy, setBusy] = useState(false)
  const [uploadError, setUploadError] = useState<string | null>(null)
  const [askPermission, setAskPermission] = useState<{ rawItemId: number; url: string } | null>(null)
  const refresh = useStoryRefresh(story.id)
  const attached = new Set(story.images.map((i) => i.url))

  async function attach(rawItemId: number, url: string, acknowledge: boolean) {
    setBusy(true)
    try {
      const image = await imageFromSource(story.id, rawItemId, url, acknowledge)
      onSelect(image.id)
      notifyOk('Фото добавлено')
      await refresh()
    } catch (err) {
      if (err instanceof ApiError && err.code === 'photo_not_allowed') setAskPermission({ rawItemId, url })
      else notifyError(err)
    } finally {
      setBusy(false)
    }
  }

  async function upload(file: File | null) {
    if (!file) return
    setUploadError(null)
    if (file.size > MAX_UPLOAD_BYTES) {
      setUploadError('Картинка больше 15 МБ')
      return
    }
    setBusy(true)
    try {
      const image = await uploadImage(story.id, file, credit.trim() || null)
      onSelect(image.id)
      setCredit('')
      notifyOk('Фото загружено')
      await refresh()
    } catch (err) {
      notifyError(err)
    } finally {
      setBusy(false)
    }
  }

  async function remove(imageId: number) {
    try {
      await deleteImage(story.id, imageId)
      if (selectedImageId === imageId) onSelect(null)
      await refresh()
    } catch (err) {
      notifyError(err)
    }
  }

  const sourceImages = story.sources.flatMap((s) => s.images.map((img) => ({ ...img, rawItemId: s.rawItemId, sourceName: s.sourceName })))

  return (
    <Stack gap="sm">
      <Title order={5}>Фото</Title>
      <Radio.Group value={selectedImageId === null ? 'none' : String(selectedImageId)}
        onChange={(v) => onSelect(v === 'none' ? null : Number(v))}>
        <Stack gap="xs">
          <Radio value="none" label="Без фото" />
          {story.images.map((img) => (
            <Group key={img.id} gap="xs" wrap="nowrap">
              <Radio value={String(img.id)} />
              <Image src={img.url} alt="" w={72} h={48} fit="cover" radius="sm" />
              <Stack gap={0} style={{ flex: 1 }}>
                <Text size="xs">{img.credit ? `Подпись: ${img.credit}` : 'Без подписи'}</Text>
                <Badge size="xs" variant="light" color={img.isAllowed ? 'green' : 'orange'}>
                  {img.uploaded ? 'загружено' : img.isAllowed ? 'можно с подписью' : 'нет разрешения'}
                </Badge>
              </Stack>
              <ActionIcon variant="subtle" color="red" aria-label="Удалить фото" onClick={() => remove(img.id)}><IconTrash size={16} /></ActionIcon>
            </Group>
          ))}
        </Stack>
      </Radio.Group>

      {sourceImages.length > 0 && (
        <Paper withBorder p="xs">
          <Text size="xs" c="dimmed" mb={4}>Фото из источников</Text>
          <SimpleGrid cols={{ base: 3, sm: 4 }} spacing={4}>
            {sourceImages.filter((i) => !attached.has(i.url)).map((img) => (
              <Stack key={img.url} gap={2}>
                <Image src={img.url} alt="" h={60} fit="cover" radius="sm" referrerPolicy="no-referrer" />
                <Button size="compact-xs" variant="light" color={img.allowed ? 'green' : 'gray'} disabled={busy}
                  onClick={() => attach(img.rawItemId, img.url, false)}>
                  {img.allowed ? 'Взять' : 'Без разрешения'}
                </Button>
              </Stack>
            ))}
          </SimpleGrid>
        </Paper>
      )}

      <Group align="flex-end" gap="xs" wrap="nowrap">
        <TextInput style={{ flex: 1 }} size="xs" label="Подпись к своему фото" placeholder="Фото: …" value={credit}
          onChange={(e) => setCredit(e.currentTarget.value)} />
        <FileButton onChange={upload} accept="image/jpeg,image/png,image/webp,image/gif">
          {(props) => <Button size="xs" variant="default" loading={busy} {...props}>Загрузить своё</Button>}
        </FileButton>
      </Group>

      {uploadError && <Text size="xs" c="red">{uploadError}</Text>}

      <Modal opened={askPermission !== null} onClose={() => setAskPermission(null)} title="Нет разрешения" centered>
        <Stack>
          <Text>У источника нет разрешения на фото. Всё равно использовать?</Text>
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setAskPermission(null)}>Отмена</Button>
            <Button color="orange" onClick={() => { const a = askPermission!; setAskPermission(null); void attach(a.rawItemId, a.url, true) }}>
              Использовать
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  )
}
