import type { DraftDto, StoryDetail } from '../api/types'

export const draft = (over: Partial<DraftDto> = {}): DraftDto => ({
  id: 10, version: 1, title: 'Без воды 16 ноября', body: 'На улице Гагарина 16 ноября отключат воду.', hashtags: ['#Отрадный'],
  byHuman: false, regenerationHint: null, factCheckIssues: [], createdAt: '2026-10-26T10:00:00Z', isCurrent: true, ...over,
})

export const story = (over: Partial<StoryDetail> = {}): StoryDetail => ({
  id: 5, status: 'ReadyForReview', score: 8, scoreReason: 'касается жителей', summary: 'Отключение воды', hasUpdates: false,
  lastError: null,
  sources: [{ rawItemId: 7, sourceName: 'otradny.org', url: 'https://otradny.org/n/1', title: 'Отключение воды',
    text: '15 ноября на улице Гагарина не будет воды.', publishedAt: '2026-10-26T08:00:00Z',
    images: [{ url: 'https://otradny.org/i/1.jpg', allowed: false }] }],
  currentDraft: draft(), versions: [draft()], images: [], publications: [], ...over,
})
