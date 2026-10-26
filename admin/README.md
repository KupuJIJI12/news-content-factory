# Админка NewsFactory (React SPA)

React + TypeScript + Vite + Mantine. В разработке Vite проксирует `/api`, `/hubs` и `/hangfire` на бэкенд
(по умолчанию `http://localhost:5080`, переопределяется переменной `VITE_API_TARGET`). В продакшене SPA раздаёт nginx (план 3).

## Локальный запуск

1. Бэкенд (PostgreSQL должен быть запущен):

   ```powershell
   $env:ConnectionStrings__Postgres = "Host=localhost;Database=newsfactory;Username=postgres;Password=<пароль>"
   $env:ADMIN_LOGIN    = "admin"
   $env:ADMIN_PASSWORD = "<пароль админки>"
   $env:MAX_CHANNEL_CHAT_ID = "<id тестового канала>"   # необязательно
   $env:MAX_BOT_TOKEN       = "<токен бота>"             # необязательно
   $env:GIGACHAT_AUTH_KEY   = "<ключ GigaChat>"          # необязательно
   dotnet run --project src/NewsFactory.Api --urls http://localhost:5080
   ```

   Первая учётка создаётся из `ADMIN_LOGIN`/`ADMIN_PASSWORD`, только если пользователей ещё нет.

2. Интерфейс:

   ```powershell
   npm --prefix admin install
   npm --prefix admin run dev
   ```

   Открыть http://localhost:5173.

## Команды

| Команда | Что делает |
|---|---|
| `npm --prefix admin test` | тесты (Vitest + Testing Library) |
| `npm --prefix admin run build` | проверка типов и сборка в `admin/dist` |
| `npm --prefix admin run lint` | только проверка типов |
