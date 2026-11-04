import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, apiFetch, setOnUnauthorized } from './client'

function mockFetch(status: number, body?: unknown, contentType = 'application/json') {
  const fn = vi.fn().mockResolvedValue(
    new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { 'content-type': contentType } }),
  )
  vi.stubGlobal('fetch', fn)
  return fn
}

describe('apiFetch', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    setOnUnauthorized(null)
  })

  it('parses problem+json into ApiError with code and extra', async () => {
    mockFetch(409, { title: 'Лимит', code: 'daily_limit_exceeded', limit: 4, published: 4 }, 'application/problem+json')
    await expect(apiFetch('/api/x')).rejects.toMatchObject({
      status: 409,
      code: 'daily_limit_exceeded',
      message: 'Лимит',
      extra: { limit: 4, published: 4 },
    })
  })

  it('maps an empty 413 from the server to a readable message', async () => {
    mockFetch(413, undefined, 'text/plain')
    await expect(apiFetch('/api/stories/1/images', { method: 'POST' })).rejects.toMatchObject({ status: 413, message: 'Файл слишком большой' })
  })

  it('calls onUnauthorized on 401', async () => {
    mockFetch(401, { code: 'x' }, 'application/problem+json')
    const spy = vi.fn()
    setOnUnauthorized(spy)
    await expect(apiFetch('/api/stories')).rejects.toBeInstanceOf(ApiError)
    expect(spy).toHaveBeenCalledTimes(1)
  })

  it('does not call onUnauthorized for the login request itself', async () => {
    mockFetch(401, { code: 'invalid_credentials' }, 'application/problem+json')
    const spy = vi.fn()
    setOnUnauthorized(spy)
    await expect(apiFetch('/api/auth/login', { method: 'POST', json: {} })).rejects.toBeInstanceOf(ApiError)
    expect(spy).not.toHaveBeenCalled()
  })

  it('sends credentials and json body', async () => {
    const fn = mockFetch(200, { ok: true })
    const result = await apiFetch<{ ok: boolean }>('/api/x', { method: 'POST', json: { a: 1 } })
    expect(result.ok).toBe(true)
    const init = fn.mock.calls[0][1] as RequestInit
    expect(init.credentials).toBe('include')
    expect(new Headers(init.headers).get('content-type')).toBe('application/json')
    expect(init.body).toBe('{"a":1}')
  })

  it('returns undefined for 204', async () => {
    mockFetch(204)
    await expect(apiFetch('/api/x', { method: 'POST' })).resolves.toBeUndefined()
  })
})
