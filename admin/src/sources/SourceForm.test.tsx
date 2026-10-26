import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderWithProviders } from '../test/render'
import { ApiError } from '../api/client'
import * as api from '../api/sources'
import { SourceForm } from './SourceForm'

vi.mock('../api/sources', async (o) => ({ ...(await o<typeof api>()), createSource: vi.fn(), updateSource: vi.fn(), testSource: vi.fn() }))
vi.mock('../api/auth', () => ({ useMe: () => ({ data: { id: 1, login: 'admin', timeZone: 'Europe/Samara' } }) }))

const set = (label: RegExp, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } })

describe('SourceForm', () => {
  beforeEach(() => {
    vi.mocked(api.createSource).mockReset()
    vi.mocked(api.testSource).mockReset()
  })

  it('builds html config json from selector fields', async () => {
    vi.mocked(api.createSource).mockImplementation(async (i) => ({ ...i, id: 9 }) as never)
    renderWithProviders(<SourceForm source={null} onSaved={() => {}} />)
    await userEvent.click(screen.getByText('HTML'))
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
})
