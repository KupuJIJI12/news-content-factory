import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderWithProviders } from '../test/render'
import { ApiError } from '../api/client'
import * as api from '../api/sources'
import * as cities from '../api/cities'
import type { City } from '../api/types'
import { SourceForm } from './SourceForm'

vi.mock('../api/sources', async (o) => ({
  ...(await o<typeof api>()), createSource: vi.fn(), updateSource: vi.fn(), testSource: vi.fn(), autodetectSource: vi.fn(),
}))
vi.mock('../api/auth', () => ({ useMe: () => ({ data: { id: 1, login: 'admin', timeZone: 'Europe/Samara' } }) }))
vi.mock('../api/cities', async (o) => ({ ...(await o<typeof cities>()), listCities: vi.fn() }))

const city = (id: number, name: string): City => ({
  id, name, slug: name, timeZone: 'Europe/Samara', keywords: [name.toLowerCase()], scoreThreshold: 6, sourcesCount: 0, channel: null,
})

const set = (label: RegExp, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } })

describe('SourceForm', () => {
  beforeEach(() => {
    vi.mocked(api.createSource).mockReset()
    vi.mocked(api.testSource).mockReset()
    vi.mocked(cities.listCities).mockImplementation(async () => [city(1, 'Отрадный')])
  })

  it('builds html config json from selector fields', async () => {
    vi.mocked(api.createSource).mockImplementation(async (i) => ({ ...i, id: 9 }) as never)
    renderWithProviders(<SourceForm source={null} onSaved={() => {}} />)
    await userEvent.click(screen.getByText('HTML'))
    await userEvent.click(screen.getByRole('button', { name: 'Ручная настройка' }))
    set(/Название/, 'Вестник')
    set(/^Адрес/, 'https://vestnik.example/')
    set(/Страница списка/, 'https://vestnik.example/news')
    set(/Селектор элемента/, '.news-item')
    set(/Селектор ссылки/, 'a.title')
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(api.createSource).toHaveBeenCalled())
    const input = vi.mocked(api.createSource).mock.calls[0][0]
    expect(input.type).toBe('Html')
    expect(JSON.parse(input.config!)).toEqual({ listUrl: 'https://vestnik.example/news', itemSelector: '.news-item', linkSelector: 'a.title' })
  })

  it('shows server validation error on field', async () => {
    vi.mocked(api.createSource).mockImplementation(async () => {
      throw new ApiError(400, 'Проверьте поля источника', 'validation', { errors: { url: ['Нужен полный адрес http(s)://…'] } })
    })
    renderWithProviders(<SourceForm source={null} onSaved={() => {}} />)
    set(/Название/, 'X')
    set(/^Адрес/, 'nope')
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }))
    expect(await screen.findByText('Нужен полный адрес http(s)://…')).toBeInTheDocument()
  })

  it('test button renders extracted items', async () => {
    vi.mocked(api.testSource).mockImplementation(async () => ({
      ok: true, error: null,
      items: [{ title: 'Отключат воду', url: 'https://x/1', publishedAt: '2026-10-26T07:00:00Z', textPreview: 'В связи с ремонтом', imageCount: 2 }],
    }))
    renderWithProviders(<SourceForm source={null} onSaved={() => {}} />)
    set(/Название/, 'X')
    set(/^Адрес/, 'https://x/rss')
    await userEvent.click(screen.getByRole('button', { name: 'Проверить' }))
    expect(await screen.findByText('Отключат воду')).toBeInTheDocument()
    expect(screen.getByText(/26\.10 11:00/)).toBeInTheDocument()
    expect(screen.getByText(/Фото: 2/)).toBeInTheDocument()
  })

  it('sends the chosen city with its dedicated flag', async () => {
    vi.mocked(cities.listCities).mockImplementation(async () => [city(1, 'Отрадный'), city(2, 'Кинель')])
    vi.mocked(api.createSource).mockImplementation(async (i) => ({ ...i, id: 9 }) as never)
    renderWithProviders(<SourceForm source={null} onSaved={() => {}} />)
    set(/Название/, 'Кинель-Информ')
    set(/^Адрес/, 'https://kinel.example/rss')
    await userEvent.click(await screen.findByRole('radio', { name: 'Кинель' }))
    await userEvent.click(screen.getByRole('switch', { name: 'Все новости источника — про Кинель' }))
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(api.createSource).toHaveBeenCalled())
    expect(vi.mocked(api.createSource).mock.calls[0][0].cities).toEqual([{ cityId: 2, isDedicated: true }])
  })

  it('preselects the only city', async () => {
    vi.mocked(api.createSource).mockImplementation(async (i) => ({ ...i, id: 9 }) as never)
    renderWithProviders(<SourceForm source={null} onSaved={() => {}} />)
    expect(await screen.findByRole('radio', { name: 'Отрадный' })).toBeChecked()
  })

  it('autodetect fills the form and shows found items', async () => {
    vi.mocked(api.autodetectSource).mockImplementation(async () => ({
      ok: true, method: 'Heuristic', type: 'Html', url: 'https://vestnik.example/news/',
      config: '{"listUrl":"https://vestnik.example/news/","itemSelector":".item","linkSelector":"a"}',
      items: [{ title: 'Отключат воду в среду', url: 'https://vestnik.example/news/1', publishedAt: null, textPreview: 'Текст', imageCount: 0 }],
      message: 'Список новостей определён по структуре страницы',
    }))
    vi.mocked(api.createSource).mockImplementation(async (i) => ({ ...i, id: 9 }) as never)
    renderWithProviders(<SourceForm source={null} onSaved={() => {}} />)
    set(/Сайт для автонастройки/, 'https://vestnik.example/news/')
    await userEvent.click(screen.getByRole('button', { name: 'Настроить автоматически' }))
    expect(await screen.findByText('Отключат воду в среду')).toBeInTheDocument()
    expect(screen.getByText(/Определено по структуре страницы/)).toBeInTheDocument()
    expect(api.autodetectSource).toHaveBeenCalledWith('https://vestnik.example/news/')

    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }))
    await waitFor(() => expect(api.createSource).toHaveBeenCalled())
    const input = vi.mocked(api.createSource).mock.calls[0][0]
    expect([input.type, input.url, input.name]).toEqual(['Html', 'https://vestnik.example/news/', 'vestnik.example'])
    expect(JSON.parse(input.config!)).toEqual({ listUrl: 'https://vestnik.example/news/', itemSelector: '.item', linkSelector: 'a' })
  })

  it('autodetect failure opens manual setup', async () => {
    vi.mocked(api.autodetectSource).mockImplementation(async () => ({
      ok: false, method: null, type: null, url: null, config: null, items: [],
      message: 'Не удалось определить список новостей автоматически — настройте селекторы вручную',
    }))
    renderWithProviders(<SourceForm source={null} onSaved={() => {}} />)
    set(/Сайт для автонастройки/, 'https://card.example/')
    await userEvent.click(screen.getByRole('button', { name: 'Настроить автоматически' }))
    expect(await screen.findByText(/настройте селекторы вручную/)).toBeInTheDocument()
    expect(screen.getByLabelText(/Страница списка/)).toHaveValue('https://card.example/')
  })
})
