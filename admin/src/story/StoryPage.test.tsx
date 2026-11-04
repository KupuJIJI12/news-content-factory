import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router'
import { renderWithProviders } from '../test/render'
import * as stories from '../api/stories'
import * as counters from '../api/counters'
import { draft, story } from '../test/fixtures'
import { StoryPage } from './StoryPage'

vi.mock('../api/stories', async (o) => ({ ...(await o<typeof stories>()), getStory: vi.fn(), preview: vi.fn() }))
vi.mock('../api/counters', async (o) => ({ ...(await o<typeof counters>()), getCounters: vi.fn() }))
vi.mock('../api/auth', () => ({ useMe: () => ({ data: { id: 1, login: 'admin', timeZone: 'Europe/Samara' } }) }))

const page = () => renderWithProviders(<Routes><Route path="/stories/:id" element={<StoryPage />} /></Routes>, { route: '/stories/5' })

describe('StoryPage', () => {
  beforeEach(() => {
    vi.mocked(stories.getStory).mockImplementation(async () => story())
    vi.mocked(stories.preview).mockImplementation(async () => [])
    vi.mocked(counters.getCounters).mockImplementation(async () => ({
      onReview: 1, withUpdates: 0,
      errors: { sourcesFailing: 0, sourcesSilent: 0, storiesProcessingFailed: 0, storiesPublishFailed: 0, total: 0 }, today: [],
    }) as never)
  })
  afterEach(() => sessionStorage.clear())

  it('offers to restore edits left over from an expired session', async () => {
    sessionStorage.setItem('draft:5', JSON.stringify({ title: 'Недописанный заголовок', body: 'b', hashtags: [] }))
    page()
    await userEvent.click(await screen.findByRole('button', { name: 'Восстановить' }))
    expect(screen.getByLabelText('Заголовок')).toHaveValue('Недописанный заголовок')
  })

  it('tells the moderator about a newer server version while editing', async () => {
    const { client } = page()
    const title = await screen.findByLabelText('Заголовок')
    fireEvent.change(title, { target: { value: 'Моя правка' } })
    vi.mocked(stories.getStory).mockImplementation(async () =>
      story({ currentDraft: draft({ id: 11, version: 2, title: 'Версия от LLM' }) }))
    await act(() => client.invalidateQueries({ queryKey: ['story', 5] }))
    expect(screen.getByLabelText('Заголовок')).toHaveValue('Моя правка')
    await userEvent.click(await screen.findByRole('button', { name: 'Показать новую' }))
    await waitFor(() => expect(screen.getByLabelText('Заголовок')).toHaveValue('Версия от LLM'))
  })

  it('shows hint when channel is not configured', async () => {
    vi.mocked(stories.preview).mockImplementation(async () => [{
      channelId: 0, channelName: 'Max', platform: 'Max', text: '**Заголовок**', length: 13, max: 4000, tooLong: false, channelConfigured: false,
    }])
    page()
    expect(await screen.findByText(/Канал Max не настроен/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Настроить канал' })).toHaveAttribute('href', '/settings')
  })

  it('shows the daily limit of the story city only', async () => {
    vi.mocked(counters.getCounters).mockImplementation(async () => ({
      onReview: 1, withUpdates: 0, collectionPaused: false,
      errors: { sourcesFailing: 0, sourcesSilent: 0, storiesProcessingFailed: 0, storiesPublishFailed: 0, rawItemsFailed24h: 0, budgetExceeded: false, total: 0 },
      today: [{ channelId: 3, name: 'Кинель — Max', cityId: 2, cityName: 'Кинель', published: 4, scheduled: 0, limit: 4 }],
    }))
    page()
    expect(await screen.findByLabelText('Заголовок')).toBeInTheDocument()
    expect(screen.queryByText(/Сегодня 4\/4/)).not.toBeInTheDocument()
    expect(screen.queryByText(/Дневной лимит будет превышен/)).not.toBeInTheDocument()
  })
})
