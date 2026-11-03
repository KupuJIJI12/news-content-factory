import { Anchor, Badge, Group, Image, SimpleGrid, Spoiler, Stack, Text } from '@mantine/core'
import type { StorySourceDto } from '../api/types'
import { tokens } from '../design/tokens'
import { formatInCity } from '../lib/cityTime'

export function SourcesColumn({ sources, tz }: { sources: StorySourceDto[]; tz: string }) {
  return (
    <Stack gap="sm">
      {sources.map((s) => (
        <article key={s.rawItemId} style={{
          background: tokens.surface, border: `1px solid ${tokens.lineSoft}`, borderRadius: 8, padding: 14,
          display: 'flex', flexDirection: 'column', gap: 8,
        }}>
          <Group justify="space-between" wrap="nowrap" gap="xs">
            <Text fw={600} size="sm">{s.sourceName}</Text>
            {s.publishedAt && <Text size="xs" c="ink.6" style={{ whiteSpace: 'nowrap' }}>{formatInCity(s.publishedAt, tz)}</Text>}
          </Group>
          <Text fw={600} size="sm">{s.title}</Text>
          <Spoiler maxHeight={160} showLabel="Показать полностью" hideLabel="Свернуть" styles={{ control: { fontSize: 13 } }}>
            <Text size="sm" c="ink.7" lh={1.55} style={{ whiteSpace: 'pre-wrap' }}>{s.text}</Text>
          </Spoiler>
          <Anchor href={s.url} target="_blank" rel="noreferrer" size="xs" c={tokens.link}>Открыть оригинал</Anchor>
          {s.images.length > 0 && (
            <SimpleGrid cols={3} spacing={4}>
              {s.images.map((img) => (
                <Stack key={img.url} gap={2}>
                  <Image src={img.url} alt="" h={70} fit="cover" radius="sm" referrerPolicy="no-referrer" />
                  <Badge size="xs" color={img.allowed ? 'teal' : 'ink'} variant="light">
                    {img.allowed ? 'можно с подписью' : 'нет разрешения'}
                  </Badge>
                </Stack>
              ))}
            </SimpleGrid>
          )}
        </article>
      ))}
    </Stack>
  )
}
