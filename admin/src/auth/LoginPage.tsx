import { useState } from 'react'
import { Alert, Button, Center, Paper, PasswordInput, Stack, TextInput, Title } from '@mantine/core'
import { useQueryClient } from '@tanstack/react-query'
import { useNavigate, useSearchParams } from 'react-router'
import { ApiError } from '../api/client'
import { login, meKey } from '../api/auth'

export function LoginPage() {
  const [user, setUser] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  async function submit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    setBusy(true)
    try {
      const me = await login(user, password)
      queryClient.setQueryData(meKey, me)
      const next = params.get('next')
      navigate(next && next.startsWith('/') ? next : '/', { replace: true })
    } catch (err) {
      if (err instanceof ApiError && err.status === 429) setError('Слишком много попыток, подождите минуту')
      else if (err instanceof ApiError && err.status === 401) setError('Неверный логин или пароль')
      else setError('Не удалось войти: сервер недоступен')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Center mih="100dvh" p="md">
      <Paper withBorder shadow="sm" p="xl" w="100%" maw={380}>
        <form onSubmit={submit}>
          <Stack>
            <Title order={2}>NewsFactory</Title>
            {error && <Alert color="red">{error}</Alert>}
            <TextInput label="Логин" value={user} onChange={(e) => setUser(e.currentTarget.value)} autoComplete="username" required />
            <PasswordInput label="Пароль" value={password} onChange={(e) => setPassword(e.currentTarget.value)}
              autoComplete="current-password" required />
            <Button type="submit" loading={busy}>Войти</Button>
          </Stack>
        </form>
      </Paper>
    </Center>
  )
}
