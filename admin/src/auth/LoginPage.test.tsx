import { describe, expect, it, vi, beforeEach } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderWithProviders } from '../test/render'
import { ApiError } from '../api/client'
import { LoginPage } from './LoginPage'
import * as auth from '../api/auth'

vi.mock('../api/auth', async (importOriginal) => ({ ...(await importOriginal<typeof auth>()), login: vi.fn() }))

async function submit() {
  await userEvent.type(screen.getByLabelText(/Логин/), 'admin')
  await userEvent.type(screen.getByLabelText(/Пароль/), 'x')
  await userEvent.click(screen.getByRole('button', { name: 'Войти' }))
}

describe('LoginPage', () => {
  beforeEach(() => {
    vi.mocked(auth.login).mockClear()
  })

  it('shows rate-limit message on 429', async () => {
    vi.mocked(auth.login).mockImplementation(async () => { throw new ApiError(429, 'Too many') })
    renderWithProviders(<LoginPage />, { route: '/login' })
    await submit()
    expect(await screen.findByText('Слишком много попыток, подождите минуту')).toBeInTheDocument()
  })

  it('shows wrong credentials message on 401', async () => {
    vi.mocked(auth.login).mockImplementation(async () => { throw new ApiError(401, 'Неверный логин или пароль', 'invalid_credentials') })
    renderWithProviders(<LoginPage />, { route: '/login' })
    await submit()
    expect(await screen.findByText('Неверный логин или пароль')).toBeInTheDocument()
  })

  it('logs in with entered credentials', async () => {
    vi.mocked(auth.login).mockResolvedValue({ id: 1, login: 'admin', timeZone: 'Europe/Samara' })
    renderWithProviders(<LoginPage />, { route: '/login?next=/sources' })
    await submit()
    expect(auth.login).toHaveBeenCalledWith('admin', 'x')
  })
})
