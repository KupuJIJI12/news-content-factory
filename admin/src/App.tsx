import { Route, Routes } from 'react-router'
import { LoginPage } from './auth/LoginPage'
import { RequireAuth } from './auth/RequireAuth'
import { AppShellLayout } from './layout/AppShellLayout'
import { QueuePage } from './queue/QueuePage'
import { StoryPage } from './story/StoryPage'

function Placeholder({ title }: { title: string }) {
  return <div>{title}</div>
}

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<RequireAuth><AppShellLayout /></RequireAuth>}>
        <Route path="/" element={<QueuePage />} />
        <Route path="/stories/:id" element={<StoryPage />} />
        <Route path="/sources" element={<Placeholder title="Источники" />} />
        <Route path="/settings" element={<Placeholder title="Настройки" />} />
      </Route>
    </Routes>
  )
}
