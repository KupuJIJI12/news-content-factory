import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderWithProviders } from '../test/render'
import { ApiError } from '../api/client'
import * as api from '../api/settings'
import type { PromptTask } from '../api/types'
import { PromptSettings } from './PromptSettings'

vi.mock('../api/settings', async (o) => ({ ...(await o<typeof api>()), getPrompts: vi.fn(), createPrompt: vi.fn(), activatePrompt: vi.fn() }))
vi.mock('../api/auth', () => ({ useMe: () => ({ data: { id: 1, login: 'admin', timeZone: 'Europe/Samara' } }) }))

const prompts: PromptTask[] = [{
  task: 'Rewrite', variables: ['city', 'sources'],
  versions: [
    { id: 2, version: 2, isActive: true, createdAt: '2026-10-25T10:00:00Z', text: 'Перепиши для {{city}}: {{sources}}' },
    { id: 1, version: 1, isActive: false, createdAt: '2026-10-20T10:00:00Z', text: 'Старый промпт {{sources}}' },
  ],
}]

describe('PromptSettings', () => {
  beforeEach(() => {
    vi.mocked(api.getPrompts).mockImplementation(async () => prompts)
    vi.mocked(api.createPrompt).mockReset()
    vi.mocked(api.activatePrompt).mockReset()
  })

  it('shows unknown variable error from server', async () => {
    vi.mocked(api.createPrompt).mockImplementation(async () => {
      throw new ApiError(400, 'Неизвестная переменная {{town}}', 'validation', { errors: { text: ['Неизвестная переменная {{town}}'] } })
    })
    renderWithProviders(<PromptSettings />)
    const editor = await screen.findByLabelText(/Новая версия/)
    fireEvent.change(editor, { target: { value: 'Текст про {{town}}' } })
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить как новую версию' }))
    await waitFor(() => expect(api.createPrompt).toHaveBeenCalledWith('Rewrite', 'Текст про {{town}}'))
    expect(await screen.findByText('Неизвестная переменная {{town}}')).toBeInTheDocument()
  })

  it('activating older version calls api', async () => {
    vi.mocked(api.activatePrompt).mockImplementation(async () => undefined)
    renderWithProviders(<PromptSettings />)
    await userEvent.click(await screen.findByRole('button', { name: 'Сделать активной' }))
    await waitFor(() => expect(api.activatePrompt).toHaveBeenCalledWith(1))
  })
})
