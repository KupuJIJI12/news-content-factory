import { ApiError, apiFetch } from './client'
import type {
  LlmProvider, LlmProviderInput, LlmTask, LlmTaskSetting, PromptTask, PromptVersion, Secrets, Usage,
} from './types'

const S = '/api/settings'
export const getSecrets = () => apiFetch<Secrets>(`${S}/secrets`)
export const getProviders = () => apiFetch<LlmProvider[]>(`${S}/llm/providers`)
export const createProvider = (input: LlmProviderInput) => apiFetch<LlmProvider>(`${S}/llm/providers`, { method: 'POST', json: input })
export const updateProvider = (id: number, input: LlmProviderInput) =>
  apiFetch<LlmProvider>(`${S}/llm/providers/${id}`, { method: 'PUT', json: input })
export const getLlmTasks = () => apiFetch<LlmTaskSetting[]>(`${S}/llm/tasks`)
export const putLlmTask = (task: LlmTask, input: { providerId: number; model: string; temperature: number }) =>
  apiFetch<LlmTaskSetting>(`${S}/llm/tasks/${task}`, { method: 'PUT', json: input })
export const getPrompts = () => apiFetch<PromptTask[]>(`${S}/prompts`)
export const createPrompt = (task: LlmTask, text: string) => apiFetch<PromptVersion>(`${S}/prompts/${task}`, { method: 'POST', json: { text } })
export const activatePrompt = (id: number) => apiFetch<void>(`${S}/prompts/${id}/activate`, { method: 'PUT' })
export const getUsage = () => apiFetch<Usage>(`${S}/llm/usage`)
export const putBudget = (dailyTokenBudget: number) => apiFetch<void>(`${S}/llm/budget`, { method: 'PUT', json: { dailyTokenBudget } })

/** Per-field messages from a validation problem ({ errors: { field: [msg] } }), first message per field. */
export function fieldErrors(err: unknown): Record<string, string> {
  if (!(err instanceof ApiError)) return {}
  const errors = (err.extra.errors ?? {}) as Record<string, string[]>
  return Object.fromEntries(Object.entries(errors).map(([k, v]) => [k, v[0] ?? '']))
}
