import { Route, Routes } from 'react-router'
import { LoginPage } from './auth/LoginPage'
import { RequireAuth } from './auth/RequireAuth'
import { AppShellLayout } from './layout/AppShellLayout'
import { QueuePage } from './queue/QueuePage'
import { SettingsPage } from './settings/SettingsPage'
import { SourcesPage } from './sources/SourcesPage'
import { StoryPage } from './story/StoryPage'

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<RequireAuth><AppShellLayout /></RequireAuth>}>
        <Route path="/" element={<QueuePage />} />
        <Route path="/stories/:id" element={<StoryPage />} />
        <Route path="/sources" element={<SourcesPage />} />
        <Route path="/settings" element={<SettingsPage />} />
      </Route>
    </Routes>
  )
}
