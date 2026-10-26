import { Fragment, type ReactNode } from 'react'
import { Alert, Box, Image, Paper, Text } from '@mantine/core'

/** Renders the subset of Max markdown our posts use: **bold**, line breaks, "• " bullets, hashtags. Everything else is text. */
function renderLine(line: string, key: number): ReactNode {
  const parts: ReactNode[] = []
  const pattern = /\*\*(.+?)\*\*|(#[\p{L}\p{N}_]+)/gu
  let last = 0
  let i = 0
  for (const m of line.matchAll(pattern)) {
    if (m.index! > last) parts.push(line.slice(last, m.index))
    if (m[1] !== undefined) parts.push(<strong key={`b${i++}`}>{m[1]}</strong>)
    else parts.push(<span key={`h${i++}`} data-hashtag style={{ color: 'var(--mantine-color-blue-6)' }}>{m[2]}</span>)
    last = m.index! + m[0].length
  }
  if (last < line.length) parts.push(line.slice(last))
  return <Fragment key={key}>{parts}</Fragment>
}

export function MaxPostPreview({ text, imageUrl, tooLong, length, max }: {
  text: string; imageUrl?: string | null; tooLong: boolean; length: number; max: number
}) {
  const paragraphs = text.split(/\n{2,}/)
  return (
    <Box>
      {tooLong && <Alert color="red" mb="sm">{`Пост длиннее ${max} символов (${length})`}</Alert>}
      <Paper withBorder radius="lg" p={0} maw={420} style={{ overflow: 'hidden', background: 'var(--mantine-color-default)' }}>
        {imageUrl && <Image src={imageUrl} alt="" mah={280} fit="cover" />}
        <Box p="sm">
          {paragraphs.map((p, pi) => (
            <Text key={pi} size="sm" mb={pi < paragraphs.length - 1 ? 'xs' : 0} style={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
              {p.split('\n').map((line, li, lines) => (
                <Fragment key={li}>{renderLine(line, li)}{li < lines.length - 1 && '\n'}</Fragment>
              ))}
            </Text>
          ))}
        </Box>
      </Paper>
      <Text size="xs" c={tooLong ? 'red' : 'dimmed'} mt={4}>{`${length} / ${max}`}</Text>
    </Box>
  )
}
