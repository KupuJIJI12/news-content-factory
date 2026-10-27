import { Badge } from '@mantine/core'

export function ScoreBadge({ score }: { score: number | null }) {
  if (score === null) return <Badge color="gray" variant="outline">—</Badge>
  const color = score >= 8 ? 'green' : score >= 6 ? 'yellow' : 'gray'
  return <Badge color={color} variant="filled" miw={32}>{score}</Badge>
}
