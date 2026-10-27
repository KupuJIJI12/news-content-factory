import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderWithProviders } from '../test/render'
import * as cities from '../api/cities'
import * as settings from '../api/settings'
import type { City } from '../api/types'
import { CitiesSettings } from './CitiesSettings'

vi.mock('../api/cities', async (o) => ({
  ...(await o<typeof cities>()), listCities: vi.fn(), createCity: vi.fn(), updateCity: vi.fn(), putCityChannel: vi.fn(),
}))
vi.mock('../api/settings', async (o) => ({ ...(await o<typeof settings>()), getSecrets: vi.fn() }))

const otradny: City = {
  id: 1, name: 'Отрадный', slug: 'otradny', timeZone: 'Europe/Samara', keywords: ['отрадн'], scoreThreshold: 6, sourcesCount: 7,
  channel: { id: 3, name: 'Отрадный — Max', platform: 'Max', externalChatId: '', dailyLimit: 4, isEnabled: false, cityId: 1, cityName: 'Отрадный' },
}

describe('CitiesSettings', () => {
  beforeEach(() => {
    vi.mocked(cities.listCities).mockImplementation(async () => [otradny])
    vi.mocked(cities.createCity).mockReset()
    vi.mocked(cities.putCityChannel).mockReset()
    vi.mocked(settings.getSecrets).mockImplementation(async () =>
      ({ maxBotToken: true, vkServiceToken: false, gigachatAuthKey: true, yandexApiKey: false }))
  })

  it('creates a city', async () => {
    vi.mocked(cities.createCity).mockImplementation(async (i) => ({ ...otradny, ...i, id: 2, slug: 'kinel', sourcesCount: 0 }))
    renderWithProviders(<CitiesSettings />)
    await userEvent.click(await screen.findByRole('button', { name: 'Добавить город' }))
    fireEvent.change(await screen.findByLabelText(/Название города/), { target: { value: 'Кинель' } })
    await userEvent.type(screen.getByRole('textbox', { name: /Ключевые слова/ }), 'кинел{enter}')
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить город' }))
    await waitFor(() => expect(cities.createCity).toHaveBeenCalledWith(
      { name: 'Кинель', timeZone: 'Europe/Samara', keywords: ['кинел'], scoreThreshold: 6 }))
  })

  it('saves the channel of a city', async () => {
    vi.mocked(cities.putCityChannel).mockImplementation(async () => otradny.channel!)
    renderWithProviders(<CitiesSettings />)
    await userEvent.click(await screen.findByText('Отрадный'))
    expect(await screen.findByText(/канал выключен/i)).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText(/ID чата/), { target: { value: '-100' } })
    await userEvent.click(screen.getByRole('checkbox', { name: /Публиковать в этот канал/ }))
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить канал' }))
    await waitFor(() => expect(cities.putCityChannel).toHaveBeenCalledWith(1,
      { name: 'Отрадный — Max', externalChatId: '-100', dailyLimit: 4, isEnabled: true }))
  })

  it('keeps an unsaved chat id when the list refetches', async () => {
    const { client } = renderWithProviders(<CitiesSettings />)
    await userEvent.click(await screen.findByText('Отрадный'))
    fireEvent.change(await screen.findByLabelText(/ID чата/), { target: { value: '-555' } })
    // e.g. the window regained focus after copying the id from Max
    vi.mocked(cities.listCities).mockImplementation(async () => [{ ...otradny, sourcesCount: 99, channel: { ...otradny.channel! } }])
    await act(() => client.invalidateQueries({ queryKey: ['cities'] }))
    expect(await screen.findByText(/Источников: 99/)).toBeInTheDocument()
    expect(screen.getByLabelText(/ID чата/)).toHaveValue('-555')
  })
})
