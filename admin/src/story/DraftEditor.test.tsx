import { describe, expect, it, vi } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderWithProviders } from '../test/render'
import * as stories from '../api/stories'
import { draft, story } from '../test/fixtures'
import { DraftEditor } from './DraftEditor'

vi.mock('../api/stories', async (o) => ({ ...(await o<typeof stories>()), regenerate: vi.fn(), saveDraft: vi.fn() }))

const value = { title: 'Без воды 16 ноября', body: 'На улице Гагарина 16 ноября отключат воду.', hashtags: ['#Отрадный'] }

describe('DraftEditor', () => {
  it('highlights fact-check claims in text', () => {
    const s = story({ currentDraft: draft({ factCheckIssues: [{ claim: '16 ноября', problem: 'Нет в источниках', source: 'Deterministic' }] }) })
    renderWithProviders(<DraftEditor story={s} value={value} onChange={() => {}} dirty={false} onSaved={() => {}} />)
    expect(screen.getAllByText('16 ноября', { selector: 'mark' }).length).toBeGreaterThan(0)
    expect(screen.getByText('Нет в источниках')).toBeInTheDocument()
  })

  it('confirms regenerate when there are unsaved edits', async () => {
    vi.mocked(stories.regenerate).mockImplementation(async () => undefined)
    renderWithProviders(<DraftEditor story={story()} value={value} onChange={() => {}} dirty onSaved={() => {}} />)
    await userEvent.type(screen.getByLabelText('Пожелание к новой версии'), 'короче')
    await userEvent.click(screen.getByRole('button', { name: 'Перегенерировать' }))
    expect(stories.regenerate).not.toHaveBeenCalled()
    await userEvent.click(await screen.findByRole('button', { name: 'Перегенерировать всё равно' }))
    await waitFor(() => expect(stories.regenerate).toHaveBeenCalledWith(5, 'короче'))
  })

  it('confirmed regenerate discards unsaved edits', async () => {
    vi.mocked(stories.regenerate).mockImplementation(async () => undefined)
    const onDiscard = vi.fn()
    renderWithProviders(<DraftEditor story={story()} value={value} onChange={() => {}} dirty onSaved={() => {}} onDiscard={onDiscard} />)
    await userEvent.click(screen.getByRole('button', { name: 'Перегенерировать' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Перегенерировать всё равно' }))
    await waitFor(() => expect(onDiscard).toHaveBeenCalled())
  })
})
