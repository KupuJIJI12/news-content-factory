import { apiFetch } from './client'

export interface CollectionState { paused: boolean }

export const getCollection = () => apiFetch<CollectionState>('/api/collection')
export const setCollectionPaused = (paused: boolean) =>
  apiFetch<CollectionState>('/api/collection', { method: 'PUT', json: { paused } }).then(() => undefined)
