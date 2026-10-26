import { useCallback, useEffect } from 'react'
import type { DraftInput } from '../api/types'

const key = (storyId: number) => `draft:${storyId}`

/** Keeps unsaved editor fields in sessionStorage (debounced) so an expired session or reload does not lose them. */
export function useDraftAutosave(storyId: number, value: DraftInput | null, dirty: boolean) {
  useEffect(() => {
    if (!dirty || !value) return
    const t = setTimeout(() => {
      try { sessionStorage.setItem(key(storyId), JSON.stringify(value)) } catch { /* storage unavailable */ }
    }, 500)
    return () => clearTimeout(t)
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
