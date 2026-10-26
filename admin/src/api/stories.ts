import { apiFetch } from './client'
import type {
  DraftDto, DraftInput, PreviewInput, PreviewResult, PublishInput, PublishResponse, QueueTab, StoryDetail, StoryImageDto,
  StoryListPage,
} from './types'

export const PAGE_SIZE = 50
export const storiesKey = (tab: QueueTab, page: number) => ['stories', tab, page] as const
export const storyKey = (id: number) => ['story', id] as const

export const listStories = (tab: QueueTab, skip: number, take: number) =>
  apiFetch<StoryListPage>(`/api/stories?tab=${tab}&skip=${skip}&take=${take}`)
export const getStory = (id: number) => apiFetch<StoryDetail>(`/api/stories/${id}`)
export const saveDraft = (id: number, input: DraftInput) =>
  apiFetch<DraftDto>(`/api/stories/${id}/draft`, { method: 'PUT', json: input })
export const regenerate = (id: number, hint: string | null) =>
  apiFetch<void>(`/api/stories/${id}/regenerate`, { method: 'POST', json: { hint } })
export const generate = (id: number) => apiFetch<void>(`/api/stories/${id}/generate`, { method: 'POST' })
export const retry = (id: number) => apiFetch<void>(`/api/stories/${id}/retry`, { method: 'POST' })
export const reject = (id: number) => apiFetch<void>(`/api/stories/${id}/reject`, { method: 'POST' })
export const ackUpdates = (id: number) => apiFetch<void>(`/api/stories/${id}/ack-updates`, { method: 'POST' })
export const publish = (id: number, input: PublishInput) =>
  apiFetch<PublishResponse>(`/api/stories/${id}/publish`, { method: 'POST', json: input })
export const cancel = (id: number) => apiFetch<void>(`/api/stories/${id}/cancel`, { method: 'POST' })
export const preview = (input: PreviewInput) => apiFetch<PreviewResult[]>('/api/preview', { method: 'POST', json: input })

export function uploadImage(id: number, file: File, credit: string | null) {
  const form = new FormData()
  form.append('file', file)
  if (credit) form.append('credit', credit)
  return apiFetch<StoryImageDto>(`/api/stories/${id}/images`, { method: 'POST', body: form })
}
export const imageFromSource = (id: number, rawItemId: number, url: string, acknowledgeNoPermission: boolean) =>
  apiFetch<StoryImageDto>(`/api/stories/${id}/images/from-source`, {
    method: 'POST', json: { rawItemId, url, acknowledgeNoPermission },
  })
export const deleteImage = (id: number, imageId: number) =>
  apiFetch<void>(`/api/stories/${id}/images/${imageId}`, { method: 'DELETE' })
