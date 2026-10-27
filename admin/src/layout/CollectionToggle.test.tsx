import { beforeEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderWithProviders } from '../test/render'
import * as api from '../api/collection'
import { CollectionToggle } from './CollectionToggle'

vi.mock('../api/collection', async (o) => ({ ...(await o<typeof api>()), setCollectionPaused: vi.fn() }))

describe('CollectionToggle', () => {
  beforeEach(() => {
    vi.mocked(api.setCollectionPaused).mockReset()
    vi.mocked(api.setCollectionPaused).mockImplementation(async () => undefined)
  })

  it('pausing asks for confirmation and calls api', async () => {
    renderWithProviders(<CollectionToggle paused={false} />)
    await userEvent.click(screen.getByRole('switch', { name: 'Сбор новостей' }))
    expect(api.setCollectionPaused).not.toHaveBeenCalled()
    await userEvent.click(await screen.findByRole('button', { name: 'Поставить на паузу' }))
    await waitFor(() => expect(api.setCollectionPaused).toHaveBeenCalledWith(true))
  })

  it('resuming does not ask', async () => {
    renderWithProviders(<CollectionToggle paused />)
    await userEvent.click(screen.getByRole('switch', { name: 'Сбор новостей' }))
    await waitFor(() => expect(api.setCollectionPaused).toHaveBeenCalledWith(false))
  })
})
