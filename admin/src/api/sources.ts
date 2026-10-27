import { apiFetch } from './client'
import type { SourceDto, SourceInput, SourceTestResult } from './types'

export const sourcesKey = ['sources'] as const
export const listSources = (cityId?: number | null) => apiFetch<SourceDto[]>(`/api/sources${cityId ? `?cityId=${cityId}` : ''}`)
export const createSource = (input: SourceInput) => apiFetch<SourceDto>('/api/sources', { method: 'POST', json: input })
export const updateSource = (id: number, input: SourceInput) => apiFetch<SourceDto>(`/api/sources/${id}`, { method: 'PUT', json: input })
export const testSource = (input: SourceInput) => apiFetch<SourceTestResult>('/api/sources/test', { method: 'POST', json: input })
