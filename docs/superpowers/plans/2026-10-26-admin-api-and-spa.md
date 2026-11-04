# NewsContentFactory — план 2: API админки и SPA

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Модератор входит в адаптивную веб-админку, видит живые счётчики и очередь сюжетов, правит черновик с превью Max, выбирает фото, публикует сразу или по расписанию, управляет источниками и настройками LLM.

**Architecture:** В существующий хост `NewsFactory.Api` добавляются REST-эндпоинты (minimal API, группа `/api`), cookie-аутентификация, SignalR-хаб `/hubs/admin` с фоновым рассыльщиком счётчиков и файловое хранилище картинок. Бизнес-операции переиспользуют сервисы плана 1 (`PublicationService`, `IPipelineQueue`, `DailyLimitPolicy`, `MaxMarkupConverter`, `DeterministicFactChecker`, коллекторы). Отдельный проект `admin/` — React SPA (Vite + TypeScript + Mantine), в разработке проксирует `/api` и `/hubs` на бэкенд; раздачу через nginx делает план 3.

**Tech Stack:** ASP.NET Core 10 minimal API, cookie auth, `Microsoft.AspNetCore.Identity` `PasswordHasher<T>` (пакет `Microsoft.Extensions.Identity.Core`), ASP.NET rate limiting, SignalR; React 19, TypeScript, Vite, Mantine 8 (`@mantine/core`, `@mantine/dates`, `@mantine/notifications`), TanStack Query 5, React Router 7, `@microsoft/signalr`, dayjs (+utc, timezone); Vitest + React Testing Library + jsdom.

**Spec:** `docs/superpowers/specs/2026-10-23-news-content-factory-design.md` (§9 «Админка», §6.9 «Модерация и публикация», §10 «Ошибки и надёжность»). Предыдущий план: `docs/superpowers/plans/2026-10-23-backend-pipeline.md`.

## Global Constraints

- Всё из Global Constraints плана 1 остаётся в силе (net10.0, `TreatWarningsAsErrors`, UTC в БД, `TimeProvider`, секреты только из окружения, Conventional Commits).
- Все эндпоинты — под `/api`, требуют аутентификации, кроме `POST /api/auth/login`. Неаутентифицированный запрос к `/api/*` → `401` (без редиректа). Хаб `/hubs/admin` и `/hangfire` тоже только после входа.
- Cookie: имя `nf_auth`, `HttpOnly`, `SameSite=Strict`, `Secure` = `SameAsRequest` (админка работает по HTTP через SSH-тоннель), скользящий срок 7 дней.
- Первая учётка — из `ADMIN_LOGIN` / `ADMIN_PASSWORD` при старте, если в таблице `Users` нет ни одной записи. Пароль хранится только хэшем `PasswordHasher<User>`.
- Ограничение входа: не больше 5 попыток `POST /api/auth/login` в минуту с одного IP → `429`.
- JSON API: `camelCase`, enum — строками (`JsonStringEnumConverter`), время — ISO 8601 UTC.
- Ошибки — `application/problem+json` с расширением `code`: `not_found`, `validation`, `invalid_status`, `daily_limit_exceeded`, `post_too_long`, `conflict`, `photo_not_allowed`.
- Время, которое вводит и видит модератор (расписание, «сегодня»), — в часовом поясе города (`City.TimeZone`, `Europe/Samara`), независимо от пояса браузера.
- Весь UI — на русском. Адаптивность: брейкпоинт Mantine `sm` (ниже — телефонная раскладка).
- Картинки: только JPEG/PNG/WEBP/GIF, не больше 15 МБ; хранятся в `Images:Root` (по умолчанию `data/images`), имя файла — `{guid}{ext}`.
- Версии npm-пакетов — последние стабильные на момент выполнения, в `package.json` закрепляются точные версии (`--save-exact`).
- Команды тестов: бэкенд — `dotnet test --project <проект>` (с `NEWSFACTORY_TEST_PG`); фронтенд — `npm --prefix admin test` (Vitest, `--run`), сборка — `npm --prefix admin run build`.

## Review Focus

1. **Модератор с браузером в другом часовом поясе ставит публикацию на «09:00»** → публикация выходит в 09:00 по Самаре, а не по поясу браузера. Тест: Task 9, `cityTime.test.ts › toCityIso interprets wall time in city zone`.
2. **Модератор поправил черновик, а в очереди уже стояла автоматическая пересборка сюжета (пришёл новый источник)** → правка человека не затирается: задача только ставит «есть обновления». Тест: Task 4, `ProcessStoryJobTests.Auto_run_does_not_overwrite_human_draft`.
3. **Сессия истекла, пока модератор редактировал текст** → после повторного входа несохранённый текст на месте. Тест: Task 11, `useDraftAutosave.test.ts › restores unsaved text after remount`.
4. **Загрузили не картинку или файл на 40 МБ** → понятная ошибка `400` с кодом `validation`, а не `500`. Тест: Task 5, `ImageEndpointsTests.Rejects_non_image_and_oversized_upload`.
5. **Пост длиннее 4000 символов** → кнопка публикации недоступна в превью, а сервер отказывает сразу (`422 post_too_long`), не дожидаясь падения задачи публикации. Тест: Task 4, `StoryActionsTests.Publish_rejects_post_longer_than_limit`.

---

## Структура файлов

```
src/NewsFactory.Api/
  Program.cs                         — + auth, rate limiter, SignalR, admin endpoints, JSON options
  Admin/
    AdminSetup.cs                    — AddAdmin(services, config) / MapAdmin(app): регистрация и маршруты
    Problems.cs                      — ProblemDetails с полем code
    CurrentUser.cs                   — ClaimsPrincipal → UserId
    Auth/AuthEndpoints.cs, Auth/AdminUserSeeder.cs, Auth/HangfireDashboardAuth.cs
    Counters/CountersService.cs, Counters/AdminHub.cs, Counters/CountersBroadcaster.cs
    Stories/StoryQueries.cs, Stories/StoryEndpoints.cs, Stories/StoryDtos.cs, Stories/DraftEditor.cs
    Images/ImageStore.cs, Images/ImageEndpoints.cs
    Preview/PreviewEndpoints.cs
    Sources/SourceEndpoints.cs, Sources/SourceDtos.cs, Sources/SourceTester.cs
    Settings/SettingsEndpoints.cs, Settings/SettingsDtos.cs
src/NewsFactory.Llm/PromptVariables.cs — обязательные/допустимые переменные промптов по задачам
src/NewsFactory.Processing/Jobs/ProcessStoryJob.cs — правка: автозапуск не трогает черновик человека
tests/NewsFactory.Api.Tests/
  AdminTestHost.cs                   — WebApplicationFactory + вход + подмена очереди/LLM/публикатора
  AuthTests.cs, CountersTests.cs, StoryQueriesTests.cs, StoryActionsTests.cs,
  ImageEndpointsTests.cs, PreviewTests.cs, SourceEndpointsTests.cs, SettingsEndpointsTests.cs
admin/
  package.json, vite.config.ts, tsconfig.json, index.html, vitest.setup.ts
  src/main.tsx, src/App.tsx, src/theme.ts
  src/api/client.ts, src/api/types.ts, src/api/*.ts (по ресурсу)
  src/auth/LoginPage.tsx, src/auth/RequireAuth.tsx
  src/layout/AppShellLayout.tsx, src/layout/CountersBar.tsx
  src/realtime/useCounters.ts
  src/lib/cityTime.ts, src/lib/format.ts
  src/queue/QueuePage.tsx
  src/story/StoryPage.tsx, SourcesColumn.tsx, DraftEditor.tsx, MaxPostPreview.tsx, ImagePicker.tsx,
            PublishPanel.tsx, useDraftAutosave.ts
  src/sources/SourcesPage.tsx, SourceForm.tsx, SourceTestResult.tsx
  src/settings/SettingsPage.tsx, CitySettings.tsx, ChannelSettings.tsx, LlmSettings.tsx, PromptSettings.tsx, UsageSettings.tsx
  src/**/*.test.ts(x)
admin/README.md                      — как запускать админку локально
```

---

### Task 1: Аутентификация

**Files:**
- Create: `src/NewsFactory.Api/Admin/AdminSetup.cs`, `Admin/Problems.cs`, `Admin/CurrentUser.cs`, `Admin/Auth/AuthEndpoints.cs`, `Admin/Auth/AdminUserSeeder.cs`, `Admin/Auth/HangfireDashboardAuth.cs`
- Modify: `src/NewsFactory.Api/Program.cs`, `NewsFactory.Api.csproj` (+ `Microsoft.Extensions.Identity.Core`), `Directory.Packages.props`
- Create: `tests/NewsFactory.Api.Tests/AdminTestHost.cs`
- Test: `tests/NewsFactory.Api.Tests/AuthTests.cs`

**Interfaces:**
- Consumes: `User` (план 1), `NewsFactoryDbContext`.
- Produces:
  - `static class AdminSetup { IServiceCollection AddAdmin(this IServiceCollection s, IConfiguration c); WebApplication MapAdmin(this WebApplication app); }` — все последующие задачи регистрируют свои сервисы/маршруты здесь. `MapAdmin` создаёт `RouteGroupBuilder api = app.MapGroup("/api").RequireAuthorization()`; каждая задача добавляет свой `Map…Endpoints(RouteGroupBuilder api)`.
  - `static class Problems { IResult NotFound(string what); IResult Validation(string message, IDictionary<string,string[]>? errors = null); IResult InvalidStatus(string message); IResult Conflict(string code, string message, object? extra = null); }` — ProblemDetails с `extensions["code"]`; `extra` попадает в extensions.
  - `static long CurrentUser.Id(this ClaimsPrincipal user)` — claim `ClaimTypes.NameIdentifier`.
  - Эндпоинты: `POST /api/auth/login {login, password}` → `200 {id, login, timeZone}` + cookie; неверная пара → `401` `{code:"invalid_credentials"}`; `POST /api/auth/logout` → `204`; `GET /api/auth/me` → `200 {id, login, timeZone}` (`timeZone` — `City.TimeZone` первого города, иначе `Europe/Samara`).
  - `AdminUserSeeder.EnsureAdminAsync(NewsFactoryDbContext db, IConfiguration config, ILogger log, CancellationToken ct)` — вызывается в `Program.cs` после `SeedData.ApplyAsync`.
  - `HangfireDashboardAuth : IDashboardAuthorizationFilter` — пускает только аутентифицированных; `/hangfire` подключается всегда (не только в Development) с этим фильтром.
  - `AdminTestHost : IAsyncDisposable` (тесты): `static Task<AdminTestHost> StartAsync(PostgresFixture db, Action<IServiceCollection>? configure = null)` — `WebApplicationFactory<Program>` с `ConnectionStrings:Postgres`, `ADMIN_LOGIN=admin`, `ADMIN_PASSWORD=secret-pass`, `MAX_CHANNEL_CHAT_ID=-100`, `Hangfire:WorkerCount=1`, подменёнными `IPipelineQueue` → `RecordingPipelineQueue`, `ILlmService` → `FakeLlmService`, `IPublisher` → фейк; свойства `HttpClient Client` (уже вошёл, cookies включены), `HttpClient Anonymous`, `IServiceProvider Services`, `RecordingPipelineQueue Queue`, `FakeLlmService Llm`; `Task<T> GetJsonAsync<T>(string url)`, `Task<HttpResponseMessage> PostJsonAsync(string url, object body)`, `Task<HttpResponseMessage> PutJsonAsync(...)`; `Task<T> WithDbAsync<T>(Func<NewsFactoryDbContext, Task<T>> f)`.

- [ ] **Step 1: Написать падающие тесты**

```csharp
[Collection("db")]
public class AuthTests(PostgresFixture db)
{
    [Fact] public async Task First_admin_is_created_from_environment_with_hashed_password()
    { /* после старта: Users.Single().Login == "admin", PasswordHash != "secret-pass" и не пустой */ }

    [Fact] public async Task Api_requires_authentication_with_401_not_redirect()
    { /* Anonymous GET /api/auth/me → 401, Location отсутствует */ }

    [Fact] public async Task Login_sets_httponly_strict_cookie_and_me_returns_user()
    { /* POST /api/auth/login {admin, secret-pass} → 200; Set-Cookie содержит "nf_auth", "httponly", "samesite=strict";
         GET /api/auth/me → {login:"admin", timeZone:"Europe/Samara"} */ }

    [Fact] public async Task Wrong_password_is_401_with_code()
    { /* → 401, problem code == "invalid_credentials" */ }

    [Fact] public async Task Sixth_login_attempt_in_a_minute_is_rate_limited()
    { /* 5 неверных попыток → 401, шестая → 429 */ }

    [Fact] public async Task Logout_clears_session()
    { /* login → logout 204 → GET /api/auth/me → 401 */ }

    [Fact] public async Task Hangfire_dashboard_requires_login()
    { /* Anonymous GET /hangfire → 401; Client (вошёл) GET /hangfire → 200 */ }

    [Fact] public async Task Existing_users_are_not_touched_by_seeder()
    { /* второй старт хоста с ADMIN_PASSWORD=другой → вход со старым паролем работает */ }
}
```

- [ ] **Step 2: Запустить — падает.** Run: `dotnet test --project tests/NewsFactory.Api.Tests --filter-class "*AuthTests"` → Expected: ошибка компиляции (`AdminTestHost`/эндпоинтов нет).
- [ ] **Step 3: Реализовать.** Cookie auth: `AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(...)` с `Events.OnRedirectToLogin/OnRedirectToAccessDenied` → 401/403. Rate limiter: `AddRateLimiter` политика `"login"` — `FixedWindow` 5/мин по `RemoteIpAddress`, `RejectionStatusCode = 429`; на эндпоинт логина `.RequireRateLimiting("login")`. `ConfigureHttpJsonOptions`: camelCase + `JsonStringEnumConverter`. В `Program.cs`: `services.AddAdmin(config)`, `app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.MapAdmin();`, Hangfire dashboard с `HangfireDashboardAuth`.
- [ ] **Step 4: Запустить — проходит.** Затем `dotnet test --project tests/NewsFactory.Api.Tests` → все PASS (старые тесты хоста тоже).
- [ ] **Step 5: Commit** — `git commit -m "feat(admin): cookie auth, admin seeding, login rate limit, dashboard auth"`

---

### Task 2: Счётчики и SignalR

**Files:**
- Create: `src/NewsFactory.Api/Admin/Counters/CountersService.cs`, `AdminHub.cs`, `CountersBroadcaster.cs`
- Modify: `Admin/AdminSetup.cs`
- Test: `tests/NewsFactory.Api.Tests/CountersTests.cs`

**Interfaces:**
- Consumes: `SourceHealth.Evaluate` (план 1), `DailyLimitPolicy.Check`, `LlmBudgetGuard.SettingKey`/`DefaultBudget`.
- Produces:
  - `record ChannelToday(long ChannelId, string Name, int Published, int Scheduled, int Limit)`
  - `record ErrorBreakdown(int SourcesFailing, int SourcesSilent, int StoriesProcessingFailed, int StoriesPublishFailed, int RawItemsFailed24h, bool BudgetExceeded) { int Total => …сумма + (BudgetExceeded ? 1 : 0); }`
  - `record Counters(int OnReview, int WithUpdates, ErrorBreakdown Errors, IReadOnlyList<ChannelToday> Today)`
  - `class CountersService(NewsFactoryDbContext db, TimeProvider time)`; `Task<Counters> GetAsync(CancellationToken ct)`:
    - `OnReview` — сюжеты `ReadyForReview`;
    - `WithUpdates` — `HasUpdates = true` и статус ∈ {`LowInterest`, `ReadyForReview`, `Scheduled`, `Published`};
    - ошибки — включённые источники по `SourceHealth` (`Failing`/`Silent`), сюжеты `ProcessingFailed`/`PublishFailed`, сырые новости `Failed` с `CollectedAt` за последние 24 ч, бюджет токенов за текущие сутки (пояс первого города) ≥ бюджета;
    - `Today` — для каждого включённого канала `DailyLimitPolicy.Check(channel.DailyLimit, публикации канала, now, tz города)`.
  - `GET /api/counters` → `Counters`.
  - `[Authorize] class AdminHub : Hub` по адресу `/hubs/admin`; событие клиента `"CountersChanged"` с аргументом `Counters`.
  - `class CountersBroadcaster(IServiceScopeFactory scopes, IHubContext<AdminHub> hub, TimeProvider time, ILogger<CountersBroadcaster> log) : BackgroundService` — раз в `Admin:CountersIntervalSeconds` (по умолчанию 5) считает счётчики в новом скоупе; если JSON отличается от последнего отправленного — `hub.Clients.All.SendAsync("CountersChanged", counters)`. Ошибки логирует и продолжает. Для тестов выделить `internal Task TickAsync(CancellationToken ct)`.

- [ ] **Step 1: Написать падающие тесты**

```csharp
[Collection("db")]
public class CountersTests(PostgresFixture db)
{
    [Fact] public async Task Counts_review_updates_errors_and_today()
    {
        // данные: 2 ReadyForReview (один HasUpdates), 1 LowInterest HasUpdates, 1 ProcessingFailed, 1 PublishFailed,
        //         источник с ConsecutiveFailures=3, источник с LastItemAt=now-30ч при ExpectedFrequencyHours=24,
        //         выключенный источник с 5 ошибками, RawItem Failed вчера и RawItem Failed 3 дня назад,
        //         канал с лимитом 4: 1 Published сегодня, 1 Scheduled сегодня
        var c = await host.GetJsonAsync<Counters>("/api/counters");
        Assert.Equal((2, 2), (c.OnReview, c.WithUpdates));
        Assert.Equal((1, 1, 1, 1, 1, false), (c.Errors.SourcesFailing, c.Errors.SourcesSilent, c.Errors.StoriesProcessingFailed,
            c.Errors.StoriesPublishFailed, c.Errors.RawItemsFailed24h, c.Errors.BudgetExceeded));
        Assert.Equal((1, 1, 4), (c.Today.Single().Published, c.Today.Single().Scheduled, c.Today.Single().Limit));
    }

    [Fact] public async Task Budget_spent_today_is_reported()
    { /* AppSetting DailyTokenBudget=100, LlmCall на 150 токенов сегодня → Errors.BudgetExceeded, Errors.Total >= 1 */ }

    [Fact] public async Task Broadcaster_sends_only_when_counters_change()
    {
        // FakeHubContext записывает SendCoreAsync(method, args)
        // TickAsync ×2 без изменений → 1 отправка "CountersChanged"; добавить ReadyForReview → TickAsync → 2 отправки
    }

    [Fact] public async Task Hub_rejects_anonymous_connection()
    { /* Anonymous POST /hubs/admin/negotiate?negotiateVersion=1 → 401 */ }
}
```
(`FakeHubContext : IHubContext<AdminHub>` — тестовый класс в этом же файле: `Clients.All` возвращает `IClientProxy`, который пишет вызовы в список.)

- [ ] **Step 2: Запустить — падает.** Run: `dotnet test --project tests/NewsFactory.Api.Tests --filter-class "*CountersTests"`.
- [ ] **Step 3: Реализовать** `CountersService`, `AdminHub`, `CountersBroadcaster` (`AddSignalR()`, `AddHostedService`, `app.MapHub<AdminHub>("/hubs/admin").RequireAuthorization()`), эндпоинт `GET /api/counters`.
- [ ] **Step 4: Запустить — проходит.**
- [ ] **Step 5: Commit** — `git commit -m "feat(admin): counters endpoint and signalr broadcaster"`

---

### Task 3: Чтение сюжетов — очередь и карточка

**Files:**
- Create: `src/NewsFactory.Api/Admin/Stories/StoryDtos.cs`, `StoryQueries.cs`, `StoryEndpoints.cs` (только GET в этой задаче)
- Test: `tests/NewsFactory.Api.Tests/StoryQueriesTests.cs`

**Interfaces:**
- Consumes: сущности плана 1, `MaxMarkupConverter`, `PostContent`, `PostTooLongException`.
- Produces:
  - `enum QueueTab { Review, LowInterest, Scheduled, Published, Rejected, Errors }` — статусы: Review=`ReadyForReview`; LowInterest=`LowInterest`; Scheduled=`Scheduled`; Published=`Published`; Rejected=`Rejected`; Errors=`ProcessingFailed`+`PublishFailed`.
  - `record StoryListItem(long Id, StoryStatus Status, int? Score, string Title, int SourcesCount, bool HasUpdates, DateTimeOffset UpdatedAt, DateTimeOffset? ScheduledAt)` — `Title` = заголовок текущего черновика, иначе `Summary`; `ScheduledAt` — ближайшая публикация в статусе `Scheduled`.
  - `record StoryListPage(IReadOnlyList<StoryListItem> Items, int Total)`
  - `GET /api/stories?tab=Review&skip=0&take=50` — сортировка: Review — `Score desc, UpdatedAt desc`; Scheduled — `ScheduledAt asc`; остальные — `UpdatedAt desc`. `take` ≤ 100.
  - `record SourceImage(string Url, bool Allowed)`; `record StorySourceDto(long RawItemId, string SourceName, string Url, string Title, string Text, DateTimeOffset? PublishedAt, IReadOnlyList<SourceImage> Images)` — `Allowed` = `Source.PhotosAllowedWithCredit`.
  - `record DraftDto(long Id, int Version, string Title, string Body, IReadOnlyList<string> Hashtags, bool ByHuman, string? RegenerationHint, IReadOnlyList<FactCheckIssue> FactCheckIssues, DateTimeOffset CreatedAt, bool IsCurrent)`
  - `record StoryImageDto(long Id, string Url, string? Credit, bool IsAllowed, bool Uploaded)` — `Url` = `/api/images/{id}`.
  - `record PublicationDto(long Id, long ChannelId, string ChannelName, PublicationStatus Status, DateTimeOffset ScheduledAt, DateTimeOffset? PublishedAt, string? LastError, bool OverrideLimit)`
  - `record StoryDetail(long Id, StoryStatus Status, int? Score, string? ScoreReason, string Summary, bool HasUpdates, string? LastError, IReadOnlyList<StorySourceDto> Sources, DraftDto? CurrentDraft, IReadOnlyList<DraftDto> Versions, IReadOnlyList<StoryImageDto> Images, IReadOnlyList<PublicationDto> Publications)` — `Versions` по убыванию версии.
  - `GET /api/stories/{id}` → `StoryDetail` или `404 not_found`.

- [ ] **Step 1: Написать падающие тесты**

```csharp
[Collection("db")]
public class StoryQueriesTests(PostgresFixture db)
{
    [Fact] public async Task Review_tab_sorted_by_score_and_uses_draft_title()
    { /* три ReadyForReview со Score 5, 9, 7; у одного нет черновика → Title == Summary; порядок 9,7,5; Total == 3 */ }

    [Fact] public async Task Errors_tab_contains_both_failure_statuses() { }

    [Fact] public async Task Paging_respects_skip_take_and_caps_take_at_100()
    { /* 3 сюжета, take=2 → 2 элемента, Total 3; take=1000 → не больше 100 */ }

    [Fact] public async Task Detail_contains_sources_with_photo_permission_versions_and_publications()
    {
        // источник с PhotosAllowedWithCredit=true и картинкой; черновики v1 (LLM) и v2 (человек, current); публикация Scheduled
        var d = await host.GetJsonAsync<StoryDetail>($"/api/stories/{id}");
        Assert.True(d.Sources.Single().Images.Single().Allowed);
        Assert.Equal([2, 1], d.Versions.Select(v => v.Version));
        Assert.True(d.CurrentDraft!.ByHuman);
        Assert.Equal(PublicationStatus.Scheduled, d.Publications.Single().Status);
    }

    [Fact] public async Task Unknown_story_is_404_with_code() { }
}
```

- [ ] **Step 2: Запустить — падает.** Run: `dotnet test --project tests/NewsFactory.Api.Tests --filter-class "*StoryQueriesTests"`.
- [ ] **Step 3: Реализовать** `StoryQueries` (`Task<StoryListPage> ListAsync(QueueTab tab, int skip, int take, CancellationToken ct)`, `Task<StoryDetail?> GetAsync(long id, CancellationToken ct)`) и GET-маршруты.
- [ ] **Step 4: Запустить — проходит.**
- [ ] **Step 5: Commit** — `git commit -m "feat(admin): story queue and detail queries"`

---

### Task 4: Действия с сюжетом и превью

**Files:**
- Create: `src/NewsFactory.Api/Admin/Stories/DraftEditor.cs`, `Admin/Preview/PreviewEndpoints.cs`
- Modify: `Admin/Stories/StoryEndpoints.cs`, `src/NewsFactory.Processing/Jobs/ProcessStoryJob.cs`, `src/NewsFactory.Publishing/Max/MaxMarkupConverter.cs` (+ `Render`)
- Test: `tests/NewsFactory.Api.Tests/StoryActionsTests.cs`, `tests/NewsFactory.Api.Tests/PreviewTests.cs`, `tests/NewsFactory.Processing.Tests/ProcessStoryJobTests.cs`

**Interfaces:**
- Consumes: `PublicationService.ScheduleAsync/CancelAsync`, `DailyLimitExceededException`, `InvalidStatusTransitionException`, `IPipelineQueue.EnqueueStory`, `DeterministicFactChecker.Check`, `SourceTextBuilder.Build`, `MaxMarkupConverter.Convert`, `CurrentUser.Id`.
- Produces:
  - `class DraftEditor(NewsFactoryDbContext db)`; `Task<DraftDto> SaveAsync(long storyId, long userId, string title, string body, IReadOnlyList<string> hashtags, CancellationToken ct)` — разрешено в статусах `ReadyForReview`, `LowInterest`, `PublishFailed` (иначе `InvalidStatusTransitionException`); создаёт новую версию `AuthorUserId = userId`, `IsCurrent = true`; хэштеги нормализуются как в `ProcessStoryJob`; `FactCheckIssues` = детерминированные замечания по новому тексту + LLM-замечания прошлой версии, чей `Claim` всё ещё встречается в новом тексте.
  - Маршруты (все возвращают `204`, кроме указанных):
    - `PUT /api/stories/{id}/draft {title, body, hashtags}` → `200 DraftDto`; пустой заголовок/текст → `400 validation`.
    - `POST /api/stories/{id}/regenerate {hint?}` → `202`; статус ∈ {`ReadyForReview`, `LowInterest`, `ProcessingFailed`} → `queue.EnqueueStory(id, forceDraft: true, hint)`; иначе `409 invalid_status`.
    - `POST /api/stories/{id}/generate` → `202`; только из `LowInterest` → `EnqueueStory(id, forceDraft: true)`.
    - `POST /api/stories/{id}/retry` → `202`; только из `ProcessingFailed` → `EnqueueStory(id)`.
    - `POST /api/stories/{id}/reject` → `story.TransitionTo(Rejected)`.
    - `POST /api/stories/{id}/ack-updates` → `HasUpdates = false`.
    - `POST /api/stories/{id}/publish {at?: string(ISO), imageId?: long, overrideLimit: bool}` → `200 {publicationIds}`. До вызова сервиса: текущий черновик (+ подпись выбранной картинки) прогоняется через `MaxMarkupConverter.Convert` для каждого включённого Max-канала; `PostTooLongException` → `422 post_too_long {length, max}`. `imageId` должен принадлежать сюжету, иначе `400 validation`. `at` в прошлом больше чем на 1 минуту → `400 validation`. `DailyLimitExceededException` → `409 daily_limit_exceeded {published, scheduled, limit}`; `InvalidStatusTransitionException` → `409 invalid_status`.
    - `POST /api/stories/{id}/cancel` → `PublicationService.CancelAsync`.
  - `POST /api/preview {title, body, hashtags, imageCredit?}` → `200 [{channelId, channelName, platform, text, length, max: 4000, tooLong}]` для каждого включённого канала города (в MVP — один Max); при `tooLong` поле `text` — результат без проверки длины (для показа), т. е. в `MaxMarkupConverter` добавить `string Render(PostContent)` без проверки, а `Convert` = `Render` + проверка.
  - Правка `ProcessStoryJob.RunAsync`: если `forceDraft == false` и `hint == null` (автозапуск) и текущий черновик написан человеком → после пересчёта оценки черновик **не** пересобирается: `HasUpdates = true`, статус возвращается к прежнему.

- [ ] **Step 1: Написать падающие тесты**

```csharp
// tests/NewsFactory.Processing.Tests/ProcessStoryJobTests.cs
[Fact] public async Task Auto_run_does_not_overwrite_human_draft()
{
    // ReadyForReview, current draft v2 AuthorUserId=1; Score(9); Rewrite настроен (не должен вызываться)
    // RunAsync(id, forceDraft:false, hint:null) → Rewrite не вызывался; current draft всё ещё v2; HasUpdates == true; статус ReadyForReview
}
```

```csharp
[Collection("db")]
public class StoryActionsTests(PostgresFixture db)
{
    [Fact] public async Task Save_draft_creates_human_version_and_rechecks_facts()
    { /* источник "15 ноября …"; PUT draft с "16 ноября" → 200, Version 2, ByHuman; FactCheckIssues содержит "16 ноября"; v1 IsCurrent=false */ }

    [Fact] public async Task Save_draft_keeps_llm_issue_only_if_claim_still_present() { }

    [Fact] public async Task Cannot_edit_scheduled_story() { /* → 409 invalid_status */ }

    [Fact] public async Task Regenerate_enqueues_forced_job_with_hint()
    { /* → 202; host.Queue.Stories == [(id, true, "короче")] */ }

    [Fact] public async Task Regenerate_published_story_is_conflict() { }

    [Fact] public async Task Generate_from_low_interest_and_retry_from_failed_enqueue_jobs() { }

    [Fact] public async Task Reject_and_ack_updates_change_story() { }

    [Fact] public async Task Publish_now_schedules_publication_by_current_user()
    { /* → 200; Publications.Single().ApprovedByUserId == id админа; host.Queue.Scheduled.Single() */ }

    [Fact] public async Task Publish_over_limit_returns_409_with_numbers_and_override_succeeds()
    { /* 4 опубликовано сегодня → 409 code daily_limit_exceeded, limit 4, published 4; overrideLimit:true → 200 */ }

    [Fact] public async Task Publish_rejects_post_longer_than_limit()
    { /* текущий черновик body 4100 символов → 422 post_too_long, length > 4000; публикаций нет */ }

    [Fact] public async Task Publish_with_foreign_image_is_validation_error() { }

    [Fact] public async Task Schedule_in_the_past_is_validation_error() { }

    [Fact] public async Task Cancel_returns_story_to_review() { }
}

[Collection("db")]
public class PreviewTests(PostgresFixture db)
{
    [Fact] public async Task Preview_renders_max_text_and_flags_too_long()
    {
        // короткий → text начинается с "**Заголовок**", tooLong false, max 4000
        // body 4100 → tooLong true, length > 4000, text всё равно возвращён
    }
}
```

- [ ] **Step 2: Запустить — падает.** Run: `dotnet test --project tests/NewsFactory.Api.Tests --filter-class "*StoryActionsTests"` и `--filter-class "*PreviewTests"`; `dotnet test --project tests/NewsFactory.Processing.Tests --filter-method "*Auto_run_does_not_overwrite_human_draft"`.
- [ ] **Step 3: Реализовать** `DraftEditor`, маршруты действий, `MaxMarkupConverter.Render`, превью, правку `ProcessStoryJob`.
- [ ] **Step 4: Запустить — проходит.** Затем полный `dotnet test` (с `NEWSFACTORY_TEST_PG`) → все PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat(admin): story actions, publish with limits, max preview"`

---

### Task 5: Картинки

**Files:**
- Create: `src/NewsFactory.Api/Admin/Images/ImageStore.cs`, `Admin/Images/ImageEndpoints.cs`
- Modify: `Admin/AdminSetup.cs`, `appsettings.json` (`"Images": {"Root": "data/images"}`)
- Test: `tests/NewsFactory.Api.Tests/ImageEndpointsTests.cs`

**Interfaces:**
- Consumes: `StoryImage`, `IPageFetcher.GetBytesAsync` (план 1).
- Produces:
  - `class ImageStore(IOptions<ImageStoreOptions> options)`; `ImageStoreOptions { string Root = "data/images"; long MaxBytes = 15 * 1024 * 1024; }`; `Task<string> SaveAsync(byte[] bytes, CancellationToken ct)` → абсолютный путь; формат определяется по сигнатуре байтов (JPEG `FF D8 FF`, PNG `89 50 4E 47`, GIF `47 49 46 38`, WEBP `RIFF....WEBP`), иначе `ImageRejectedException("Файл не является картинкой JPEG/PNG/WEBP/GIF")`; больше `MaxBytes` → `ImageRejectedException("Картинка больше 15 МБ")`. `Stream Open(string path)`.
  - `POST /api/stories/{id}/images` (multipart: `file`, необязательное `credit`) → `200 StoryImageDto` (`IsAllowed = true`, `RawItemId = null`); отказ → `400 validation`.
  - `POST /api/stories/{id}/images/from-source {rawItemId, url, acknowledgeNoPermission}` → скачивает через `IPageFetcher.GetBytesAsync`; `RawItem` должен принадлежать сюжету и `url` — входить в его `ImageUrls`, иначе `400 validation`. Если у источника `PhotosAllowedWithCredit = false` и `acknowledgeNoPermission != true` → `409 photo_not_allowed`. `Credit` = `Source.PhotoCredit ?? Source.Name` при разрешении, иначе `null`; `IsAllowed` = флаг источника.
  - `GET /api/images/{id}` → файл с правильным `Content-Type`; нет записи/файла → `404`.
  - `DELETE /api/stories/{id}/images/{imageId}` → `204`; если картинка выбрана в публикации со статусом `Scheduled`/`Sending` → `409 conflict`.

- [ ] **Step 1: Написать падающие тесты**

```csharp
[Collection("db")]
public class ImageEndpointsTests(PostgresFixture db)
{
    [Fact] public async Task Upload_stores_file_and_serves_it_back()
    { /* PNG 1x1 + credit "Фото: читатель" → 200, Uploaded, IsAllowed; GET Url → 200 image/png, те же байты */ }

    [Fact] public async Task Rejects_non_image_and_oversized_upload()
    { /* текстовый файл → 400 validation "не является картинкой"; 16 МБ JPEG-заголовок + нули → 400 "больше 15 МБ" */ }

    [Fact] public async Task From_source_with_permission_uses_source_credit()
    { /* источник PhotosAllowedWithCredit, PhotoCredit "otradny.org"; фейковый IPageFetcher отдаёт PNG → Credit "otradny.org", IsAllowed */ }

    [Fact] public async Task From_source_without_permission_requires_acknowledgement()
    { /* без флага → 409 photo_not_allowed; с acknowledgeNoPermission → 200, IsAllowed false, Credit null */ }

    [Fact] public async Task From_source_url_must_belong_to_story() { /* чужой url → 400 */ }

    [Fact] public async Task Cannot_delete_image_used_by_scheduled_publication() { }
}
```
(`IPageFetcher` подменяется в `AdminTestHost.StartAsync(configure: …)`; `Images:Root` — временная папка теста.)

- [ ] **Step 2: Запустить — падает.**
- [ ] **Step 3: Реализовать.**
- [ ] **Step 4: Запустить — проходит.**
- [ ] **Step 5: Commit** — `git commit -m "feat(admin): image upload, source photos with permission, serving"`

---

### Task 6: Источники

**Files:**
- Create: `src/NewsFactory.Api/Admin/Sources/SourceDtos.cs`, `SourceTester.cs`, `SourceEndpoints.cs`
- Test: `tests/NewsFactory.Api.Tests/SourceEndpointsTests.cs`

**Interfaces:**
- Consumes: `ISourceCollector` (все три), `HtmlSourceConfig`, `VkSourceConfig`, `SourceHealth`.
- Produces:
  - `record SourceDto(long Id, string Name, SourceType Type, string Url, string? Config, int PollIntervalMinutes, int? ExpectedFrequencyHours, bool PhotosAllowedWithCredit, string? PhotoCredit, bool IsEnabled, bool IsDedicated, DateTimeOffset? LastRunAt, DateTimeOffset? LastSuccessAt, DateTimeOffset? LastItemAt, int ConsecutiveFailures, string? LastError, SourceHealthStatus Health)` — `IsDedicated` по связи с городом (MVP: единственный город).
  - `record SourceInput(string Name, SourceType Type, string Url, string? Config, int PollIntervalMinutes, int? ExpectedFrequencyHours, bool PhotosAllowedWithCredit, string? PhotoCredit, bool IsEnabled, bool IsDedicated)`
  - Валидация `SourceInput` (→ `400 validation` с полями): имя не пустое; `Url` абсолютный http(s); `PollIntervalMinutes` 15…1440; `Rss` — `Config` null или пусто; `Html` — `Config` десериализуется в `HtmlSourceConfig` с непустыми `ListUrl`, `ItemSelector`, `LinkSelector`; `Vk` — `VkSourceConfig` с непустым `ScreenName`.
  - `GET /api/sources` → `SourceDto[]` (по имени); `POST /api/sources` → `201 SourceDto` (связь с городом создаётся с `IsDedicated`); `PUT /api/sources/{id}` → `200`; дубль `Url` → `409 conflict`.
  - `record SourceTestItem(string Title, string Url, DateTimeOffset? PublishedAt, string TextPreview, int ImageCount)`; `record SourceTestResult(bool Ok, string? Error, IReadOnlyList<SourceTestItem> Items)` — `TextPreview` = первые 300 символов.
  - `class SourceTester(IEnumerable<ISourceCollector> collectors)`; `Task<SourceTestResult> RunAsync(SourceInput input, CancellationToken ct)` — собирает во временный `Source` (не сохраняется), таймаут 60 с; исключение → `Ok = false`, `Error = message`.
  - `POST /api/sources/test` (тело `SourceInput`) → `200 SourceTestResult` (сначала та же валидация).

- [ ] **Step 1: Написать падающие тесты** — `Lists_sources_with_health`, `Creates_html_source_with_valid_selectors_linked_to_city`, `Rejects_html_source_without_selectors` (400, поле `config`), `Rejects_duplicate_url` (409), `Updates_flags_and_interval`, `Test_run_returns_items_without_saving` (фейковый `ISourceCollector` типа Html возвращает 2 элемента → `Ok`, 2 `Items`, `TextPreview.Length <= 300`, в БД нет новых `RawItem`/источников), `Test_run_reports_collector_error` (коллектор бросает → `Ok == false`, `Error` с текстом).
- [ ] **Step 2: Запустить — падает.**
- [ ] **Step 3: Реализовать.**
- [ ] **Step 4: Запустить — проходит.**
- [ ] **Step 5: Commit** — `git commit -m "feat(admin): sources api with validation and dry-run test"`

---

### Task 7: Настройки

**Files:**
- Create: `src/NewsFactory.Llm/PromptVariables.cs`, `src/NewsFactory.Api/Admin/Settings/SettingsDtos.cs`, `SettingsEndpoints.cs`
- Test: `tests/NewsFactory.Llm.Tests/PromptVariablesTests.cs`, `tests/NewsFactory.Api.Tests/SettingsEndpointsTests.cs`

**Interfaces:**
- Consumes: сущности плана 1, `DefaultPrompts`, `LlmBudgetGuard`.
- Produces:
  - `static class PromptVariables { IReadOnlySet<string> Required(LlmTask t); IReadOnlySet<string> Allowed(LlmTask t); IReadOnlyList<string> Validate(LlmTask t, string template); }` — Relevance: `city,title,text`; Clustering: `city,title,text,stories`; Scoring: `city,sources`; Rewrite: обязательные `city,sources`, допустимые + `hint`; FactCheck: `sources,draft`. `Validate` возвращает ошибки: неизвестная переменная, отсутствует обязательная.
  - `GET/PUT /api/settings/city` — `{name, keywords: string[], scoreThreshold (1–10), timeZone}` (`timeZone` только чтение).
  - `GET /api/settings/channels`, `PUT /api/settings/channels/{id}` — `{id, name, platform, externalChatId, dailyLimit (1–20), isEnabled}`.
  - `GET /api/settings/secrets` → `{maxBotToken, vkServiceToken, gigachatAuthKey, yandexApiKey}` — только `bool` «настроено».
  - Провайдеры: `GET/POST /api/settings/llm/providers`, `PUT /api/settings/llm/providers/{id}` — `{id, name, kind, baseUrl, apiKeyEnvVar, models[], headers{}, supportsJsonSchema, authUrl?, authScope?, apiKeyConfigured (только чтение)}`; `models` не пустой; `baseUrl` абсолютный https.
  - Задачи: `GET /api/settings/llm/tasks` → `[{task, providerId, providerName, model, temperature}]`; `PUT /api/settings/llm/tasks/{task}` `{providerId, model, temperature (0–1)}` — `model` должен быть в `models` провайдера, иначе `400`.
  - Промпты: `GET /api/settings/prompts` → `[{task, versions:[{id, version, isActive, createdAt, text}]}]`; `POST /api/settings/prompts/{task} {text}` → новая версия `max+1`, активная, остальные этой задачи `IsActive=false`; ошибки `PromptVariables.Validate` → `400`. `PUT /api/settings/prompts/{id}/activate` → активирует эту, гасит остальные той же задачи.
  - Расход: `GET /api/settings/llm/usage` → `{today, month, dailyBudget}` (токены по поясу города); `PUT /api/settings/llm/budget {dailyTokenBudget ≥ 1000}`.

- [ ] **Step 1: Написать падающие тесты**
  - `PromptVariablesTests`: `Defaults_are_valid` (для каждой задачи `Validate(t, DefaultPrompts.Get(t))` пусто), `Unknown_variable_is_error`, `Missing_required_is_error`, `Hint_is_optional_for_rewrite`.
  - `SettingsEndpointsTests`: `City_update_changes_keywords_and_threshold`, `Threshold_out_of_range_is_400`, `Channel_update_changes_limit_and_chat_id`, `Secrets_report_only_flags` (в ответе нет значений, только bool), `Task_model_must_belong_to_provider`, `New_prompt_version_becomes_the_only_active`, `Invalid_prompt_is_400_with_variable_name`, `Activate_older_version`, `Usage_counts_today_and_month_in_city_zone`, `Budget_below_minimum_is_400`, `Provider_api_key_configured_flag_reflects_environment`.
- [ ] **Step 2: Запустить — падает.**
- [ ] **Step 3: Реализовать.**
- [ ] **Step 4: Запустить — проходит.** Затем полный `dotnet test` → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat(admin): settings api for city, channels, llm and prompts"`

---

### Task 8: Каркас SPA — сборка, тесты, API-клиент, вход

**Files:**
- Create: `admin/` (Vite React TS), `admin/vite.config.ts`, `admin/vitest.setup.ts`, `admin/src/main.tsx`, `App.tsx`, `theme.ts`, `api/client.ts`, `api/types.ts`, `api/auth.ts`, `auth/LoginPage.tsx`, `auth/RequireAuth.tsx`, `admin/README.md`, `admin/.gitignore`
- Test: `admin/src/api/client.test.ts`, `admin/src/auth/LoginPage.test.tsx`

**Interfaces:**
- Consumes: эндпоинты Task 1.
- Produces:
  - `vite.config.ts`: dev-сервер порт 5173, `server.proxy` для `/api` и `/hubs` (с `ws: true`) на `http://localhost:5080` (переменная `VITE_API_TARGET` переопределяет); `test` — `environment: 'jsdom'`, `setupFiles: './vitest.setup.ts'` (`@testing-library/jest-dom`, `matchMedia`-заглушка для Mantine).
  - `package.json` scripts: `dev`, `build` (`tsc -b && vite build`), `test` (`vitest --run`), `lint` (`tsc --noEmit`).
  - `api/types.ts` — TS-типы, **один в один** с DTO Task 1–7 (`Counters`, `ErrorBreakdown`, `ChannelToday`, `StoryListItem`, `StoryListPage`, `StoryDetail`, `StorySourceDto`, `SourceImage`, `DraftDto`, `FactCheckIssue`, `StoryImageDto`, `PublicationDto`, `PreviewResult`, `SourceDto`, `SourceInput`, `SourceTestResult`, `Me`, настройки); enum-ы — строковые union-типы (`'ReadyForReview' | …`).
  - `api/client.ts`: `class ApiError extends Error { status: number; code?: string; extra: Record<string, unknown> }`; `apiFetch<T>(path: string, init?: RequestInit & { json?: unknown }): Promise<T>` — `credentials: 'include'`, JSON-тело, разбор problem+json в `ApiError`; на `401` вызывает зарегистрированный `onUnauthorized()` (переход на `/login?next=…`), кроме самого логина.
  - `api/auth.ts`: `login(login, password): Promise<Me>`, `logout(): Promise<void>`, `me(): Promise<Me>`; хук `useMe()` (TanStack Query, ключ `['me']`).
  - `RequireAuth` — пока `useMe` грузится — лоадер; `401` → редирект на `/login`.
  - `LoginPage` — поля «Логин», «Пароль», кнопка «Войти»; `401` → «Неверный логин или пароль»; `429` → «Слишком много попыток, подождите минуту»; после входа — на `next` или `/`.
  - `App.tsx` маршруты: `/login`, `/` (очередь), `/stories/:id`, `/sources`, `/settings` — все кроме `/login` под `RequireAuth` и `AppShellLayout` (Task 9).

- [ ] **Step 1: Создать проект** — `npm create vite@latest admin -- --template react-ts`; установить зависимости из Tech Stack с `--save-exact`; Vitest/RTL/jsdom/jest-dom — dev-зависимости.
- [ ] **Step 2: Написать падающие тесты**

```ts
// client.test.ts
it('parses problem+json into ApiError with code and extra', async () => {
  // fetch-мок → 409 {code:'daily_limit_exceeded', limit:4, published:4}
  await expect(apiFetch('/api/x')).rejects.toMatchObject({ status: 409, code: 'daily_limit_exceeded', extra: { limit: 4 } })
})
it('calls onUnauthorized on 401', async () => { /* setOnUnauthorized(spy); 401 → spy вызван один раз */ })
it('sends credentials and json body', async () => { /* init.credentials === 'include', content-type application/json */ })

// LoginPage.test.tsx
it('shows rate-limit message on 429', async () => { /* login бросает ApiError(429) → текст «Слишком много попыток» */ })
it('shows wrong credentials message on 401', async () => { })
```
- [ ] **Step 3: Запустить — падает.** Run: `npm --prefix admin test` → Expected: FAIL (модулей нет).
- [ ] **Step 4: Реализовать** клиент, auth, страницу входа, маршруты, тему Mantine (`primaryColor: 'blue'`, `defaultRadius: 'md'`).
- [ ] **Step 5: Запустить — проходит.** `npm --prefix admin test` → PASS; `npm --prefix admin run build` → без ошибок.
- [ ] **Step 6: Записать `admin/README.md`** — локальный запуск: бэкенд `dotnet run --project src/NewsFactory.Api --urls http://localhost:5080` (с `ConnectionStrings__Postgres`, `ADMIN_LOGIN`, `ADMIN_PASSWORD`), затем `npm --prefix admin install && npm --prefix admin run dev`, открыть `http://localhost:5173`.
- [ ] **Step 7: Commit** — `git commit -m "feat(admin-ui): vite react scaffold, api client, login"`

---

### Task 9: Каркас интерфейса — шапка со счётчиками и время города

**Files:**
- Create: `admin/src/layout/AppShellLayout.tsx`, `layout/CountersBar.tsx`, `realtime/useCounters.ts`, `lib/cityTime.ts`, `lib/format.ts`, `api/counters.ts`
- Test: `admin/src/lib/cityTime.test.ts`, `admin/src/layout/CountersBar.test.tsx`

**Interfaces:**
- Consumes: `GET /api/counters`, хаб `/hubs/admin` (`CountersChanged`), `Me.timeZone`.
- Produces:
  - `useCounters(): Counters | undefined` — начальное значение из `GET /api/counters` (Query-ключ `['counters']`), затем `HubConnectionBuilder().withUrl('/hubs/admin').withAutomaticReconnect()`; на `CountersChanged` — `queryClient.setQueryData(['counters'], c)` и `invalidateQueries({queryKey: ['stories']})`. Устанавливает `document.title = onReview > 0 ? \`(${onReview}) NewsFactory\` : 'NewsFactory'`.
  - `CountersBar({ counters, compact })` — «На проверке N · Обновления N · Ошибки N · Сегодня X/лимит (+N запланировано)»; клик по «Ошибки» → `/?tab=Errors`; при `compact` (ниже `sm`) — одна иконка-значок с суммой «на проверке» и раскрывающимся списком.
  - `lib/cityTime.ts`: `toCityIso(wallTime: string /* 'YYYY-MM-DD HH:mm' */, tz: string): string` — ISO с офсетом города; `formatInCity(isoUtc: string, tz: string, pattern = 'DD.MM HH:mm'): string`; `isPastInCity(wallTime, tz, now = Date.now()): boolean`.
  - `AppShellLayout` — Mantine `AppShell`: шапка (бургер на телефоне, `CountersBar`, кнопка «Выйти»), навигация «Очередь / Источники / Настройки».

- [ ] **Step 1: Написать падающие тесты**

```ts
// cityTime.test.ts
it('toCityIso interprets wall time in city zone', () => {
  // тест сам выставляет process.env.TZ = 'America/New_York' (браузер в другом поясе)
  expect(toCityIso('2026-10-27 09:00', 'Europe/Samara')).toBe('2026-10-27T09:00:00+04:00')
})
it('formatInCity shows Samara wall time for UTC instant', () => {
  expect(formatInCity('2026-10-26T21:30:00Z', 'Europe/Samara', 'DD.MM HH:mm')).toBe('27.10 01:30')
})

// CountersBar.test.tsx
it('renders all counters and today usage', () => { /* «На проверке 3», «Обновления 1», «Ошибки 2», «Сегодня 2/4 (+1)» */ })
it('compact mode shows single badge with review count', () => { })
```
- [ ] **Step 2: Запустить — падает.**
- [ ] **Step 3: Реализовать** (dayjs с плагинами `utc`, `timezone`, `customParseFormat`).
- [ ] **Step 4: Запустить — проходит.** + `npm --prefix admin run build`.
- [ ] **Step 5: Commit** — `git commit -m "feat(admin-ui): app shell, live counters, city time helpers"`

---

### Task 10: Очередь

**Files:**
- Create: `admin/src/queue/QueuePage.tsx`, `queue/StoryRow.tsx`, `api/stories.ts`
- Test: `admin/src/queue/QueuePage.test.tsx`

**Interfaces:**
- Consumes: `GET /api/stories?tab=…`, `formatInCity`.
- Produces:
  - `api/stories.ts`: `listStories(tab, skip, take)`, `getStory(id)`, `saveDraft`, `regenerate`, `generate`, `retry`, `reject`, `ackUpdates`, `publish`, `cancel`, `preview`, `uploadImage`, `imageFromSource`, `deleteImage` — по одной функции на маршрут Task 3–5; Query-ключи `['stories', tab, page]`, `['story', id]`.
  - `QueuePage` — вкладки «На проверке · Низкий интерес · Запланировано · Опубликовано · Отклонено · Ошибки» (вкладка в `?tab=`), счётчик элементов на вкладке; строка: оценка (бейдж, цвет по порогу: ≥ 8 зелёный, ≥ 6 жёлтый, иначе серый), заголовок, «N источн.», время в поясе города, значок «есть обновления»; на «Запланировано» — время публикации. Ниже `sm` — карточки вместо таблицы. Пустая вкладка — «Здесь пока пусто». Пагинация по 50.

- [ ] **Step 1: Написать падающие тесты** — `renders review items with score badge and updates icon`, `switches tab via query string and requests that tab`, `shows empty state`, `scheduled tab shows publication time in city zone` (API замокан через `vi.mock('../api/stories')`).
- [ ] **Step 2: Запустить — падает.**
- [ ] **Step 3: Реализовать.**
- [ ] **Step 4: Запустить — проходит.** + build.
- [ ] **Step 5: Commit** — `git commit -m "feat(admin-ui): story queue with tabs"`

---

### Task 11: Карточка сюжета

**Files:**
- Create: `admin/src/story/StoryPage.tsx`, `SourcesColumn.tsx`, `DraftEditor.tsx`, `MaxPostPreview.tsx`, `ImagePicker.tsx`, `PublishPanel.tsx`, `useDraftAutosave.ts`
- Test: `admin/src/story/MaxPostPreview.test.tsx`, `PublishPanel.test.tsx`, `useDraftAutosave.test.ts`, `DraftEditor.test.tsx`

**Interfaces:**
- Consumes: функции `api/stories.ts` (Task 10), `toCityIso`/`isPastInCity` (Task 9), `ApiError` (Task 8).
- Produces:
  - `MaxPostPreview({ text, imageUrl, tooLong, length, max })` — «пузырь» поста: картинка сверху (если есть), текст с поддержкой только `**жирного**`, переносов строк, `• ` пунктов; хэштеги подсвечены; **весь остальной текст экранируется** (никакого `dangerouslySetInnerHTML` для пользовательского текста без экранирования); при `tooLong` — красная плашка «Пост длиннее {max} символов ({length})».
  - `DraftEditor({ story, onSaved })` — поля заголовка, текста (`Textarea autosize`), хэштегов (`TagsInput`); список замечаний проверки фактов, каждое — фраза `claim` подсвечена в тексте (`Highlight`) и источник замечания («проверка чисел»/«LLM»); «Перегенерировать» с полем подсказки (подтверждение, если есть несохранённые правки); «История версий» — выбор версии показывает её текст (только просмотр) и кнопку «Взять за основу» (копирует в поля). Сохранение — кнопка «Сохранить» → `saveDraft`.
  - `useDraftAutosave(storyId, value)` — хранит несохранённые поля в `sessionStorage` под ключом `draft:{storyId}` (с debounce 500 мс), `restore()` возвращает сохранённое, `clear()` после успешного сохранения; все обращения к storage в `try/catch`.
  - `ImagePicker({ story })` — фото источников с бейджем «можно с подписью» / «нет разрешения» (для последних — подтверждение «У источника нет разрешения на фото. Всё равно использовать?» → `acknowledgeNoPermission: true`); загрузка своего файла (`FileButton`, accept `image/jpeg,image/png,image/webp,image/gif`) с полем подписи; выбранная картинка — радио-выбор, «без фото» по умолчанию; удаление прикреплённой.
  - `PublishPanel({ story, previewTooLong, selectedImageId, tz, today })` — кнопки «Опубликовать сейчас», «Запланировать» (Mantine `DateTimePicker`, значение трактуется как время города через `toCityIso`; прошедшее время — кнопка недоступна и подсказка), «Отклонить», «Отменить публикацию» (для `Scheduled`), «Повторить» (для `ProcessingFailed`), «Всё равно сгенерировать» (для `LowInterest`); строка «Сегодня X/лимит»; если `X + запланировано + 1 > лимит` — предупреждение до нажатия; ответ `409 daily_limit_exceeded` → модальное окно «Дневной лимит исчерпан (опубликовано N, запланировано M, лимит L). Опубликовать всё равно?» → повтор с `overrideLimit: true`; `previewTooLong` → кнопки публикации недоступны. Ниже `sm` — панель закреплена внизу экрана.
  - `StoryPage` — загрузка `getStory`; ПК: `Grid` из трёх колонок (Источники · Редактор · Превью, превью получает `preview()` с debounce 400 мс по текущим полям редактора и подписи выбранной картинки); ниже `sm` — `Tabs` «Источники · Редактор · Превью»; плашка «Появились новые подробности» с кнопкой «Просмотрено» (`ackUpdates`), если `hasUpdates`; плашка с `lastError`.

- [ ] **Step 1: Написать падающие тесты**

```tsx
// MaxPostPreview.test.tsx
it('renders bold, bullets and hashtags', () => { /* "**Заголовок**\n\n• раз\n\n#Отрадный" → <strong>Заголовок</strong>, элемент "• раз", хэштег с классом подсветки */ })
it('escapes html from text', () => { /* "<img src=x onerror=alert(1)>" показан как текст, в DOM нет <img> кроме картинки поста */ })
it('shows too-long warning', () => { /* tooLong, length 4100 → «Пост длиннее 4000 символов (4100)» */ })

// PublishPanel.test.tsx
it('warns before click when limit would be exceeded', () => { /* today {published:3, scheduled:1, limit:4} → видно предупреждение */ })
it('asks to override on 409 and retries with overrideLimit', async () => {
  // publish мок: 1-й вызов → ApiError(409,'daily_limit_exceeded',{published:4,scheduled:0,limit:4}), 2-й → ok
  // клик «Опубликовать сейчас» → модалка → «Опубликовать всё равно» → второй вызов с overrideLimit:true
})
it('disables publish when preview is too long', () => { })
it('schedules using city time zone', async () => { /* выбор 2026-10-27 09:00 → publish({at:'2026-10-27T09:00:00+04:00', …}) */ })

// useDraftAutosave.test.ts
it('restores unsaved text after remount', () => { /* записать поля → размонтировать → смонтировать заново → restore() вернул текст */ })
it('survives storage throwing', () => { /* sessionStorage.setItem бросает → хук не падает */ })

// DraftEditor.test.tsx
it('highlights fact-check claims in text', () => { })
it('confirms regenerate when there are unsaved edits', async () => { })
```
- [ ] **Step 2: Запустить — падает.**
- [ ] **Step 3: Реализовать.**
- [ ] **Step 4: Запустить — проходит.** + build.
- [ ] **Step 5: Commit** — `git commit -m "feat(admin-ui): story page with editor, max preview, photos and publish panel"`

---

### Task 12: Источники и настройки в интерфейсе

**Files:**
- Create: `admin/src/sources/SourcesPage.tsx`, `SourceForm.tsx`, `SourceTestResult.tsx`, `api/sources.ts`; `admin/src/settings/SettingsPage.tsx`, `CitySettings.tsx`, `ChannelSettings.tsx`, `LlmSettings.tsx`, `PromptSettings.tsx`, `UsageSettings.tsx`, `api/settings.ts`
- Test: `admin/src/sources/SourceForm.test.tsx`, `admin/src/settings/PromptSettings.test.tsx`

**Interfaces:**
- Consumes: эндпоинты Task 6–7.
- Produces:
  - `SourcesPage` — таблица (на телефоне — карточки): имя, тип, вкл/выкл, состояние (`Ok` зелёный / `Failing` красный «Ошибки подряд: N» / `Silent` жёлтый «Нет новостей дольше ожидаемого»), последний запуск, последняя ошибка; кнопка «Добавить»; клик — форма редактирования в `Drawer`.
  - `SourceForm` — поля из `SourceInput`; для `Html` — отдельные поля селекторов (`listUrl`, `itemSelector`, `linkSelector`, `titleSelector`, `dateSelector`, `dateFormat`, `bodySelector`, `imageSelector`) собираются в JSON `config`; для `Vk` — `screenName`; ошибки валидации сервера показываются у полей; кнопка «Проверить» → `POST /api/sources/test` → `SourceTestResult` (список извлечённого: заголовок, дата в поясе города, начало текста, число фото; или текст ошибки).
  - `SettingsPage` — вкладки «Город · Канал · LLM · Промпты · Расход»:
    - `CitySettings` — ключевые слова (`TagsInput`), порог оценки (`NumberInput` 1–10);
    - `ChannelSettings` — ID чата, дневной лимит, вкл/выкл; статус секретов «токен бота: настроен/не настроен»;
    - `LlmSettings` — провайдеры (форма, «ключ: настроен/не настроен»), таблица задач с выбором провайдера/модели/температуры;
    - `PromptSettings` — по задаче: список версий, редактор новой версии с подсказкой допустимых переменных `{{…}}`, «Сохранить как новую версию», «Сделать активной»; ошибка сервера с именем переменной — у поля;
    - `UsageSettings` — токены сегодня/за месяц, дневной бюджет (редактируемый, минимум 1000).

- [ ] **Step 1: Написать падающие тесты** — `SourceForm › builds html config json from selector fields`, `SourceForm › shows server validation error on field`, `SourceForm › test button renders extracted items`, `PromptSettings › shows unknown variable error from server`, `PromptSettings › activating older version calls api`.
- [ ] **Step 2: Запустить — падает.**
- [ ] **Step 3: Реализовать.**
- [ ] **Step 4: Запустить — проходит.** + build.
- [ ] **Step 5: Commit** — `git commit -m "feat(admin-ui): sources and settings pages"`

---

### Task 13: Сквозная проверка и ручная приёмка

**Files:**
- Create: `tests/NewsFactory.Api.Tests/AdminFlowTests.cs`
- Modify: `admin/README.md` (чек-лист приёмки)

**Interfaces:**
- Consumes: всё из Tasks 1–12.

- [ ] **Step 1: Написать сквозной API-тест** `AdminFlowTests.Moderator_flow_from_review_to_published` — через `AdminTestHost` с фейковыми LLM/публикатором и очередью, выполняющей задачи сразу (как `InlineQueue` в `EndToEndPipelineTests` плана 1): вход → `CollectSourceJob` для otradny.org (фейковый коллектор) → `GET /api/counters` (`OnReview == 1`) → `GET /api/stories?tab=Review` → `PUT draft` (правка заголовка) → `POST /api/preview` (не длинный) → загрузка картинки → `POST publish` с `imageId` → выполнение отложенной публикации → `GET /api/stories/{id}` (`Published`, у публикации `Published`), фейковый публикатор получил исправленный заголовок и картинку с подписью.
- [ ] **Step 2: Запустить** — `dotnet test` (с `NEWSFACTORY_TEST_PG`) → все PASS; `npm --prefix admin test` → PASS; `npm --prefix admin run build` → без ошибок.
- [ ] **Step 3: Чек-лист ручной приёмки в `admin/README.md`** (выполняет владелец на своём ПК): вход/выход; счётчики обновляются без перезагрузки при появлении нового черновика; правка и сохранение черновика; превью Max совпадает с реальным постом в тестовом канале (`MAX_CHANNEL_CHAT_ID` = тестовый канал); публикация сейчас и по расписанию на «через 5 минут» по Самаре; предупреждение и обход лимита; загрузка своего фото; «Проверить» для источника otradny.org; работа с телефона через SSH-тоннель (Termius/JuiceSSH, `localhost:8080` → прод в плане 3; локально — `localhost:5173`).
- [ ] **Step 4: Commit** — `git commit -m "test(admin): moderator flow end-to-end; acceptance checklist"`

---

## Не входит в этот план (план 3)

- Сборка SPA в образ nginx, `docker-compose.yml`, nginx-конфиг (`127.0.0.1:80`, прокси `/api`, `/hubs` с WebSocket, `/hangfire`), том для `Images:Root`, бэкапы, установка Russian Trusted Root CA, tzdata и ICU в образы, `NewsFactory.EvalBench`.
- Отложенные мелочи плана 1 (журнал `progress` плана 1, раздел «Deferred minors»), кроме закрытых здесь: длинный пост теперь ловится до публикации (Task 4).
