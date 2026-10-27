import { describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import { renderWithProviders } from '../test/render'
import type { Counters } from '../api/types'
import { CountersBar, pageTitle } from './CountersBar'

const counters: Counters = {
  onReview: 3,
  withUpdates: 1,
  errors: { sourcesFailing: 1, sourcesSilent: 1, storiesProcessingFailed: 0, storiesPublishFailed: 0, rawItemsFailed24h: 0,
    budgetExceeded: false, total: 2 },
  today: [{ channelId: 1, name: 'Max', published: 2, scheduled: 1, limit: 4, cityId: 1, cityName: 'Отрадный' }],
  collectionPaused: false,
}

describe('CountersBar', () => {
  it('renders all counters and today usage', () => {
    renderWithProviders(<CountersBar counters={counters} compact={false} />)
    expect(screen.getByText('На проверке 3')).toBeInTheDocument()
    expect(screen.getByText('Обновления 1')).toBeInTheDocument()
    expect(screen.getByText('Ошибки 2')).toBeInTheDocument()
    expect(screen.getByText('Сегодня 2/4 (+1)')).toBeInTheDocument()
  })

  it('compact mode shows single badge with review count', () => {
    renderWithProviders(<CountersBar counters={counters} compact />)
    expect(screen.getByLabelText('На проверке: 3')).toBeInTheDocument()
    expect(screen.queryByText('Обновления 1')).not.toBeInTheDocument()
  })

  it('page title shows review count only when there is something to review', () => {
    expect(pageTitle(3)).toBe('(3) NewsFactory')
    expect(pageTitle(0)).toBe('NewsFactory')
  })

  it('shows paused badge', () => {
    renderWithProviders(<CountersBar counters={{ ...counters, collectionPaused: true }} compact={false} />)
    expect(screen.getByText('Сбор на паузе')).toBeInTheDocument()
  })
})
