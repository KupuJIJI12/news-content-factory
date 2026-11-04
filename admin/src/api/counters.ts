import { apiFetch } from './client'
import type { Counters } from './types'

export const countersKey = ['counters'] as const
export const getCounters = () => apiFetch<Counters>('/api/counters')
