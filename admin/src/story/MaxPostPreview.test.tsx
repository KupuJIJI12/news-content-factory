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

  it('renders the channel link as a link', () => {
    renderWithProviders(<MaxPostPreview text={'**T**\n\nТекст\n\n[Отрадный Live в Max](https://max.ru/otradny)'} tooLong={false} length={10} max={4000} />)
    const link = screen.getByRole('link', { name: 'Отрадный Live в Max' })
    expect(link).toHaveAttribute('href', 'https://max.ru/otradny')
    expect(screen.queryByText(/\]\(/)).not.toBeInTheDocument()
  })

  it('does not turn a non-http link into a link', () => {
    renderWithProviders(<MaxPostPreview text={'[жми](javascript:alert(1))'} tooLong={false} length={10} max={4000} />)
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
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
