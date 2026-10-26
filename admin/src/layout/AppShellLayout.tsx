import { AppShell, Burger, Button, Group, NavLink, Title } from '@mantine/core'
import { useDisclosure, useMediaQuery } from '@mantine/hooks'
import { IconInbox, IconRss, IconSettings } from '@tabler/icons-react'
import { useQueryClient } from '@tanstack/react-query'
import { Link, Outlet, useLocation, useNavigate } from 'react-router'
import { logout } from '../api/auth'
import { useCounters } from '../realtime/useCounters'
import { CountersBar } from './CountersBar'

const links = [
  { to: '/', label: 'Очередь', icon: IconInbox },
  { to: '/sources', label: 'Источники', icon: IconRss },
  { to: '/settings', label: 'Настройки', icon: IconSettings },
]

export function AppShellLayout() {
  const [opened, { toggle, close }] = useDisclosure()
  const isMobile = useMediaQuery('(max-width: 48em)') ?? false
  const counters = useCounters()
  const location = useLocation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  async function signOut() {
    await logout().catch(() => {})
    queryClient.clear()
    navigate('/login', { replace: true })
  }

  return (
    <AppShell header={{ height: 56 }} navbar={{ width: 220, breakpoint: 'sm', collapsed: { mobile: !opened } }} padding="md">
      <AppShell.Header>
        <Group h="100%" px="md" justify="space-between" wrap="nowrap">
          <Group gap="sm" wrap="nowrap">
            <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" aria-label="Меню" />
            <Title order={4}>NewsFactory</Title>
          </Group>
          <Group gap="sm" wrap="nowrap">
            <CountersBar counters={counters} compact={isMobile} />
            <Button variant="subtle" size="xs" onClick={signOut}>Выйти</Button>
          </Group>
        </Group>
      </AppShell.Header>
      <AppShell.Navbar p="xs">
        {links.map((l) => (
          <NavLink key={l.to} component={Link} to={l.to} label={l.label} leftSection={<l.icon size={18} />}
            active={l.to === '/' ? location.pathname === '/' || location.pathname.startsWith('/stories') : location.pathname.startsWith(l.to)}
            onClick={close} />
        ))}
      </AppShell.Navbar>
      <AppShell.Main>
        <Outlet />
      </AppShell.Main>
    </AppShell>
  )
}
