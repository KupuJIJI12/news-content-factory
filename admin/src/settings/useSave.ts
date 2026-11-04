import { useState } from 'react'
import { useQueryClient, type QueryKey } from '@tanstack/react-query'
import { notifications } from '@mantine/notifications'
import { ApiError } from '../api/client'
import { fieldErrors } from '../api/settings'

/** Runs a settings save: field errors for validation problems, a toast otherwise, then refetch. */
export function useSave(queryKey: QueryKey) {
  const queryClient = useQueryClient()
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  async function save(action: () => Promise<unknown>, ok = 'Сохранено') {
    setBusy(true)
    setErrors({})
    try {
      await action()
      notifications.show({ color: 'green', message: ok })
      await queryClient.invalidateQueries({ queryKey })
      return true
    } catch (err) {
      setErrors(fieldErrors(err))
      notifications.show({ color: 'red', message: err instanceof ApiError ? err.message : 'Сервер недоступен' })
      return false
    } finally {
      setBusy(false)
    }
  }
  return { save, errors, busy }
}
