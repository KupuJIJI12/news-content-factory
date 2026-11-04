import { Fragment, type ReactNode } from 'react'
import { Alert, Box, Image, Text } from '@mantine/core'
import { tokens } from '../design/tokens'

/**
 * Renders the subset of Max markdown our posts use: **bold**, [text](https://link), line breaks, "• " bullets, hashtags.
 * Everything else is text; only http(s) links become links.
 */
function renderLine(line: string, key: number): ReactNode {
  const parts: ReactNode[] = []
  const pattern = /\*\*(.+?)\*\*|\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)|(#[\p{L}\p{N}_]+)/gu
  let last = 0
  let i = 0
  for (const m of line.matchAll(pattern)) {
    if (m.index! > last) parts.push(line.slice(last, m.index))
    if (m[1] !== undefined) parts.push(<strong key={`b${i++}`}>{m[1]}</strong>)
    else if (m[2] !== undefined)
      parts.push(<a key={`l${i++}`} href={m[3]} target="_blank" rel="noreferrer" style={{ color: tokens.link }}>{m[2]}</a>)
    else parts.push(<span key={`h${i++}`} data-hashtag style={{ color: tokens.link }}>{m[4]}</span>)
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
      {tooLong && <Alert color="brick" mb="sm">{`Пост длиннее ${max} символов (${length})`}</Alert>}
      <div style={{ maxWidth: 420, overflow: 'hidden', background: tokens.surface, border: `1px solid ${tokens.lineSoft}`, borderRadius: 12 }}>
        {imageUrl && <Image src={imageUrl} alt="" mah={280} fit="cover" />}
        <Box px={16} py={14}>
          {paragraphs.map((p, pi) => (
            <Text key={pi} size="sm" lh={1.55} mb={pi < paragraphs.length - 1 ? 10 : 0} style={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
              {p.split('\n').map((line, li, lines) => (
                <Fragment key={li}>{renderLine(line, li)}{li < lines.length - 1 && '\n'}</Fragment>
              ))}
            </Text>
          ))}
        </Box>
      </div>
      <Text size="xs" c={tooLong ? 'brick.6' : 'ink.6'} mt={6}>{`${length} из ${max} символов`}</Text>
    </Box>
  )
}
