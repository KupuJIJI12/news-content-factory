import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderWithProviders } from '../test/render'
import { ApiError } from '../api/client'
import * as stories from '../api/stories'
import { story } from '../test/fixtures'
import { PublishPanel } from './PublishPanel'

vi.mock('../api/stories', async (o) => ({ ...(await o<typeof stories>()), publish: vi.fn(), reject: vi.fn(), cancel: vi.fn() }))

const today = (published: number, scheduled: number, limit = 4) => ({ channelId: 1, name: 'Max', published, scheduled, limit })

describe('PublishPanel', () => {
  beforeEach(() => {
    vi.mocked(stories.publish).mockReset()
  })

  it('warns before click when limit would be exceeded', () => {
    renderWithProviders(<PublishPanel story={story()} previewTooLong={false} selectedImageId={null} tz="Europe/Samara" today={today(3, 1)} />)
    expect(screen.getByText(/Дневной лимит будет превышен/)).toBeInTheDocument()
  })

  it('asks to override on 409 and retries with overrideLimit', async () => {
    let call = 0
    vi.mocked(stories.publish).mockImplementation(async () => {
      if (call++ === 0) throw new ApiError(409, 'Лимит', 'daily_limit_exceeded', { published: 4, scheduled: 0, limit: 4 })
      return { publicationIds: [1] }
    })
    renderWithProviders(<PublishPanel story={story()} previewTooLong={false} selectedImageId={3} tz="Europe/Samara" today={today(2, 0)} />)
    await userEvent.click(screen.getByRole('button', { name: 'Опубликовать сейчас' }))
    expect(await screen.findByText(/Дневной лимит исчерпан \(опубликовано 4, запланировано 0, лимит 4\)/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Опубликовать всё равно' }))
    await waitFor(() => expect(stories.publish).toHaveBeenCalledTimes(2))
    expect(vi.mocked(stories.publish).mock.calls[1]).toEqual([5, { overrideLimit: true, imageId: 3 }])
  })

  it('disables publish when preview is too long', () => {
    renderWithProviders(<PublishPanel story={story()} previewTooLong selectedImageId={null} tz="Europe/Samara" today={today(0, 0)} />)
    expect(screen.getByRole('button', { name: 'Опубликовать сейчас' })).toBeDisabled()
  })

  it('schedules using city time zone', async () => {
    vi.mocked(stories.publish).mockImplementation(async () => ({ publicationIds: [1] }))
    renderWithProviders(<PublishPanel story={story()} previewTooLong={false} selectedImageId={null} tz="Europe/Samara" today={today(0, 0)} />)
    fireEvent.change(screen.getByLabelText(/Дата и время/), { target: { value: '2030-01-15T09:00' } })
    await userEvent.click(screen.getByRole('button', { name: 'Запланировать' }))
    await waitFor(() => expect(stories.publish).toHaveBeenCalledWith(5, { at: '2030-01-15T09:00:00+04:00', overrideLimit: false }))
  })

  it('cannot schedule in the past', () => {
    renderWithProviders(<PublishPanel story={story()} previewTooLong={false} selectedImageId={null} tz="Europe/Samara" today={today(0, 0)} />)
    fireEvent.change(screen.getByLabelText(/Дата и время/), { target: { value: '2020-01-01T09:00' } })
    expect(screen.getByRole('button', { name: 'Запланировать' })).toBeDisabled()
  })

  it('scheduled story offers only cancel', () => {
    renderWithProviders(<PublishPanel story={story({ status: 'Scheduled' })} previewTooLong={false} selectedImageId={null} tz="Europe/Samara" />)
    expect(screen.getByRole('button', { name: 'Отменить публикацию' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Опубликовать сейчас' })).not.toBeInTheDocument()
  })
})
