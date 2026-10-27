import { useCallback, useEffect } from 'react'
import type { DraftInput } from '../api/types'

const key = (storyId: number) => `draft:${storyId}`

/** Keeps unsaved editor fields in sessionStorage so an expired session or reload does not lose them. */
export function useDraftAutosave(storyId: number, value: DraftInput | null, dirty: boolean) {
  // Written on every change, not debounced: a 401 redirect or navigation can follow a keystroke immediately.
  useEffect(() => {
    if (!dirty || !value) return
    try { sessionStorage.setItem(key(storyId), JSON.stringify(value)) } catch { /* storage unavailable */ }
  }, [storyId, value, dirty])

  const restore = useCallback((): DraftInput | null => {
    try {
      const raw = sessionStorage.getItem(key(storyId))
      return raw ? (JSON.parse(raw) as DraftInput) : null
    } catch {
      return null
    }
  }, [storyId])

  const clear = useCallback(() => {
    try { sessionStorage.removeItem(key(storyId)) } catch { /* ignore */ }
  }, [storyId])

  return { restore, clear }
}
