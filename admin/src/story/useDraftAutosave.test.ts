import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { useDraftAutosave } from './useDraftAutosave'

const value = { title: 'Несохранённый', body: 'Текст в работе', hashtags: ['#Отрадный'] }

describe('useDraftAutosave', () => {
  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
    sessionStorage.clear()
  })

  it('restores unsaved text after remount', () => {
    vi.useFakeTimers()
    const first = renderHook(() => useDraftAutosave(5, value, true))
    act(() => { vi.advanceTimersByTime(600) })
    first.unmount()
    const second = renderHook(() => useDraftAutosave(5, null, false))
    expect(second.result.current.restore()).toEqual(value)
  })

  it('clear removes the saved draft', () => {
    vi.useFakeTimers()
    const { result } = renderHook(() => useDraftAutosave(6, value, true))
    act(() => { vi.advanceTimersByTime(600) })
    act(() => result.current.clear())
    expect(result.current.restore()).toBeNull()
  })

  it('survives storage throwing', () => {
    vi.useFakeTimers()
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('denied') })
    const { result } = renderHook(() => useDraftAutosave(7, value, true))
    act(() => { vi.advanceTimersByTime(600) })
    expect(result.current.restore()).toBeNull()
  })
})
