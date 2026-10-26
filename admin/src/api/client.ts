/** Error from the admin API: HTTP status plus the problem+json "code" and any extra fields. */
export class ApiError extends Error {
  readonly status: number
  readonly code?: string
  readonly extra: Record<string, unknown>

  constructor(status: number, message: string, code?: string, extra: Record<string, unknown> = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.extra = extra
  }
}

let onUnauthorized: (() => void) | null = null

/** Called on any 401 except the login request itself (e.g. to redirect to /login). */
export function setOnUnauthorized(handler: (() => void) | null) {
  onUnauthorized = handler
}

export type ApiInit = RequestInit & { json?: unknown }

export async function apiFetch<T>(path: string, init: ApiInit = {}): Promise<T> {
  const { json, headers, ...rest } = init
  const h = new Headers(headers)
  let body = rest.body
  if (json !== undefined) {
    h.set('content-type', 'application/json')
    body = JSON.stringify(json)
  }
  h.set('accept', 'application/json')

  const response = await fetch(path, { ...rest, body, headers: h, credentials: 'include' })
  if (response.status === 204) return undefined as T

  const isJson = (response.headers.get('content-type') ?? '').includes('json')
  const payload = isJson ? await response.json().catch(() => undefined) : undefined

  if (!response.ok) {
    if (response.status === 401 && path !== '/api/auth/login') onUnauthorized?.()
    const { title, code, type: _type, status: _status, detail, traceId: _traceId, ...extra } =
      (payload ?? {}) as Record<string, unknown>
    const message = (title as string) || (detail as string) ||
      (response.status === 413 ? 'Файл слишком большой' : `Ошибка ${response.status}`)
    throw new ApiError(response.status, message, code as string | undefined, extra)
  }
  return payload as T
}
