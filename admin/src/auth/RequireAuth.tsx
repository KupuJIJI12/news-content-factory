import type { ReactNode } from 'react'
import { Center, Loader } from '@mantine/core'
import { Navigate, useLocation } from 'react-router'
import { useMe } from '../api/auth'

export function RequireAuth({ children }: { children: ReactNode }) {
  const me = useMe()
  const location = useLocation()
  if (me.isPending) return <Center mih="100dvh"><Loader /></Center>
  if (me.isError) {
    const next = encodeURIComponent(location.pathname + location.search)
    return <Navigate to={`/login?next=${next}`} replace />
  }
  return <>{children}</>
}
