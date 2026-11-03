import { useEffect, useState } from 'react'
import { Center, Loader, Pagination, Stack, Tabs, Text, Tooltip } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { IconBell } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useNavigate, useSearchParams } from 'react-router'
import { listStories, PAGE_SIZE, storiesKey, storyCounts, storyCountsKey } from '../api/stories'
import type { QueueTab, StoryListItem } from '../api/types'
import { CityTag } from '../design/CityTag'
import { ScoreNumber, ScoreRail } from '../design/ScoreRail'
import { tokens } from '../design/tokens'
import { CityFilter } from '../lib/CityFilter'
import { formatInCity } from '../lib/cityTime'
import { plural } from '../lib/format'
import { QueueSidePanel } from './QueueSidePanel'

const TABS: { value: QueueTab; label: string }[] = [
  { value: 'Review', label: 'На проверке' },
  { value: 'LowInterest', label: 'Низкий интерес' },
  { value: 'Scheduled', label: 'Запланировано' },
  { value: 'Published', label: 'Опубликовано' },
  { value: 'Rejected', label: 'Отклонено' },
  { value: 'Errors', label: 'Ошибки' },
]

const isTab = (v: string | null): v is QueueTab => TABS.some((t) => t.value === v)

export function QueuePage() {
  const [params, setParams] = useSearchParams()
  const tab: QueueTab = isTab(params.get('tab')) ? (params.get('tab') as QueueTab) : 'Review'
  const page = Math.max(1, Number(params.get('page') ?? 1) || 1)
  const cityId = Number(params.get('city')) || null
  // Wide screens decide from the side panel; narrow ones open the story page.
  const wide = useMediaQuery('(min-width: 75em)') ?? false
  const navigate = useNavigate()
  const query = useQuery({
    queryKey: storiesKey(tab, page, cityId),
    queryFn: () => (cityId ? listStories(tab, (page - 1) * PAGE_SIZE, PAGE_SIZE, cityId) : listStories(tab, (page - 1) * PAGE_SIZE, PAGE_SIZE)),
  })
  const counts = useQuery({ queryKey: storyCountsKey(cityId), queryFn: () => (cityId ? storyCounts(cityId) : storyCounts()) })
  const setQuery = (next: Record<string, string>) => setParams({ ...(cityId ? { city: String(cityId) } : {}), ...next })

  const items = query.data?.items ?? []
  const [selected, setSelected] = useState<number | null>(null)
  const selectedId = items.some((s) => s.id === selected) ? selected : items[0]?.id ?? null
  useEffect(() => { setSelected(null) }, [tab, cityId])

  const choose = (s: StoryListItem) => (wide ? setSelected(s.id) : navigate(`/stories/${s.id}`))
  const when = (s: StoryListItem) =>
    formatInCity(tab === 'Scheduled' && s.scheduledAt ? s.scheduledAt : s.updatedAt, s.timeZone)
  const sources = (n: number) => `${n} ${plural(n, 'источник', 'источника', 'источников')}`

  return (
    <Stack gap="md">
      <CityFilter value={cityId} onChange={(id) => setParams({ ...(id ? { city: String(id) } : {}), tab })} />
      <Tabs value={tab} onChange={(v) => setQuery(v ? { tab: v } : {})} keepMounted={false} color="ink">
        <Tabs.List style={{ overflowX: 'auto', flexWrap: 'nowrap', borderColor: tokens.line }}>
          {TABS.map((t) => (
            <Tabs.Tab key={t.value} value={t.value} c={t.value === tab ? 'ink.8' : 'ink.6'}
              rightSection={counts.data && (
                <span className="nf-num" style={{
                  fontWeight: 600, color: t.value === 'Errors' && counts.data.Errors > 0 ? tokens.brick : 'inherit',
                }}>
                  {counts.data[t.value]}
                </span>
              )}>
              {t.label}
            </Tabs.Tab>
          ))}
        </Tabs.List>
      </Tabs>

      <div style={{ display: 'flex', gap: 20, alignItems: 'flex-start' }}>
        <section aria-label="Очередь" style={{ flex: 1, minWidth: 0 }}>
          {query.isPending ? (
            <Center p="xl"><Loader /></Center>
          ) : query.isError ? (
            <Text c="brick.6">Не удалось загрузить очередь: {query.error.message}</Text>
          ) : items.length === 0 ? (
            <Center p="xl"><Text c="ink.6">Здесь пока пусто</Text></Center>
          ) : (
            <div style={{ background: tokens.surface, border: `1px solid ${tokens.line}`, borderRadius: 8, overflow: 'hidden' }}>
              {items.map((s, i) => {
                const active = wide && s.id === selectedId
                return (
                  <button key={s.id} type="button" onClick={() => choose(s)} aria-current={active ? 'true' : undefined} style={{
                    all: 'unset', boxSizing: 'border-box', width: '100%', cursor: 'pointer',
                    display: 'grid', gridTemplateColumns: '6px 34px minmax(0, 1fr) auto', alignItems: 'center', gap: 14,
                    padding: '14px 16px', background: active ? '#E8EDF2' : tokens.surface,
                    borderBottom: i < items.length - 1 ? `1px solid ${tokens.lineSoft}` : 0,
                  }}>
                    <ScoreRail score={s.score} />
                    <ScoreNumber score={s.score} />
                    <span style={{ display: 'flex', flexDirection: 'column', gap: 4, minWidth: 0 }}>
                      <span style={{ fontWeight: 600, fontSize: 15, lineHeight: 1.35, color: tokens.ink }}>{s.title}</span>
                      <span style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 8, fontSize: 13, color: tokens.muted }}>
                        <CityTag name={s.cityName} />
                        <span>{sources(s.sourcesCount)}</span>
                        {s.hasUpdates && (
                          <Tooltip label="Появились новые подробности">
                            <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4, color: tokens.amberInk }}>
                              <IconBell size={14} aria-label="Есть обновления" />
                              новые подробности
                            </span>
                          </Tooltip>
                        )}
                      </span>
                    </span>
                    <span style={{ fontSize: 13, color: tokens.muted, whiteSpace: 'nowrap' }}>{when(s)}</span>
                  </button>
                )
              })}
            </div>
          )}

          {query.data && query.data.total > PAGE_SIZE && (
            <Pagination mt="md" total={Math.ceil(query.data.total / PAGE_SIZE)} value={page}
              onChange={(p) => setQuery({ tab, page: String(p) })} />
          )}
        </section>

        {wide && selectedId !== null && (
          <div style={{ flex: '0 0 400px' }}>
            <QueueSidePanel key={selectedId} storyId={selectedId} />
          </div>
        )}
      </div>
    </Stack>
  )
}
