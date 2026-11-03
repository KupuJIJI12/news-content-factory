import { useState } from 'react'
import { ActionIcon, Center, Loader, Pagination, Stack, Tabs, Text, Tooltip } from '@mantine/core'
import { IconBell, IconTrash } from '@tabler/icons-react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'
import { deleteStory, listStories, PAGE_SIZE, storiesKey, storyCounts, storyCountsKey } from '../api/stories'
import type { QueueTab, StoryListItem } from '../api/types'
import { CityTag } from '../design/CityTag'
import { ConfirmDelete } from '../design/ConfirmDelete'
import { ScoreNumber, ScoreRail } from '../design/ScoreRail'
import { tokens } from '../design/tokens'
import { CityFilter } from '../lib/CityFilter'
import { formatInCity } from '../lib/cityTime'
import { plural } from '../lib/format'
import { notifyError, notifyOk } from '../story/useStoryActions'

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
  const query = useQuery({
    queryKey: storiesKey(tab, page, cityId),
    queryFn: () => (cityId ? listStories(tab, (page - 1) * PAGE_SIZE, PAGE_SIZE, cityId) : listStories(tab, (page - 1) * PAGE_SIZE, PAGE_SIZE)),
  })
  const counts = useQuery({ queryKey: storyCountsKey(cityId), queryFn: () => (cityId ? storyCounts(cityId) : storyCounts()) })
  const setQuery = (next: Record<string, string>) => setParams({ ...(cityId ? { city: String(cityId) } : {}), ...next })

  const items = query.data?.items ?? []
  const queryClient = useQueryClient()
  const [toDelete, setToDelete] = useState<StoryListItem | null>(null)
  const [deleting, setDeleting] = useState(false)
  const canDelete = tab === 'Rejected'

  async function remove(s: StoryListItem) {
    setDeleting(true)
    try {
      await deleteStory(s.id)
      notifyOk('Сюжет удалён')
      setToDelete(null)
      await queryClient.invalidateQueries({ queryKey: ['stories'] }) // the list and the tab counts
    } catch (err) {
      notifyError(err)
    } finally {
      setDeleting(false)
    }
  }
  const when = (s: StoryListItem) =>
    formatInCity(tab === 'Scheduled' && s.scheduledAt ? s.scheduledAt : s.updatedAt, s.timeZone)
  // Who reported it: two names fit on a row, the rest is a count. Older API answers without names fall back to the count.
  const sources = (s: StoryListItem) => {
    const names = s.sourceNames ?? []
    if (names.length === 0) return `${s.sourcesCount} ${plural(s.sourcesCount, 'источник', 'источника', 'источников')}`
    const rest = names.length - 2
    return names.slice(0, 2).join(', ') + (rest > 0 ? ` и ещё ${rest}` : '')
  }

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

      <section aria-label="Очередь">
        {query.isPending ? (
          <Center p="xl"><Loader /></Center>
        ) : query.isError ? (
          <Text c="brick.6">Не удалось загрузить очередь: {query.error.message}</Text>
        ) : items.length === 0 ? (
          <Center p="xl"><Text c="ink.6">Здесь пока пусто</Text></Center>
        ) : (
          <div style={{ background: tokens.surface, border: `1px solid ${tokens.line}`, borderRadius: 8, overflow: 'hidden' }}>
            {items.map((s, i) => (
              <div key={s.id} className="nf-row" style={{
                display: 'flex', alignItems: 'center', borderBottom: i < items.length - 1 ? `1px solid ${tokens.lineSoft}` : 0,
              }}>
                <Link to={`/stories/${s.id}`} style={{
                  flex: 1, minWidth: 0, display: 'grid', gridTemplateColumns: '6px 34px minmax(0, 1fr) auto', alignItems: 'center', gap: 14,
                  padding: '14px 16px', textDecoration: 'none', color: 'inherit',
                }}>
                  <ScoreRail score={s.score} />
                  <ScoreNumber score={s.score} />
                  <span style={{ display: 'flex', flexDirection: 'column', gap: 4, minWidth: 0 }}>
                    <span style={{ fontWeight: 600, fontSize: 15, lineHeight: 1.35, color: tokens.ink }}>{s.title}</span>
                    <span style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 8, fontSize: 13, color: tokens.muted }}>
                      <CityTag name={s.cityName} />
                      <span title={(s.sourceNames ?? []).join(', ')}>{sources(s)}</span>
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
                </Link>
                {canDelete && (
                  <Tooltip label="Удалить">
                    <ActionIcon variant="subtle" color="brick" size="lg" mr={10} aria-label={`Удалить «${s.title}»`}
                      onClick={() => setToDelete(s)}>
                      <IconTrash size={18} />
                    </ActionIcon>
                  </Tooltip>
                )}
              </div>
            ))}
          </div>
        )}

        {query.data && query.data.total > PAGE_SIZE && (
          <Pagination mt="md" total={Math.ceil(query.data.total / PAGE_SIZE)} value={page}
            onChange={(p) => setQuery({ tab, page: String(p) })} />
        )}
      </section>
      <ConfirmDelete opened={toDelete !== null} busy={deleting} onClose={() => setToDelete(null)}
        onConfirm={() => toDelete && remove(toDelete)} title="Удалить сюжет?"
        text={`«${toDelete?.title ?? ''}» пропадёт из «Отклонено». Те же новости не вернутся в очередь: собранные статьи остаются в базе, чтобы их не собирать повторно.`} />
    </Stack>
  )
}
