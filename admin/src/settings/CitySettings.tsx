import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button, Group, NumberInput, Stack, TagsInput, Text, TextInput } from '@mantine/core'
import { getCity, putCity } from '../api/settings'
import { useSave } from './useSave'

const key = ['settings', 'city'] as const

export function CitySettings() {
  const { data } = useQuery({ queryKey: key, queryFn: getCity })
  const [name, setName] = useState('')
  const [keywords, setKeywords] = useState<string[]>([])
  const [threshold, setThreshold] = useState(6)
  const { save, errors, busy } = useSave(key)

  useEffect(() => {
    if (!data) return
    setName(data.name)
    setKeywords(data.keywords)
    setThreshold(data.scoreThreshold)
  }, [data])

  if (!data) return null
  return (
    <Stack gap="sm" maw={560}>
      <TextInput label="Город" value={name} onChange={(e) => setName(e.currentTarget.value)} error={errors.name} />
      <TagsInput label="Ключевые слова" description="Enter — добавить слово; новости без них отсеиваются (кроме источников «только про наш город»)"
        value={keywords} onChange={setKeywords} error={errors.keywords} />
      <NumberInput label="Порог интереса" description="Сюжеты с оценкой ниже уходят во вкладку «Низкий интерес»" min={1} max={10}
        value={threshold} onChange={(v) => setThreshold(Number(v) || 1)} error={errors.scoreThreshold} />
      <Text size="xs" c="dimmed">{`Часовой пояс: ${data.timeZone}`}</Text>
      <Group justify="flex-end">
        <Button loading={busy} onClick={() => save(() => putCity({ name: name.trim(), keywords, scoreThreshold: threshold }))}>Сохранить</Button>
      </Group>
    </Stack>
  )
}
