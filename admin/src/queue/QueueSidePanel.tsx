import { useState } from 'react'
import { Button, Center, Group, Loader, Stack, Text } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { getStory, preview, reject, storyKey } from '../api/stories'
import type { StoryDetail } from '../api/types'
import { CityTag } from '../design/CityTag'
import { ScoreNumber } from '../design/ScoreRail'
import { tokens } from '../design/tokens'
import { MaxPostPreview } from '../story/MaxPostPreview'
import { usePublish } from '../story/usePublish'
import { notifyError, notifyOk, useStoryRefresh } from '../story/useStoryActions'

function Actions({ story }: { story: StoryDetail }) {
  const pub = usePublish(story)
  const refresh = useStoryRefresh(story.id)
  const [busy, setBusy] = useState(false)
  const reviewable = story.status === 'ReadyForReview'

  async function doReject() {
    setBusy(true)
    try {
      await reject(story.id)
      notifyOk('Сюжет отклонён')
      await refresh()
    } catch (err) {
      notifyError(err)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Group gap="xs" wrap="wrap">
      {reviewable && story.currentDraft && (
        <Button loading={pub.busy} disabled={busy} onClick={() => pub.request({ overrideLimit: false })}>Опубликовать сейчас</Button>
      )}
      <Button component={Link} to={`/stories/${story.id}`} variant="default">
        {reviewable ? 'Править и запланировать' : 'Открыть сюжет'}
      </Button>
      {reviewable && (
        <Button variant="subtle" color="brick" loading={busy} disabled={pub.busy} onClick={doReject}>Отклонить</Button>
      )}
      {pub.dialogs}
    </Group>
  )
}

/** Decide from the queue: the selected story's interest, the post as it will look, and the decision buttons. */
export function QueueSidePanel({ storyId }: { storyId: number }) {
  const { data: story, isLoading, isError } = useQuery({ queryKey: storyKey(storyId), queryFn: () => getStory(storyId) })
  const draft = story?.currentDraft ?? null
  const { data: previews } = useQuery({
    queryKey: ['preview', draft?.title, draft?.body, draft?.hashtags, story?.cityId],
    queryFn: () => preview({ title: draft!.title, body: draft!.body, hashtags: draft!.hashtags, cityId: story!.cityId }),
    enabled: !!draft,
  })
  const max = previews?.[0]

  return (
    <aside aria-label="Выбранный сюжет" style={{
      background: tokens.surface, border: `1px solid ${tokens.line}`, borderRadius: 8, padding: 20, position: 'sticky', top: 76,
    }}>
      {isLoading ? <Center h={120}><Loader size="sm" /></Center>
        : isError || !story ? <Text c="ink.6" size="sm">Не удалось загрузить сюжет.</Text>
          : (
            <Stack gap="md">
              <Group gap="md" wrap="nowrap" align="center">
                <ScoreNumber score={story.score} size={40} />
                <Stack gap={4}>
                  <CityTag name={story.cityName} />
                  {story.scoreReason && <Text size="sm" c="ink.6" lh={1.35}>{story.scoreReason}</Text>}
                </Stack>
              </Group>
              {draft && max
                ? <MaxPostPreview text={max.text} tooLong={max.tooLong} length={max.length} max={max.max} />
                : <Text size="sm" c="ink.6">{draft ? 'Готовлю превью…' : 'Черновика пока нет.'}</Text>}
              {draft && (draft.factCheckIssues.length === 0
                ? <Text size="sm" c="teal.7">Проверка фактов: расхождений с источником нет</Text>
                : <Text size="sm" c="amber.9">{`Замечаний проверки фактов: ${draft.factCheckIssues.length}. Посмотрите их в сюжете.`}</Text>)}
              <Actions story={story} />
            </Stack>
          )}
    </aside>
  )
}
