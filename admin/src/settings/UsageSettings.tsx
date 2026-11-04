import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button, Group, NumberInput, Progress, SimpleGrid, Paper, Stack, Text } from '@mantine/core'
import { getUsage, putBudget } from '../api/settings'
import { useSave } from './useSave'

const key = ['settings', 'usage'] as const
const MIN_BUDGET = 1000
const fmt = (n: number) => n.toLocaleString('ru-RU')

export function UsageSettings() {
  const { data } = useQuery({ queryKey: key, queryFn: getUsage, refetchInterval: 60_000 })
  const [budget, setBudget] = useState<number>(MIN_BUDGET)
  const { save, busy } = useSave(key)
  useEffect(() => { if (data) setBudget(data.dailyBudget) }, [data])
  if (!data) return null
  const pct = data.dailyBudget > 0 ? Math.min(100, (data.today / data.dailyBudget) * 100) : 0
  return (
    <Stack gap="md" maw={560}>
      <SimpleGrid cols={2}>
        <Paper withBorder p="sm"><Text size="xs" c="dimmed">Токенов сегодня</Text><Text fw={700} size="xl">{fmt(data.today)}</Text></Paper>
        <Paper withBorder p="sm"><Text size="xs" c="dimmed">За месяц</Text><Text fw={700} size="xl">{fmt(data.month)}</Text></Paper>
      </SimpleGrid>
      <Stack gap={4}>
        <Text size="sm">{`Использовано ${Math.round(pct)}% дневного бюджета`}</Text>
        <Progress value={pct} color={pct >= 90 ? 'red' : pct >= 70 ? 'orange' : 'blue'} />
      </Stack>
      <Group align="flex-end">
        <NumberInput label="Дневной бюджет, токенов" description={`не меньше ${fmt(MIN_BUDGET)}; при превышении обработка ждёт следующих суток`}
          min={MIN_BUDGET} step={10000} thousandSeparator=" " value={budget} onChange={(v) => setBudget(Number(v) || MIN_BUDGET)}
          error={budget < MIN_BUDGET ? `Не меньше ${fmt(MIN_BUDGET)}` : undefined} style={{ flex: 1 }} />
        <Button loading={busy} disabled={budget < MIN_BUDGET} onClick={() => save(() => putBudget(budget))}>Сохранить</Button>
      </Group>
    </Stack>
  )
}
