import { beforeEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderWithProviders } from '../test/render'
import type { StoryListItem, StoryListPage } from '../api/types'
import * as stories from '../api/stories'
import * as cities from '../api/cities'
import { QueuePage } from './QueuePage'

vi.mock('../api/stories', async (o) => ({ ...(await o<typeof stories>()), listStories: vi.fn(), storyCounts: vi.fn() }))
vi.mock('../api/auth', () => ({ useMe: () => ({ data: { id: 1, login: 'admin', timeZone: 'Europe/Samara' } }) }))
vi.mock('../api/cities', async (o) => ({ ...(await o<typeof cities>()), listCities: vi.fn() }))

const item = (over: Partial<StoryListItem> = {}): StoryListItem => ({
  id: 1, status: 'ReadyForReview', score: 9, title: 'Без воды 15 ноября', sourcesCount: 2, hasUpdates: false,
  updatedAt: '2026-10-26T10:00:00Z', scheduledAt: null, cityId: 1, cityName: 'Отрадный', timeZone: 'Europe/Samara', ...over,
})

function respond(page: StoryListPage) {
  vi.mocked(stories.listStories).mockImplementation(async () => page)
}

describe('QueuePage', () => {
  beforeEach(() => {
    vi.mocked(stories.listStories).mockReset()
    vi.mocked(cities.listCities).mockImplementation(async () => [
      { id: 1, name: 'Отрадный', slug: 'otradny', timeZone: 'Europe/Samara', keywords: [], scoreThreshold: 6, sourcesCount: 1, channel: null },
    ])
    vi.mocked(stories.storyCounts).mockImplementation(async () =>
      ({ Review: 3, LowInterest: 12, Scheduled: 1, Published: 40, Rejected: 0, Errors: 2 }))
  })

  it('renders review items with score badge and updates icon', async () => {
    respond({ items: [item(), item({ id: 2, score: 6, title: 'Второй', hasUpdates: true })], total: 2 })
    renderWithProviders(<QueuePage />)
    expect(await screen.findByText('Без воды 15 ноября')).toBeInTheDocument()
    expect(screen.getByText('9')).toBeInTheDocument()
    expect(screen.getAllByText('2 источника').length).toBeGreaterThan(0)
    expect(screen.getByLabelText('Есть обновления')).toBeInTheDocument()
  })

  it('switches tab via query string and requests that tab', async () => {
    respond({ items: [], total: 0 })
    renderWithProviders(<QueuePage />, { route: '/?tab=Scheduled' })
    await waitFor(() => expect(stories.listStories).toHaveBeenCalledWith('Scheduled', 0, 50))
    await userEvent.click(screen.getByRole('tab', { name: 'Отклонено' }))
    await waitFor(() => expect(stories.listStories).toHaveBeenCalledWith('Rejected', 0, 50))
  })

  it('shows empty state', async () => {
    respond({ items: [], total: 0 })
    renderWithProviders(<QueuePage />)
    expect(await screen.findByText('Здесь пока пусто')).toBeInTheDocument()
  })

  it('scheduled tab shows publication time in city zone', async () => {
    respond({ items: [item({ status: 'Scheduled', scheduledAt: '2026-10-26T21:30:00Z' })], total: 1 })
    renderWithProviders(<QueuePage />, { route: '/?tab=Scheduled' })
    expect(await screen.findByText(/27\.10 01:30/)).toBeInTheDocument()
  })

  it('shows counts on every tab', async () => {
    respond({ items: [item()], total: 3 })
    renderWithProviders(<QueuePage />)
    expect(await screen.findByRole('tab', { name: /Низкий интерес\s*12/ })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /Запланировано\s*1/ })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /Ошибки\s*2/ })).toBeInTheDocument()
  })

  it('filters by city', async () => {
    vi.mocked(cities.listCities).mockImplementation(async () => [
      { id: 1, name: 'Отрадный', slug: 'otradny', timeZone: 'Europe/Samara', keywords: [], scoreThreshold: 6, sourcesCount: 1, channel: null },
      { id: 2, name: 'Кинель', slug: 'kinel', timeZone: 'Europe/Samara', keywords: [], scoreThreshold: 6, sourcesCount: 1, channel: null },
    ])
    respond({ items: [item()], total: 1 })
    renderWithProviders(<QueuePage />)
    await userEvent.click(await screen.findByRole('textbox', { name: 'Город' }))
    await userEvent.click(await screen.findByRole('option', { name: 'Кинель' }))
    await waitFor(() => expect(stories.listStories).toHaveBeenCalledWith('Review', 0, 50, 2))
    await waitFor(() => expect(stories.storyCounts).toHaveBeenCalledWith(2))
    expect(screen.getAllByText('Отрадный').length).toBeGreaterThan(0) // city badge in the row
  })
})
