import { Alert, Anchor, Paper, Stack, Text } from '@mantine/core'
import type { SourceTestResult as Result } from '../api/types'
import { formatInCity } from '../lib/cityTime'

export function SourceTestResult({ result, tz }: { result: Result; tz: string }) {
  if (!result.ok) return <Alert color="red" title="Проверка не прошла">{result.error ?? 'Неизвестная ошибка'}</Alert>
  if (result.items.length === 0) return <Alert color="yellow">Источник ответил, но новостей не найдено — проверьте селекторы.</Alert>
  return (
    <Stack gap="xs">
      <Text size="sm" c="green">{`Найдено: ${result.items.length}`}</Text>
      {result.items.map((it) => (
        <Paper key={it.url} withBorder p="xs">
          <Anchor href={it.url} target="_blank" rel="noreferrer" size="sm" fw={600}>{it.title}</Anchor>
          <Text size="xs" c="dimmed">
            {(it.publishedAt ? formatInCity(it.publishedAt, tz) : 'без даты') + ` · Фото: ${it.imageCount}`}
          </Text>
          <Text size="xs" lineClamp={3}>{it.textPreview}</Text>
        </Paper>
      ))}
    </Stack>
  )
}
