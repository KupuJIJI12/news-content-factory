import { beforeEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderWithProviders } from '../test/render'
import * as stories from '../api/stories'
import { story } from '../test/fixtures'
import { ImagePicker } from './ImagePicker'

vi.mock('../api/stories', async (o) => ({ ...(await o<typeof stories>()), uploadImage: vi.fn() }))

describe('ImagePicker', () => {
  beforeEach(() => {
    vi.mocked(stories.uploadImage).mockReset()
  })

  it('refuses a file over 15 MB without uploading it', async () => {
    renderWithProviders(<ImagePicker story={story()} selectedImageId={null} onSelect={() => {}} />)
    const big = new File([new Uint8Array(16 * 1024 * 1024)], 'big.jpg', { type: 'image/jpeg' })
    const input = document.querySelector('input[type="file"]') as HTMLInputElement
    await userEvent.upload(input, big)
    expect(await screen.findByText('Картинка больше 15 МБ')).toBeInTheDocument()
    expect(stories.uploadImage).not.toHaveBeenCalled()
  })
})
