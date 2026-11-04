import { AppShell, Group, UnstyledButton } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { useQueryClient } from '@tanstack/react-query'
import { Link, Outlet, useLocation, useNavigate } from 'react-router'
import { logout } from '../api/auth'
import type { Counters } from '../api/types'
import { Logo } from '../design/Logo'
import { tokens } from '../design/tokens'
import { useCounters } from '../realtime/useCounters'
import { ChannelMeter, PausedBadge } from './ChannelMeter'
import { CollectionToggle } from './CollectionToggle'

type NavItem = { to: string; label: string; count?: (c: Counters) => number; alarm?: boolean }

const NAV: NavItem[] = [
  { to: '/', label: 'Очередь', count: (c) => c.onReview },
  { to: '/sources', label: 'Источники', count: (c) => c.errors.sourcesFailing + c.errors.sourcesSilent, alarm: true },
  { to: '/settings', label: 'Настройки' },
]

const isActive = (to: string, path: string) => (to === '/' ? path === '/' || path.startsWith('/stories') : path.startsWith(to))

function NavCount({ item, counters }: { item: NavItem; counters: Counters | undefined }) {
  const n = counters && item.count ? item.count(counters) : 0
  if (!n) return null
  return <span className="nf-num" style={{ color: item.alarm ? tokens.brick : tokens.ink, fontWeight: 600 }}>{n}</span>
}

export function AppShellLayout() {
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
    <AppShell
      header={{ height: 56 }}
      navbar={{ width: 220, breakpoint: 'sm', collapsed: { mobile: true } }}
      footer={{ height: 60, collapsed: !isMobile }}
      padding={0}
      styles={{ main: { background: tokens.ground } }}
    >
      <AppShell.Header style={{ borderBottom: `1px solid ${tokens.line}` }}>
        <Group h="100%" px={{ base: 'sm', sm: 'lg' }} justify="space-between" wrap="nowrap" gap="md">
          <Link to="/" aria-label="Контент завод, к очереди" style={{ textDecoration: 'none' }}>
            <Logo size={isMobile ? 16 : 18} />
          </Link>
          <Group gap={isMobile ? 'sm' : 'xl'} wrap="nowrap">
            {counters?.collectionPaused && !isMobile && <PausedBadge />}
            {counters && <CollectionToggle paused={counters.collectionPaused} compact={isMobile} />}
            <ChannelMeter counters={counters} compact={isMobile} />
            {!isMobile && (
              <UnstyledButton onClick={signOut} c="ink.6" fz="sm" px={4} py={8}>Выйти</UnstyledButton>
            )}
          </Group>
        </Group>
      </AppShell.Header>

      <AppShell.Navbar p="sm" style={{ background: tokens.ground, borderRight: 0 }}>
        <nav aria-label="Разделы" style={{ display: 'flex', flexDirection: 'column', gap: 2, paddingTop: 8 }}>
          {NAV.map((item) => {
            const active = isActive(item.to, location.pathname)
            return (
              <Link key={item.to} to={item.to} aria-current={active ? 'page' : undefined} style={{
                display: 'flex', justifyContent: 'space-between', padding: '10px 12px', borderRadius: 6, textDecoration: 'none',
                background: active ? tokens.surface : 'transparent', color: active ? tokens.ink : tokens.inkSoft, fontWeight: active ? 600 : 400,
              }}>
                {item.label}
                <NavCount item={item} counters={counters} />
              </Link>
            )
          })}
        </nav>
      </AppShell.Navbar>

      <AppShell.Main>
        {isMobile && counters?.collectionPaused && (
          <div style={{ padding: '8px 12px 0' }}><PausedBadge /></div>
        )}
        <div style={{ padding: isMobile ? 12 : '20px 24px 24px 12px' }}>
          <Outlet />
        </div>
      </AppShell.Main>

      <AppShell.Footer style={{ borderTop: `1px solid ${tokens.line}` }}>
        <nav aria-label="Разделы" style={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', height: '100%' }}>
          {NAV.map((item) => {
            const active = isActive(item.to, location.pathname)
            return (
              <Link key={item.to} to={item.to} aria-current={active ? 'page' : undefined} style={{
                display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 6, textDecoration: 'none', fontSize: 14,
                color: active ? tokens.ink : tokens.muted, fontWeight: active ? 600 : 400,
              }}>
                {item.label}
                <NavCount item={item} counters={counters} />
              </Link>
            )
          })}
          <UnstyledButton onClick={signOut} c="ink.6" fz={14} style={{ textAlign: 'center' }}>Выйти</UnstyledButton>
        </nav>
      </AppShell.Footer>
    </AppShell>
  )
}
