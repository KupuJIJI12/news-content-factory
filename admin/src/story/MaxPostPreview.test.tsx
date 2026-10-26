import { describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import { renderWithProviders } from '../test/render'
import { MaxPostPreview } from './MaxPostPreview'

describe('MaxPostPreview', () => {
  it('renders bold, bullets and hashtags', () => {
    renderWithProviders(<MaxPostPreview text={'**Заголовок**\n\n• раз\n\n#Отрадный #ЖКХ'} tooLong={false} length={30} max={4000} />)
    expect(screen.getByText('Заголовок').tagName).toBe('STRONG')
    expect(screen.getByText('• раз')).toBeInTheDocument()
    expect(screen.getByText('#Отрадный')).toHaveAttribute('data-hashtag')
  })

  it('escapes html from text', () => {
    const { container } = renderWithProviders(
      <MaxPostPreview text={'<img src=x onerror=alert(1)> **жирно**'} tooLong={false} length={10} max={4000} />)
    expect(container.querySelectorAll('img')).toHaveLength(0)
    expect(screen.getByText(/<img src=x/)).toBeInTheDocument()
  })

  it('shows image when given', () => {
    const { container } = renderWithProviders(
      <MaxPostPreview text="Текст" imageUrl="/api/images/1" tooLong={false} length={5} max={4000} />)
    expect(container.querySelectorAll('img')).toHaveLength(1)
  })

  it('shows too-long warning', () => {
    renderWithProviders(<MaxPostPreview text="..." tooLong length={4100} max={4000} />)
    expect(screen.getByText('Пост длиннее 4000 символов (4100)')).toBeInTheDocument()
  })
})
