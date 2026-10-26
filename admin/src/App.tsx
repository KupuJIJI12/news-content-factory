import { Route, Routes } from 'react-router'
import { LoginPage } from './auth/LoginPage'
import { RequireAuth } from './auth/RequireAuth'

function Placeholder({ title }: { title: string }) {
  return <div style={{ padding: 16 }}>{title}</div>
}

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/" element={<RequireAuth><Placeholder title="Очередь" /></RequireAuth>} />
      <Route path="/stories/:id" element={<RequireAuth><Placeholder title="Сюжет" /></RequireAuth>} />
      <Route path="/sources" element={<RequireAuth><Placeholder title="Источники" /></RequireAuth>} />
      <Route path="/settings" element={<RequireAuth><Placeholder title="Настройки" /></RequireAuth>} />
    </Routes>
  )
}
