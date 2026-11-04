import { useQuery } from '@tanstack/react-query'
import { apiFetch } from './client'
import type { Me } from './types'

export const login = (login: string, password: string) =>
  apiFetch<Me>('/api/auth/login', { method: 'POST', json: { login, password } })
export const logout = () => apiFetch<void>('/api/auth/logout', { method: 'POST' })
export const me = () => apiFetch<Me>('/api/auth/me')

export const meKey = ['me'] as const
export const useMe = () => useQuery({ queryKey: meKey, queryFn: me, retry: false, staleTime: 5 * 60_000 })
