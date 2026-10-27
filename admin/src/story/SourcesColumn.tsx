import { Anchor, Badge, Group, Image, Paper, SimpleGrid, Spoiler, Stack, Text, Title } from '@mantine/core'
import type { StorySourceDto } from '../api/types'
import { formatInCity } from '../lib/cityTime'

export function SourcesColumn({ sources, tz }: { sources: StorySourceDto[]; tz: string }) {
  return (
    <Stack gap="sm">
      {sources.map((s) => (
        <Paper key={s.rawItemId} withBorder p="sm">
          <Group justify="space-between" wrap="nowrap" mb={4}>
            <Text fw={600} size="sm">{s.sourceName}</Text>
            {s.publishedAt && <Text size="xs" c="dimmed">{formatInCity(s.publishedAt, tz)}</Text>}
          </Group>
          <Title order={6} mb={4}>{s.title}</Title>
          <Spoiler maxHeight={160} showLabel="Показать полностью" hideLabel="Свернуть">
            <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>{s.text}</Text>
          </Spoiler>
          <Anchor href={s.url} target="_blank" rel="noreferrer" size="xs">Открыть оригинал</Anchor>
          {s.images.length > 0 && (
            <SimpleGrid cols={3} spacing={4} mt="xs">
              {s.images.map((img) => (
                <Stack key={img.url} gap={2}>
                  <Image src={img.url} alt="" h={70} fit="cover" radius="sm" referrerPolicy="no-referrer" />
                  <Badge size="xs" color={img.allowed ? 'green' : 'gray'} variant="light">
                    {img.allowed ? 'можно с подписью' : 'нет разрешения'}
                  </Badge>
                </Stack>
              ))}
            </SimpleGrid>
          )}
        </Paper>
      ))}
    </Stack>
  )
}
