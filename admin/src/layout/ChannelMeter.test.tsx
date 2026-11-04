import { describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import { renderWithProviders } from '../test/render'
import type { Counters } from '../api/types'
import { ChannelMeter, pageTitle } from './ChannelMeter'

const counters = (today: Counters['today']): Counters => ({
  onReview: 3, withUpdates: 1, collectionPaused: false, today,
  errors: { sourcesFailing: 1, sourcesSilent: 1, storiesProcessingFailed: 0, storiesPublishFailed: 0, rawItemsFailed24h: 0, budgetExceeded: false, total: 2 },
})

describe('ChannelMeter', () => {
  it('shows today usage of the only channel', () => {
    renderWithProviders(<ChannelMeter counters={counters([{ channelId: 1, name: 'Max', published: 2, scheduled: 1, limit: 4, cityId: 1, cityName: 'Отрадный' }])} />)
    expect(screen.getByText('В канале сегодня')).toBeInTheDocument()
    expect(screen.getByText('2 из 4')).toBeInTheDocument()
    expect(screen.getByLabelText('В канале сегодня 2 из 4, ещё 1 в плане')).toBeInTheDocument()
  })

  it('names the city when there are several channels', () => {
    renderWithProviders(<ChannelMeter counters={counters([
      { channelId: 1, name: 'Отрадный — Max', published: 1, scheduled: 0, limit: 4, cityId: 1, cityName: 'Отрадный' },
      { channelId: 2, name: 'Кинель — Max', published: 0, scheduled: 0, limit: 3, cityId: 2, cityName: 'Кинель' },
    ])} />)
    expect(screen.getByText('Кинель')).toBeInTheDocument()
    expect(screen.getByLabelText('Отрадный: 1 из 4')).toBeInTheDocument()
  })

  it('page title shows review count only when there is something to review', () => {
    expect(pageTitle(3)).toBe('(3) Контент завод')
    expect(pageTitle(0)).toBe('Контент завод')
  })
})
