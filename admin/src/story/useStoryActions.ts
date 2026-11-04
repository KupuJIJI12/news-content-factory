import { useQueryClient } from '@tanstack/react-query'
import { notifications } from '@mantine/notifications'
import { ApiError } from '../api/client'
import { countersKey } from '../api/counters'
import { storyKey } from '../api/stories'

/** Refreshes everything a story action can change and reports errors uniformly. */
export function useStoryRefresh(storyId: number) {
  const queryClient = useQueryClient()
  return async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: storyKey(storyId) }),
      queryClient.invalidateQueries({ queryKey: ['stories'] }),
      queryClient.invalidateQueries({ queryKey: countersKey }),
    ])
  }
}

export function notifyError(err: unknown) {
  notifications.show({ color: 'red', message: err instanceof ApiError ? err.message : 'Сервер недоступен' })
}

export function notifyOk(message: string) {
  notifications.show({ color: 'green', message })
}
