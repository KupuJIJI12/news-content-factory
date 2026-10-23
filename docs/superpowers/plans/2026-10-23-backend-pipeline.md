# NewsContentFactory — план 1: Backend-конвейер

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Работающий без UI backend: собирает новости из источников Отрадного, фильтрует, склеивает в сюжеты, оценивает, переписывает и проверяет через LLM, публикует в Max по команде сервиса публикации.

**Architecture:** Модульный монолит на .NET 10: проекты Domain, Infrastructure (EF Core + PostgreSQL), Collection, Llm, Processing, Publishing и хост Api с Hangfire. Конвейер — цепочка задач Hangfire, состояние в БД. Внешние системы (сайты, VK, LLM, Max) скрыты за интерфейсами и в тестах подменяются фейками.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core 10 + Npgsql, PostgreSQL 17, Hangfire + Hangfire.PostgreSql, AngleSharp, SmartReader, System.ServiceModel.Syndication, Microsoft.Extensions.Http.Resilience, Serilog, xUnit v3, Testcontainers.PostgreSql, Respawn, Microsoft.Extensions.TimeProvider.Testing.

**Spec:** `docs/superpowers/specs/2026-10-23-news-content-factory-design.md`

**Серия планов:** этот план — 1 из 3. План 2 — REST API админки + SignalR + React SPA. План 3 — docker compose, nginx, бэкапы, стенд сравнения LLM (EvalBench).

## Global Constraints

- Целевая платформа `net10.0`; `Nullable` = enable; `TreatWarningsAsErrors` = true; версии пакетов — только в `Directory.Packages.props` (central package management), последние стабильные, совместимые с net10.0.
- Все моменты времени — `DateTimeOffset` в UTC, колонки `timestamptz`. «День» (лимит, бюджет) — в часовом поясе города `Europe/Samara`, через `TimeZoneInfo.FindSystemTimeZoneById(city.TimeZone)`. Текущее время — только через `TimeProvider` (в тестах `FakeTimeProvider`).
- Граф зависимостей проектов: `Domain` ← `Infrastructure` ← {`Collection`, `Llm`, `Publishing`} ← `Processing`; `Api` ссылается на всё. `Publishing` не ссылается на `Llm`; `Collection` не ссылается на `Processing`.
- Секреты только из конфигурации/переменных окружения: `ConnectionStrings__Postgres`, `MAX_BOT_TOKEN`, `MAX_CHANNEL_CHAT_ID`, `VK_SERVICE_TOKEN`, `YANDEX_API_KEY`, `YANDEX_FOLDER_ID`, `GIGACHAT_AUTH_KEY`. В БД хранятся только имена переменных.
- Внутренний формат текста поста (`Draft.Body`): абзацы через пустую строку, `**жирный**`, пункты списка — строки, начинающиеся с `- `. Ссылок и другой разметки нет.
- Сбор: таймаут HTTP 30 с; не чаще 1 запроса в секунду на хост; User-Agent `NewsContentFactory/1.0`; элементы с `PublishedAt` старше 48 ч → `SkippedOld`.
- Значения по умолчанию: интервал опроса 60 мин, порог интересности 6, дневной лимит канала 4, окно склейки 72 ч, бюджет токенов 500 000 в сутки.
- LLM: невалидный ответ → ровно один повтор с текстом ошибки; HTTP 429/5xx → до 3 попыток с нарастающей паузой.
- Max API: база `https://platform-api2.max.ru`, заголовок `Authorization: <token>` (без `Bearer`), текст сообщения ≤ 4000 символов, `format: "markdown"`.
- Max и GigaChat используют сертификаты НУЦ Минцифры: корневой сертификат «Russian Trusted Root CA» должен быть доверенным на машине разработчика (для live-тестов) и в Docker-образе (план 3).
- Hangfire: проекты `Collection`, `Processing`, `Publishing` ссылаются на `Hangfire.Core` **только** ради атрибутов `[DisableConcurrentExecution]` и `[AutomaticRetry]` на методах джобов; постановка задач — только через `IPipelineQueue`/`ICollectQueue`.
- Коммит после каждой задачи, сообщения в стиле Conventional Commits.

## Review Focus

1. **Сайт отдаёт страницу в `windows-1251`** (типично для Bitrix, а otradny.org похож на Bitrix) → текст декодируется в нормальную кириллицу, а не в «РЎРѕ…». Тест: Task 4, `PoliteHttpFetcherTests.Decodes_windows_1251_from_meta_charset`.
2. **У новости нет даты или дата не разбирается** → новость не теряется и не падает: `PublishedAt = null`, при сохранении используется время сбора, статус не `SkippedOld`. Тесты: Task 4 (`RssCollectorTests.Item_without_pubDate_has_null_PublishedAt`), Task 7 (`CollectSourceJobTests.Item_without_date_is_not_skipped_as_old`).
3. **Граница суток в часовом поясе города** → публикация в 01:30 по Самаре (21:30 UTC прошлого дня) считается в лимит сегодняшнего дня. Тест: Task 3, `DailyLimitPolicyTests.Counts_by_city_local_date_not_utc_date`.
4. **Пост длиннее 4000 символов** → публикация падает с понятной ошибкой ещё до вызова API, без молчаливой обрезки. Тест: Task 14, `MaxMarkupConverterTests.Throws_when_longer_than_4000`.
5. **LLM обернула JSON в ```json … ``` или дописала текст после него** → ответ всё равно разбирается. Тест: Task 8, `LlmJsonTests.Parses_json_inside_markdown_fence_with_trailing_text`.

---

## Структура файлов

```
NewsFactory.sln
Directory.Build.props                 — net10.0, Nullable, TreatWarningsAsErrors, LangVersion latest
Directory.Packages.props              — версии пакетов
.gitignore, .editorconfig
src/NewsFactory.Domain/
  Entities/*.cs                       — по файлу на сущность из §5 спеки
  Enums.cs                            — все enum
  StoryStatusRules.cs                 — допустимые переходы статусов сюжета
  DailyLimitPolicy.cs                 — подсчёт дневного лимита
  Abstractions/IPipelineQueue.cs      — постановка задач конвейера
src/NewsFactory.Infrastructure/
  NewsFactoryDbContext.cs
  Configurations/*.cs                 — IEntityTypeConfiguration по сущности
  Migrations/*
  PostgresLocks.cs                    — advisory lock
src/NewsFactory.Collection/
  CollectedItem.cs, ISourceCollector.cs
  Http/IPageFetcher.cs, Http/PoliteHttpFetcher.cs, Http/HostThrottle.cs
  Text/HtmlText.cs                    — HTML → простой текст
  Rss/RssCollector.cs
  Html/HtmlSourceConfig.cs, Html/HtmlCollector.cs, Html/DateParsing.cs
  Vk/VkSourceConfig.cs, Vk/VkOptions.cs, Vk/VkCollector.cs
  Jobs/CollectSourceJob.cs, Jobs/CollectDueSourcesJob.cs
  SourceHealth.cs
src/NewsFactory.Llm/
  Results.cs                          — record-ы ответов LLM
  LlmJson.cs, PromptRenderer.cs
  Clients/ChatModels.cs, Clients/IChatCompletionClient.cs
  Clients/OpenAiCompatibleClient.cs, Clients/GigaChatClient.cs, Clients/GigaChatTokenProvider.cs
  ILlmService.cs, LlmService.cs, LlmBudgetGuard.cs, Exceptions.cs
  Prompts/*.md                        — промпты по умолчанию (embedded resources)
src/NewsFactory.Processing/
  KeywordMatcher.cs, SourceTextBuilder.cs, DeterministicFactChecker.cs
  RelevanceStage.cs, ClusteringStage.cs
  Jobs/ProcessRawItemJob.cs, Jobs/ProcessStoryJob.cs
src/NewsFactory.Publishing/
  PostContent.cs, IPublisher.cs, Exceptions.cs
  Max/MaxOptions.cs, Max/MaxMarkupConverter.cs, Max/MaxPublisher.cs
  PublicationService.cs, Jobs/PublishJob.cs
src/NewsFactory.Api/
  Program.cs, HangfirePipelineQueue.cs, SeedData.cs, AppOptions.cs, appsettings.json
tests/NewsFactory.TestSupport/        — PostgresFixture, RecordingPipelineQueue, FakeLlmService, StubHttpHandler, TestData
tests/NewsFactory.{Domain,Infrastructure,Collection,Llm,Processing,Publishing,Api}.Tests/
tests/NewsFactory.LiveTests/          — пропускаются без переменных окружения
```

---

### Task 1: Каркас решения и доменная модель

**Files:**
- Create: `NewsFactory.sln`, `Directory.Build.props`, `Directory.Packages.props`, `.gitignore` (`dotnet new gitignore`), `.editorconfig`
- Create: проекты `src/*` и `tests/*` из структуры выше (classlib / web / xunit v3), ссылки по графу из Global Constraints
- Create: `src/NewsFactory.Domain/Enums.cs`, `src/NewsFactory.Domain/Entities/*.cs`, `src/NewsFactory.Domain/StoryStatusRules.cs`, `src/NewsFactory.Domain/Abstractions/IPipelineQueue.cs`
- Test: `tests/NewsFactory.Domain.Tests/StoryStatusRulesTests.cs`

**Interfaces:**
- Produces:
  - `enum SourceType { Rss, Html, Vk }`
  - `enum RawItemStatus { New, SkippedOld, Irrelevant, Relevant, Clustered, Failed }`
  - `enum StoryStatus { New, Processing, LowInterest, ReadyForReview, Scheduled, Published, Rejected, ProcessingFailed, PublishFailed }`
  - `enum Platform { Max }`
  - `enum PublicationStatus { Scheduled, Sending, Published, Failed, Unknown, Cancelled }`
  - `enum LlmTask { Relevance, Clustering, Scoring, Rewrite, FactCheck }`
  - `enum LlmProviderKind { OpenAiCompatible, GigaChat }`
  - `enum FactCheckIssueSource { Deterministic, Llm }`; `record FactCheckIssue(string Claim, string Problem, FactCheckIssueSource Source)`
  - `abstract class Entity { long Id; DateTimeOffset CreatedAt; DateTimeOffset UpdatedAt; }` — все сущности наследуют.
  - Сущности с полями ровно по §5 спеки, плюс уточнения:
    - `City.ScoreThreshold int = 6`, `City.Keywords List<string>`
    - `Source.Config string?` (сырой JSON), `Source.LastItemAt DateTimeOffset?`
    - `RawItem.ImageUrls List<string>`, `RawItem.PublishedAt DateTimeOffset?`, `RawItem.CityId long?` (город, для которого новость признана релевантной)
    - `Story.Score int?`, `Story.Summary string`
    - `Draft.AuthorUserId long?` (null = LLM), `Draft.Hashtags List<string>`, `Draft.FactCheckIssues List<FactCheckIssue>`
    - `Publication.Attempts int`, `Publication.HangfireJobId string?`, `Publication.OverrideLimit bool`
    - `LlmProvider { Name, Kind, BaseUrl, ApiKeyEnvVar, List<string> Models, Dictionary<string,string> Headers, bool SupportsJsonSchema, string? AuthUrl, string? AuthScope }`
    - `LlmTaskSetting { LlmTask Task, long ProviderId, string Model, double Temperature }`
    - `PromptTemplate { LlmTask Task, int Version, string Text, bool IsActive }`
    - `AppSetting { string Key, string Value }`
  - `static class StoryStatusRules { bool CanTransition(StoryStatus from, StoryStatus to); }`
  - `Story.TransitionTo(StoryStatus next)` — бросает `InvalidStatusTransitionException`, если `CanTransition` = false.
  - `interface IPipelineQueue { void EnqueueRawItem(long rawItemId); void DelayRawItem(long rawItemId, DateTimeOffset runAt); void EnqueueStory(long storyId, bool forceDraft = false, string? hint = null); void DelayStory(long storyId, DateTimeOffset runAt); string SchedulePublication(long publicationId, DateTimeOffset runAt); void CancelJob(string jobId); }`

- [ ] **Step 1: Создать решение и проекты**

`dotnet new sln -n NewsFactory`, проекты и ссылки. Тестовые проекты: xunit v3 (`xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`). Run: `dotnet build` → Expected: `Build succeeded. 0 Warning(s)`.

- [ ] **Step 2: Написать падающий тест переходов статусов**

```csharp
public class StoryStatusRulesTests
{
    public static TheoryData<StoryStatus, StoryStatus> Allowed => new()
    {
        { StoryStatus.New, StoryStatus.Processing },
        { StoryStatus.Processing, StoryStatus.LowInterest },
        { StoryStatus.Processing, StoryStatus.ReadyForReview },
        { StoryStatus.Processing, StoryStatus.ProcessingFailed },
        { StoryStatus.LowInterest, StoryStatus.Processing },
        { StoryStatus.ReadyForReview, StoryStatus.Processing },
        { StoryStatus.ProcessingFailed, StoryStatus.Processing },
        { StoryStatus.ReadyForReview, StoryStatus.Scheduled },
        { StoryStatus.ReadyForReview, StoryStatus.Rejected },
        { StoryStatus.LowInterest, StoryStatus.Rejected },
        { StoryStatus.Scheduled, StoryStatus.Published },
        { StoryStatus.Scheduled, StoryStatus.PublishFailed },
        { StoryStatus.Scheduled, StoryStatus.ReadyForReview },
        { StoryStatus.PublishFailed, StoryStatus.Scheduled },
        { StoryStatus.PublishFailed, StoryStatus.ReadyForReview },
    };

    [Theory, MemberData(nameof(Allowed))]
    public void Allowed_transitions(StoryStatus from, StoryStatus to) =>
        Assert.True(StoryStatusRules.CanTransition(from, to));

    [Theory]
    [InlineData(StoryStatus.New, StoryStatus.Published)]
    [InlineData(StoryStatus.Published, StoryStatus.Processing)]
    [InlineData(StoryStatus.Rejected, StoryStatus.Processing)]
    [InlineData(StoryStatus.LowInterest, StoryStatus.Scheduled)]
    public void Forbidden_transitions(StoryStatus from, StoryStatus to) =>
        Assert.False(StoryStatusRules.CanTransition(from, to));

    [Fact]
    public void TransitionTo_throws_on_forbidden()
    {
        var story = new Story { Status = StoryStatus.Published };
        Assert.Throws<InvalidStatusTransitionException>(() => story.TransitionTo(StoryStatus.Processing));
    }
}
```

- [ ] **Step 3: Запустить — убедиться, что падает**

Run: `dotnet test tests/NewsFactory.Domain.Tests --filter "FullyQualifiedName~StoryStatusRulesTests"` → Expected: ошибка компиляции (`StoryStatusRules` не существует).

- [ ] **Step 4: Реализовать enum-ы, сущности, `StoryStatusRules`, `Story.TransitionTo`, `IPipelineQueue`**

Переходы — статический `HashSet<(StoryStatus, StoryStatus)>` ровно из списка `Allowed` выше.

- [ ] **Step 5: Запустить — убедиться, что проходит**

Run: та же команда → Expected: PASS (20 тестов).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(domain): solution skeleton, entities and story status rules"
```

---

### Task 2: БД — DbContext, маппинг, миграция, тестовая инфраструктура

**Files:**
- Create: `src/NewsFactory.Infrastructure/NewsFactoryDbContext.cs`, `Configurations/*.cs`, `PostgresLocks.cs`, `Migrations/*`
- Create: `tests/NewsFactory.TestSupport/PostgresFixture.cs`, `TestData.cs`
- Test: `tests/NewsFactory.Infrastructure.Tests/DbMappingTests.cs`, `PostgresLocksTests.cs`

**Interfaces:**
- Consumes: сущности Task 1.
- Produces:
  - `class NewsFactoryDbContext : DbContext` с `DbSet`: `Cities, Sources, SourceCities, RawItems, Stories, Drafts, StoryImages, Channels, Publications, Users, LlmProviders, LlmTaskSettings, PromptTemplates, LlmCalls, AppSettings`. `SaveChangesAsync` проставляет `CreatedAt`/`UpdatedAt` из внедрённого `TimeProvider`.
  - `static class PostgresLocks { Task AcquireCityLockAsync(NewsFactoryDbContext db, long cityId, CancellationToken ct); }` — `SELECT pg_advisory_xact_lock(1001, @cityId)`; требует открытой транзакции.
  - `PostgresFixture : IAsyncLifetime` (Testcontainers `postgres:17`): `NewsFactoryDbContext CreateContext(TimeProvider? time = null)`, `Task ResetAsync()` (Respawn, кроме `__EFMigrationsHistory`), `string ConnectionString`. Используется как `[CollectionDefinition("db")]`.
  - `static class TestData` — построители: `City Otradny()`, `Source RssSource(...)`, `RawItem RawItem(...)`, `Story Story(...)`, `Channel MaxChannel(...)`.

Маппинг: списки строк → `text[]`; `Source.Config` → `jsonb`; `Draft.FactCheckIssues`, `LlmProvider.Headers` → `jsonb` (`ToJson()` или `HasConversion` через System.Text.Json); enum → строки (`HasConversion<string>()`). Индексы: уникальный `RawItems(SourceId, ExternalId)`; уникальный `Cities(Slug)`; уникальный `Sources(Url)`; уникальный `Channels(CityId, Platform)`; уникальный `PromptTemplates(Task, Version)`; уникальный `LlmTaskSettings(Task)`; уникальный `AppSettings(Key)`; уникальный `Users(Login)`; индекс `Stories(CityId, CreatedAt)`; индекс `Publications(ChannelId, Status)`.

- [ ] **Step 1: Написать падающие тесты**

```csharp
[Collection("db")]
public class DbMappingTests(PostgresFixture db)
{
    [Fact]
    public async Task Story_with_draft_round_trips_lists_and_json()
    {
        await db.ResetAsync();
        await using (var ctx = db.CreateContext())
        {
            var city = TestData.Otradny();
            var story = TestData.Story(city);
            story.Drafts.Add(new Draft { Version = 1, Title = "T", Body = "B", IsCurrent = true,
                Hashtags = ["#Отрадный", "#новости"],
                FactCheckIssues = [new("15 ноября", "нет в источниках", FactCheckIssueSource.Deterministic)] });
            ctx.Add(story); await ctx.SaveChangesAsync();
        }
        await using var read = db.CreateContext();
        var d = await read.Drafts.SingleAsync();
        Assert.Equal(["#Отрадный", "#новости"], d.Hashtags);
        Assert.Equal("15 ноября", d.FactCheckIssues.Single().Claim);
        Assert.Equal(StoryStatus.New, (await read.Stories.SingleAsync()).Status);
    }

    [Fact]
    public async Task Duplicate_raw_item_external_id_per_source_is_rejected()
    {
        await db.ResetAsync();
        await using var ctx = db.CreateContext();
        var src = TestData.RssSource("https://a.example/rss");
        ctx.AddRange(TestData.RawItem(src, externalId: "x"), TestData.RawItem(src, externalId: "x"));
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
}

[Collection("db")]
public class PostgresLocksTests(PostgresFixture db)
{
    [Fact]
    public async Task Second_transaction_waits_for_city_lock()
    {
        await using var a = db.CreateContext(); await using var b = db.CreateContext();
        await using var ta = await a.Database.BeginTransactionAsync();
        await PostgresLocks.AcquireCityLockAsync(a, 1, default);
        await using var tb = await b.Database.BeginTransactionAsync();
        var waiting = PostgresLocks.AcquireCityLockAsync(b, 1, default);
        await Task.Delay(300);
        Assert.False(waiting.IsCompleted);
        await ta.CommitAsync();
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
```

- [ ] **Step 2: Запустить — убедиться, что падает**

Run: `dotnet test tests/NewsFactory.Infrastructure.Tests` → Expected: ошибка компиляции.

- [ ] **Step 3: Реализовать DbContext, конфигурации, `PostgresLocks`, `PostgresFixture` (применяет миграции через `Database.MigrateAsync`), `TestData`; создать миграцию**

Для `dotnet ef` добавить в Infrastructure `NewsFactoryDesignTimeFactory : IDesignTimeDbContextFactory<NewsFactoryDbContext>` (строка подключения из `ConnectionStrings__Postgres`, по умолчанию `Host=localhost;Database=newsfactory;Username=postgres;Password=postgres`). Run: `dotnet ef migrations add Initial -p src/NewsFactory.Infrastructure`.

- [ ] **Step 4: Запустить — убедиться, что проходит**

Run: `dotnet test tests/NewsFactory.Infrastructure.Tests` → Expected: PASS (нужен запущенный Docker).

- [ ] **Step 5: Commit** — `git commit -m "feat(infra): EF Core model, initial migration, postgres test fixture"`

---

### Task 3: Дневной лимит канала

**Files:**
- Create: `src/NewsFactory.Domain/DailyLimitPolicy.cs`
- Test: `tests/NewsFactory.Domain.Tests/DailyLimitPolicyTests.cs`

**Interfaces:**
- Produces:
  - `record DailyLimitCheck(int Published, int Scheduled, int Limit) { bool WouldExceed => Published + Scheduled + 1 > Limit; }`
  - `static DailyLimitCheck DailyLimitPolicy.Check(int limit, IEnumerable<Publication> channelPublications, DateTimeOffset publishAt, TimeZoneInfo tz)` — считает публикации канала, у которых локальная дата `(PublishedAt ?? ScheduledAt)` в `tz` равна локальной дате `publishAt`. `Published` = статусы `Published` + `Sending`; `Scheduled` = статус `Scheduled`. Остальные статусы не считаются.

- [ ] **Step 1: Написать падающие тесты**

```csharp
public class DailyLimitPolicyTests
{
    static readonly TimeZoneInfo Samara = TimeZoneInfo.FindSystemTimeZoneById("Europe/Samara");
    static Publication Pub(PublicationStatus s, string utc) =>
        new() { Status = s, ScheduledAt = DateTimeOffset.Parse(utc), PublishedAt = s == PublicationStatus.Published ? DateTimeOffset.Parse(utc) : null };

    [Fact]
    public void Counts_by_city_local_date_not_utc_date()
    {
        // 2026-10-23 21:30Z = 2026-10-24 01:30 по Самаре
        var pubs = new[] { Pub(PublicationStatus.Published, "2026-10-23T21:30:00Z") };
        var r = DailyLimitPolicy.Check(4, pubs, DateTimeOffset.Parse("2026-10-24T08:00:00Z"), Samara);
        Assert.Equal(1, r.Published);
    }

    [Fact]
    public void Splits_published_and_scheduled_and_ignores_cancelled_failed()
    {
        var pubs = new[] {
            Pub(PublicationStatus.Published, "2026-10-24T06:00:00Z"),
            Pub(PublicationStatus.Sending,   "2026-10-24T07:00:00Z"),
            Pub(PublicationStatus.Scheduled, "2026-10-24T15:00:00Z"),
            Pub(PublicationStatus.Cancelled, "2026-10-24T16:00:00Z"),
            Pub(PublicationStatus.Failed,    "2026-10-24T17:00:00Z") };
        var r = DailyLimitPolicy.Check(4, pubs, DateTimeOffset.Parse("2026-10-24T10:00:00Z"), Samara);
        Assert.Equal((2, 1), (r.Published, r.Scheduled));
        Assert.False(r.WouldExceed);
    }

    [Fact]
    public void WouldExceed_when_limit_reached()
    {
        var pubs = Enumerable.Range(0, 4).Select(i => Pub(PublicationStatus.Published, $"2026-10-24T0{i + 5}:00:00Z"));
        Assert.True(DailyLimitPolicy.Check(4, pubs, DateTimeOffset.Parse("2026-10-24T12:00:00Z"), Samara).WouldExceed);
    }

    [Fact]
    public void Scheduling_for_tomorrow_counts_tomorrow()
    {
        var pubs = Enumerable.Range(0, 4).Select(i => Pub(PublicationStatus.Published, $"2026-10-24T0{i + 5}:00:00Z"));
        Assert.False(DailyLimitPolicy.Check(4, pubs, DateTimeOffset.Parse("2026-10-25T08:00:00Z"), Samara).WouldExceed);
    }
}
```

- [ ] **Step 2: Запустить — падает.** Run: `dotnet test tests/NewsFactory.Domain.Tests --filter "FullyQualifiedName~DailyLimitPolicyTests"` → Expected: ошибка компиляции.
- [ ] **Step 3: Реализовать `DailyLimitPolicy.Check`.**
- [ ] **Step 4: Запустить — проходит.** Expected: PASS (4 теста).
- [ ] **Step 5: Commit** — `git commit -m "feat(domain): daily publication limit policy in city time zone"`

---

### Task 4: HTTP-загрузчик, HTML→текст, RSS-сборщик

**Files:**
- Create: `src/NewsFactory.Collection/CollectedItem.cs`, `ISourceCollector.cs`, `Http/IPageFetcher.cs`, `Http/PoliteHttpFetcher.cs`, `Http/HostThrottle.cs`, `Text/HtmlText.cs`, `Rss/RssCollector.cs`
- Create: `tests/NewsFactory.TestSupport/StubHttpHandler.cs`
- Create fixtures: `tests/NewsFactory.Collection.Tests/Fixtures/rss/synthetic.xml`, `rss/450media.xml`, `rss/samaragis.xml`, `http/cp1251.html`
- Test: `tests/NewsFactory.Collection.Tests/HostThrottleTests.cs`, `PoliteHttpFetcherTests.cs`, `HtmlTextTests.cs`, `RssCollectorTests.cs`

**Interfaces:**
- Consumes: `Source`, `SourceType` (Task 1).
- Produces:
  - `record CollectedItem(string ExternalId, string Url, string Title, string Text, DateTimeOffset? PublishedAt, IReadOnlyList<string> ImageUrls)`
  - `interface ISourceCollector { SourceType Type { get; } Task<IReadOnlyList<CollectedItem>> CollectAsync(Source source, CancellationToken ct); }`
  - `record FetchedPage(Uri FinalUrl, string Content)`; `interface IPageFetcher { Task<FetchedPage> GetAsync(Uri url, CancellationToken ct); Task<byte[]> GetBytesAsync(Uri url, CancellationToken ct); }`
  - `class HostThrottle(TimeProvider time, TimeSpan minInterval)` с `Task WaitAsync(string host, CancellationToken ct)` — следующий запрос к тому же хосту не раньше `minInterval` после предыдущего; разные хосты независимы.
  - `class PoliteHttpFetcher(HttpClient http, HostThrottle throttle) : IPageFetcher` — UA `NewsContentFactory/1.0`, таймаут 30 с (на `HttpClient`), кодировка: `charset` из `Content-Type` → иначе `<meta charset>`/`<meta http-equiv>` в первых 2048 байтах → иначе UTF-8. Регистрирует `CodePagesEncodingProvider`.
  - `static string HtmlText.ToPlainText(string html)` — блочные элементы (`p, div, br, h1-h6, li`) дают переносы; абзацы разделены `\n\n`; `li` → строка `- …`; скрипты/стили удалены; пробелы схлопнуты; HTML-сущности декодированы. Реализация на AngleSharp.
  - `class RssCollector(IPageFetcher fetcher) : ISourceCollector` (`Type = Rss`). Поля: `ExternalId` = `guid` ?? `link`; `Text` = `HtmlText.ToPlainText(content:encoded ?? description)`; `ImageUrls` = `enclosure`/`media:content` с image-типом + `src` всех `<img>` из HTML тела, абсолютные, без повторов; `PublishedAt` = `pubDate` или `null`, если его нет или он не разбирается. Парсинг — `SyndicationFeed.Load` с `XmlReaderSettings { DtdProcessing = Ignore }`; при ошибке разбора даты в `SyndicationFeed` — читать `pubDate` вручную из `ElementExtensions`/сырого XML.
  - `StubHttpHandler` (TestSupport): `Map(string url, HttpResponseMessage)`/`MapFile(url, path, contentType)`, запоминает запросы `Requests`.

- [ ] **Step 1: Подготовить фикстуры**

`synthetic.xml` — написать руками RSS 2.0 с тремя `item`: (1) `content:encoded` с `<p>`, `<ul><li>`, `<img src="/img/a.jpg">` и отличающимся `description`; (2) только `description` и `enclosure type="image/jpeg"`; (3) без `pubDate`. `cp1251.html` — HTML с `<meta charset="windows-1251">` и текстом «Отрадный», сохранённый в кодировке windows-1251 (сгенерировать скриптом через `Encoding.GetEncoding(1251)`).
Реальные ленты снять с машины с доступом к сайтам (компьютер разработчика или VPS):
```bash
curl -sL https://450media.ru/rss/ -o tests/NewsFactory.Collection.Tests/Fixtures/rss/450media.xml
curl -sL https://samaragis.ru/feed/ -o tests/NewsFactory.Collection.Tests/Fixtures/rss/samaragis.xml
```

- [ ] **Step 2: Написать падающие тесты**

```csharp
public class HostThrottleTests
{
    [Fact]
    public async Task Same_host_waits_min_interval_other_host_does_not()
    {
        var time = new FakeTimeProvider();
        var t = new HostThrottle(time, TimeSpan.FromSeconds(1));
        await t.WaitAsync("a.ru", default);
        var second = t.WaitAsync("a.ru", default);
        await t.WaitAsync("b.ru", default);            // не блокируется
        Assert.False(second.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));
        await second.WaitAsync(TimeSpan.FromSeconds(1));
    }
}

public class PoliteHttpFetcherTests
{
    [Fact]
    public async Task Decodes_windows_1251_from_meta_charset()
    {
        var page = await FetcherWith("http://x.ru/", "Fixtures/http/cp1251.html", "text/html").GetAsync(new("http://x.ru/"), default);
        Assert.Contains("Отрадный", page.Content);
    }
    [Fact]
    public async Task Sends_user_agent()
    { /* после GetAsync: handler.Requests.Single().Headers.UserAgent.ToString() == "NewsContentFactory/1.0" */ }
}

public class HtmlTextTests
{
    [Fact]
    public void Paragraphs_lists_and_entities() =>
        Assert.Equal("Первый абзац.\n\nВторой &.\n\n- пункт 1\n- пункт 2",
            HtmlText.ToPlainText("<p>Первый   абзац.</p><script>x()</script><p>Второй &amp;.</p><ul><li>пункт 1</li><li>пункт 2</li></ul>"));
}

public class RssCollectorTests
{
    [Fact]
    public async Task Prefers_content_encoded_and_collects_images()
    {
        var items = await Collect("Fixtures/rss/synthetic.xml");
        Assert.Contains("- пункт", items[0].Text);
        Assert.Equal(["https://feed.example/img/a.jpg"], items[0].ImageUrls);
        Assert.Equal(["https://feed.example/b.jpg"], items[1].ImageUrls);
    }
    [Fact]
    public async Task Item_without_pubDate_has_null_PublishedAt() =>
        Assert.Null((await Collect("Fixtures/rss/synthetic.xml"))[2].PublishedAt);

    [Theory]
    [InlineData("Fixtures/rss/450media.xml")]
    [InlineData("Fixtures/rss/samaragis.xml")]
    public async Task Real_feeds_parse(string path)
    {
        var items = await Collect(path);
        Assert.True(items.Count >= 5);
        Assert.All(items, i => { Assert.NotEmpty(i.Title); Assert.True(i.Text.Length > 100); Assert.StartsWith("https://", i.Url); Assert.NotNull(i.PublishedAt); });
    }
}
```
(`Collect` и `FetcherWith` — приватные хелперы теста: `StubHttpHandler` + `PoliteHttpFetcher` + `RssCollector` над `Source { Url = "https://feed.example/rss" }`.)

- [ ] **Step 3: Запустить — падает.** Run: `dotnet test tests/NewsFactory.Collection.Tests` → Expected: ошибка компиляции.
- [ ] **Step 4: Реализовать `HostThrottle`, `PoliteHttpFetcher`, `HtmlText`, `RssCollector`, `StubHttpHandler`.**
- [ ] **Step 5: Запустить — проходит.** Expected: PASS.
- [ ] **Step 6: Commit** — `git commit -m "feat(collection): polite fetcher, html-to-text, rss collector"`

---

### Task 5: HTML-сборщик

**Files:**
- Create: `src/NewsFactory.Collection/Html/HtmlSourceConfig.cs`, `Html/HtmlCollector.cs`, `Html/DateParsing.cs`
- Create fixtures: `Fixtures/html/synthetic-list.html`, `synthetic-article.html`, `otradny-list.html`, `otradny-article.html`, `63ru-list.html`, `63ru-article.html`
- Test: `tests/NewsFactory.Collection.Tests/HtmlCollectorTests.cs`, `DateParsingTests.cs`

**Interfaces:**
- Consumes: `IPageFetcher`, `HtmlText`, `CollectedItem` (Task 4).
- Produces:
  - `record HtmlSourceConfig(string ListUrl, string ItemSelector, string LinkSelector, string? TitleSelector = null, string? DateSelector = null, string? DateFormat = null, string? BodySelector = null, string? ImageSelector = null)` — хранится в `Source.Config` (JSON, camelCase).
  - `static DateTimeOffset? DateParsing.TryParse(string raw, string? format, TimeZoneInfo tz)` — с `format` → `ParseExact` (культура ru-RU); без формата — пробует `dd.MM.yyyy HH:mm`, `dd.MM.yyyy`, `d MMMM yyyy, HH:mm`, `d MMMM yyyy` (ru-RU, родительный падеж месяцев); результат — местное время `tz`, переведённое в UTC; неразобранное → `null`.
  - `class HtmlCollector(IPageFetcher fetcher) : ISourceCollector` (`Type = Html`): загружает `ListUrl`, для каждого `ItemSelector` берёт ссылку (`LinkSelector` → `href`, абсолютный относительно финального URL списка), заголовок (`TitleSelector` или текст ссылки), дату (`DateSelector` из элемента списка; если в списке нет — пытается тем же селектором на странице статьи); затем грузит статью: текст по `BodySelector` → `HtmlText.ToPlainText`, иначе SmartReader (`Reader.ParseArticle`) → его `TextContent`, приведённый `HtmlText`; картинки по `ImageSelector` (`src`/`data-src`, абсолютные). `ExternalId` = абсолютный URL статьи. Часовой пояс для дат — `Europe/Samara`. Падение загрузки одной статьи не роняет весь сбор: статья пропускается, ошибка логируется.

- [ ] **Step 1: Снять реальные страницы и подобрать селекторы**

```bash
curl -sL https://otradny.org/sitenews/ -o .../Fixtures/html/otradny-list.html
curl -sL "<URL первой новости из списка>" -o .../Fixtures/html/otradny-article.html
curl -sL https://63.ru/text/tags/otradnyy/ -o .../Fixtures/html/63ru-list.html
curl -sL "<URL первой новости из списка>" -o .../Fixtures/html/63ru-article.html
```
По сохранённым страницам подобрать `HtmlSourceConfig` для обоих сайтов и записать оба JSON в `tests/NewsFactory.Collection.Tests/Fixtures/html/configs.json` — эти же значения пойдут в сиды (Task 18). Синтетические страницы: список из 3 элементов с относительными ссылками и датами `23.10.2026`, статья с `<div class="text">` и `<img data-src="/i/1.jpg">`.

- [ ] **Step 2: Написать падающие тесты**

```csharp
public class DateParsingTests
{
    static readonly TimeZoneInfo Samara = TimeZoneInfo.FindSystemTimeZoneById("Europe/Samara");
    [Theory]
    [InlineData("23.10.2026", null, "2026-10-22T20:00:00Z")]
    [InlineData("23 октября 2026, 18:01", null, "2026-10-23T14:01:00Z")]
    [InlineData("28 сентября, 2026, 10:39", "d MMMM, yyyy, HH:mm", "2026-09-28T06:39:00Z")]
    public void Parses_local_dates(string raw, string? fmt, string utc) =>
        Assert.Equal(DateTimeOffset.Parse(utc), DateParsing.TryParse(raw, fmt, Samara));
    [Fact]
    public void Garbage_is_null() => Assert.Null(DateParsing.TryParse("вчера", null, Samara));
}

public class HtmlCollectorTests
{
    [Fact]
    public async Task Synthetic_list_resolves_relative_links_and_uses_body_selector()
    {
        var items = await Collect(SyntheticConfig, "https://site.example/news/");
        Assert.Equal(3, items.Count);
        Assert.Equal("https://site.example/news/a.html", items[0].Url);
        Assert.Equal(items[0].Url, items[0].ExternalId);
        Assert.DoesNotContain("<", items[0].Text);
        Assert.Equal(["https://site.example/i/1.jpg"], items[0].ImageUrls);
    }
    [Fact]
    public async Task Falls_back_to_smartreader_without_body_selector()
    {
        var items = await Collect(SyntheticConfig with { BodySelector = null }, "https://site.example/news/");
        Assert.True(items[0].Text.Length > 50);
    }
    [Fact]
    public async Task One_broken_article_does_not_fail_collection()
    {
        // статья b.html → 500
        Assert.Equal(2, (await Collect(SyntheticConfig, "https://site.example/news/", failing: "b.html")).Count);
    }
    [Theory]
    [InlineData("otradny")]
    [InlineData("63ru")]
    public async Task Real_sites_parse_with_configured_selectors(string site)
    {
        var items = await CollectReal(site); // config из configs.json, статья для всех ссылок — сохранённая *-article.html
        Assert.NotEmpty(items);
        Assert.All(items, i => { Assert.NotEmpty(i.Title); Assert.NotNull(i.PublishedAt); Assert.True(i.Text.Length > 100); });
    }
}
```

- [ ] **Step 3: Запустить — падает.** Run: `dotnet test tests/NewsFactory.Collection.Tests --filter "FullyQualifiedName~Html|FullyQualifiedName~DateParsing"` → Expected: ошибка компиляции.
- [ ] **Step 4: Реализовать `HtmlSourceConfig`, `DateParsing`, `HtmlCollector`.**
- [ ] **Step 5: Запустить — проходит.**
- [ ] **Step 6: Commit** — `git commit -m "feat(collection): html collector with selectors and smartreader fallback"`

---

### Task 6: VK-сборщик

**Files:**
- Create: `src/NewsFactory.Collection/Vk/VkSourceConfig.cs`, `Vk/VkOptions.cs`, `Vk/VkCollector.cs`, `Vk/VkApiException.cs`
- Create fixtures: `Fixtures/vk/resolve.json`, `Fixtures/vk/wall.json`, `Fixtures/vk/error.json`
- Test: `tests/NewsFactory.Collection.Tests/VkCollectorTests.cs`

**Interfaces:**
- Consumes: `CollectedItem`, `ISourceCollector`, `StubHttpHandler` (Task 4).
- Produces:
  - `record VkSourceConfig(string ScreenName, long? OwnerId = null)`
  - `class VkOptions { string ApiBaseUrl = "https://api.vk.com/method/"; string ApiVersion = "5.199"; string ServiceToken; }` (`ServiceToken` ← `VK_SERVICE_TOKEN`)
  - `class VkCollector(HttpClient http, IOptions<VkOptions> options) : ISourceCollector` (`Type = Vk`):
    - если `OwnerId` нет — `utils.resolveScreenName?screen_name=…` → `owner_id = -object_id` (для `type = "group"`), **записывает** обновлённый JSON конфига обратно в `source.Config` (сохранит вызывающий джоб);
    - `wall.get?owner_id=…&count=20` с `access_token` и `v` в query;
    - пропускает записи с `marked_as_ads = 1`; для репостов берёт `text`/вложения из `copy_history[0]`, если собственный `text` пуст; пропускает записи с пустым итоговым текстом;
    - `ExternalId = "{owner_id}_{id}"`, `Url = "https://vk.ru/wall{owner_id}_{id}"`, `PublishedAt` = `date` (unix), `Title` = первое предложение текста (до `.`, `!`, `?` или перевода строки), не длиннее 120 символов (обрезка по слову + `…`), `ImageUrls` = для каждого `photo`-вложения URL размера с максимальной `width*height`;
    - ответ с `error` → `VkApiException(code, message)`.

- [ ] **Step 1: Подготовить фикстуры** — `wall.json` (5 записей): обычная с фото (два размера), реклама, репост с пустым текстом и `copy_history`, запись только с фото без текста, запись с первым предложением длиннее 120 символов. `resolve.json`: `{"response":{"object_id":12345,"type":"group"}}`. `error.json`: `{"error":{"error_code":5,"error_msg":"User authorization failed"}}`.

- [ ] **Step 2: Написать падающие тесты**

```csharp
public class VkCollectorTests
{
    [Fact]
    public async Task Resolves_screen_name_once_and_stores_owner_id()
    {
        var source = new Source { Type = SourceType.Vk, Config = """{"screenName":"trkotradny"}""" };
        await Collect(source);
        Assert.Equal(-12345, JsonSerializer.Deserialize<VkSourceConfig>(source.Config!, Json.Web)!.OwnerId);
        await Collect(source);
        Assert.Single(Handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("utils.resolveScreenName"));
    }
    [Fact]
    public async Task Maps_posts_skips_ads_and_empty_uses_repost_text()
    {
        var items = await Collect(SourceWithOwner);
        Assert.Equal(3, items.Count);                                   // реклама и пост без текста отброшены
        Assert.Equal("-12345_101", items[0].ExternalId);
        Assert.Equal("https://vk.ru/wall-12345_101", items[0].Url);
        Assert.Equal(["https://sun.userapi.com/big.jpg"], items[0].ImageUrls);
        Assert.Contains("текст оригинала", items[1].Text);
        Assert.True(items[2].Title.Length <= 121 && items[2].Title.EndsWith("…"));
    }
    [Fact]
    public async Task Api_error_throws() =>
        Assert.Equal(5, (await Assert.ThrowsAsync<VkApiException>(() => CollectWithWall("Fixtures/vk/error.json"))).Code);
}
```

- [ ] **Step 3: Запустить — падает.** Run: `dotnet test tests/NewsFactory.Collection.Tests --filter "FullyQualifiedName~VkCollectorTests"`.
- [ ] **Step 4: Реализовать `VkCollector` и сопутствующие типы.**
- [ ] **Step 5: Запустить — проходит.**
- [ ] **Step 6: Commit** — `git commit -m "feat(collection): vk wall collector"`

---

### Task 7: Джобы сбора и здоровье источников

**Files:**
- Create: `src/NewsFactory.Collection/Jobs/CollectSourceJob.cs`, `Jobs/CollectDueSourcesJob.cs`, `SourceHealth.cs`
- Create: `tests/NewsFactory.TestSupport/RecordingPipelineQueue.cs`
- Test: `tests/NewsFactory.Collection.Tests/CollectSourceJobTests.cs`, `CollectDueSourcesJobTests.cs`, `SourceHealthTests.cs`

**Interfaces:**
- Consumes: `ISourceCollector` (Tasks 4–6), `NewsFactoryDbContext`, `PostgresFixture` (Task 2), `IPipelineQueue` (Task 1).
- Produces:
  - `class CollectSourceJob(NewsFactoryDbContext db, IEnumerable<ISourceCollector> collectors, IPipelineQueue queue, TimeProvider time, ILogger<CollectSourceJob> log)`; `Task RunAsync(long sourceId, CancellationToken ct)`:
    1. `LastRunAt = now`; вызвать коллектор по `source.Type`;
    2. ошибка → `ConsecutiveFailures++`, `LastError = ex.Message`, сохранить, **не** пробрасывать;
    3. успех → `ConsecutiveFailures = 0`, `LastError = null`, `LastSuccessAt = now`; для каждого элемента, чьего `(SourceId, ExternalId)` ещё нет в БД, создать `RawItem` (`CollectedAt = now`); `PublishedAt` старше `now − 48 ч` → `SkippedOld`, иначе `New`; если создан хотя бы один элемент — `LastItemAt = now`; сохранить (в том числе изменённый коллектором `source.Config`); для каждого нового `New` → `queue.EnqueueRawItem(id)`.
  - `class CollectDueSourcesJob(NewsFactoryDbContext db, ICollectQueue queue, TimeProvider time)`; `Task RunAsync(CancellationToken ct)` — для включённых источников с `LastRunAt == null || LastRunAt + PollIntervalMinutes <= now` вызывает `queue.EnqueueCollect(id)`.
  - `CollectSourceJob.RunAsync` помечен `[DisableConcurrentExecution(600)]`.
  - `interface ICollectQueue { void EnqueueCollect(long sourceId); }` — в `NewsFactory.Collection`.
  - `enum SourceHealthStatus { Ok, Failing, Silent }`; `static SourceHealthStatus SourceHealth.Evaluate(Source s, DateTimeOffset now)` — `ConsecutiveFailures >= 3` → `Failing`; иначе `ExpectedFrequencyHours` задан и `(LastItemAt ?? CreatedAt) + ExpectedFrequencyHours < now` → `Silent`; иначе `Ok`.
  - `RecordingPipelineQueue : IPipelineQueue` (TestSupport) — пишет вызовы в публичные списки `RawItems`, `Stories (Id, ForceDraft, Hint)`, `Delayed`, `Scheduled (PublicationId, RunAt, JobId)`, `Cancelled`.

- [ ] **Step 1: Написать падающие тесты** (с `PostgresFixture`, фейковым `ISourceCollector`, который возвращает заданный список или бросает)

```csharp
[Collection("db")]
public class CollectSourceJobTests(PostgresFixture db)
{
    [Fact] public async Task New_items_are_saved_once_and_enqueued() { /* два запуска с одинаковым списком из 2 элементов → в БД 2 RawItem, queue.RawItems.Count == 2 */ }
    [Fact] public async Task Old_items_are_skipped_and_not_enqueued() { /* PublishedAt = now-49h → Status SkippedOld, queue пуста */ }
    [Fact] public async Task Item_without_date_is_not_skipped_as_old() { /* PublishedAt = null → Status New, enqueued */ }
    [Fact] public async Task Failure_increments_counter_and_keeps_error() { /* 3 падения → ConsecutiveFailures == 3, LastError == сообщение; затем успех → 0 и null */ }
    [Fact] public async Task Collector_config_changes_are_persisted() { /* фейк меняет source.Config → после RunAsync в БД новый Config */ }
}

[Collection("db")]
public class CollectDueSourcesJobTests(PostgresFixture db)
{
    [Fact] public async Task Enqueues_only_due_enabled_sources() { /* A: LastRunAt null; B: now-61m при интервале 60; C: now-10m; D: disabled → enqueued [A, B] */ }
}

public class SourceHealthTests
{
    [Fact] public void Three_failures_is_failing() { }
    [Fact] public void No_items_longer_than_expected_is_silent() { /* LastItemAt = now-25h, Expected = 24 → Silent */ }
    [Fact] public void Within_expected_is_ok() { }
}
```
(Тела `{ }` выше — сценарии; в каждом тесте явные `Assert` по указанным значениям.)

- [ ] **Step 2: Запустить — падает.** Run: `dotnet test tests/NewsFactory.Collection.Tests --filter "FullyQualifiedName~Job|FullyQualifiedName~SourceHealth"`.
- [ ] **Step 3: Реализовать `CollectSourceJob`, `CollectDueSourcesJob`, `ICollectQueue`, `SourceHealth`, `RecordingPipelineQueue`.**
- [ ] **Step 4: Запустить — проходит.**
- [ ] **Step 5: Commit** — `git commit -m "feat(collection): collect jobs, dedupe, flood guard, source health"`

---

### Task 8: Разбор ответов LLM и шаблоны промптов

**Files:**
- Create: `src/NewsFactory.Llm/Results.cs`, `LlmJson.cs`, `PromptRenderer.cs`, `Exceptions.cs`
- Test: `tests/NewsFactory.Llm.Tests/LlmJsonTests.cs`, `PromptRendererTests.cs`

**Interfaces:**
- Produces:
  - Результаты (DataAnnotations для базовой валидации):
    - `record RelevanceResult(bool Relevant, [Required] string Reason)`
    - `record ClusteringResult(long? StoryId, [Required] string Reason, string? NewSummary)` — правило «`StoryId` из списка кандидатов» передаёт вызывающий через `extraValidation` (Task 11).
    - `record ScoringResult([Range(1, 10)] int Score, [Required] string Reason)`
    - `record RewriteResult([Required, MinLength(5)] string Title, [Required, MinLength(50)] string Body, [MinLength(3), MaxLength(4)] string[] Hashtags)`
    - `record LlmFactIssue([Required] string Claim, [Required] string Problem)`; `record FactCheckResult([Required] LlmFactIssue[] Issues)`
  - `record LlmParseResult<T>(T? Value, IReadOnlyList<string> Errors) { bool Ok => Errors.Count == 0; }`
  - `static LlmParseResult<T> LlmJson.Parse<T>(string raw, Func<T, IEnumerable<string>>? extraValidation = null)` — алгоритм: снять обёртку ```json/``` если есть; взять подстроку от первой `{` до парной ей `}` (с учётом строк и экранирования); десериализовать (`JsonSerializerDefaults.Web`, `PropertyNameCaseInsensitive`); прогнать `Validator.TryValidateObject(..., validateAllProperties: true)` (+ вложенные элементы массивов `LlmFactIssue`); затем `extraValidation`. Любая ошибка → `Errors` с человекочитаемым текстом на русском (он уйдёт в повторный запрос).
  - `static string PromptRenderer.Render(string template, IReadOnlyDictionary<string, string> vars)` — заменяет `{{name}}`; неизвестная переменная в шаблоне → `KeyNotFoundException` с именем; лишние переменные игнорируются.
  - `class LlmResponseInvalidException(LlmTask task, IReadOnlyList<string> errors) : Exception` (свойства `Task`, `Errors`); `class LlmBudgetExceededException(DateTimeOffset resumeAt) : Exception` (свойство `ResumeAt`)

- [ ] **Step 1: Написать падающие тесты**

```csharp
public class LlmJsonTests
{
    [Fact]
    public void Parses_json_inside_markdown_fence_with_trailing_text()
    {
        var r = LlmJson.Parse<ScoringResult>("Вот ответ:\n```json\n{\"score\": 7, \"reason\": \"важно {для} всех\"}\n```\nНадеюсь, помог!");
        Assert.True(r.Ok); Assert.Equal(7, r.Value!.Score);
    }
    [Fact]
    public void Out_of_range_score_is_error() => Assert.False(LlmJson.Parse<ScoringResult>("""{"score": 11, "reason": "x"}""").Ok);
    [Fact]
    public void Wrong_hashtag_count_is_error() =>
        Assert.False(LlmJson.Parse<RewriteResult>("""{"title":"Заголовок","body":"%s","hashtags":["#a"]}""".Replace("%s", new string('т', 60))).Ok);
    [Fact]
    public void Not_json_is_error_with_message() => Assert.NotEmpty(LlmJson.Parse<ScoringResult>("не знаю").Errors);
    [Fact]
    public void Extra_validation_runs()
    {
        var r = LlmJson.Parse<ClusteringResult>("""{"storyId": 99, "reason":"x"}""", c => c.StoryId is 99 ? ["storyId 99 нет в списке"] : []);
        Assert.Equal(["storyId 99 нет в списке"], r.Errors);
    }
}

public class PromptRendererTests
{
    [Fact] public void Replaces_variables() =>
        Assert.Equal("Город Отрадный", PromptRenderer.Render("Город {{city}}", new Dictionary<string, string> { ["city"] = "Отрадный", ["unused"] = "x" }));
    [Fact] public void Unknown_variable_throws() =>
        Assert.Throws<KeyNotFoundException>(() => PromptRenderer.Render("{{sources}}", new Dictionary<string, string>()));
}
```

- [ ] **Step 2: Запустить — падает.** Run: `dotnet test tests/NewsFactory.Llm.Tests --filter "FullyQualifiedName~LlmJson|FullyQualifiedName~PromptRenderer"`.
- [ ] **Step 3: Реализовать.**
- [ ] **Step 4: Запустить — проходит.**
- [ ] **Step 5: Commit** — `git commit -m "feat(llm): structured result parsing and prompt rendering"`

---

### Task 9: HTTP-клиенты LLM (OpenAI-совместимый и GigaChat)

**Files:**
- Create: `src/NewsFactory.Llm/Clients/ChatModels.cs`, `IChatCompletionClient.cs`, `OpenAiCompatibleClient.cs`, `GigaChatTokenProvider.cs`, `GigaChatClient.cs`, `LlmClientFactory.cs`
- Test: `tests/NewsFactory.Llm.Tests/OpenAiCompatibleClientTests.cs`, `GigaChatClientTests.cs`

**Interfaces:**
- Consumes: `LlmProvider` (Task 1), `StubHttpHandler` (Task 4).
- Produces:
  - `record ChatMessage(string Role, string Content)`; `record ChatRequest(string Model, IReadOnlyList<ChatMessage> Messages, double Temperature, JsonNode? JsonSchema)`; `record ChatCompletion(string Content, int PromptTokens, int CompletionTokens)`
  - `interface IChatCompletionClient { Task<ChatCompletion> CompleteAsync(ChatRequest request, CancellationToken ct); }`
  - `class OpenAiCompatibleClient(HttpClient http, LlmProvider provider, string apiKey) : IChatCompletionClient` — `POST {BaseUrl}/chat/completions`, `Authorization: Bearer {apiKey}`, плюс все `provider.Headers`; тело `{model, messages, temperature}` и, если `provider.SupportsJsonSchema && JsonSchema != null`, `response_format: {type:"json_schema", json_schema:{name:"result", schema: …}}`; ответ: `choices[0].message.content`, `usage.prompt_tokens`/`completion_tokens` (нет `usage` → 0). Ошибочный статус → `HttpRequestException` со статусом.
  - `class GigaChatTokenProvider(HttpClient http, LlmProvider provider, string authKey, TimeProvider time)`; `Task<string> GetTokenAsync(CancellationToken ct)` — `POST {AuthUrl}` с `Authorization: Basic {authKey}`, `RqUID: <новый Guid>`, форма `scope={AuthScope}`; ответ `{access_token, expires_at}` (`expires_at` — unix ms); кэширует до `expires_at − 60 с`.
  - `class GigaChatClient(HttpClient http, LlmProvider provider, GigaChatTokenProvider tokens) : IChatCompletionClient` — тот же формат запроса/ответа, что OpenAI-совместимый, на `{BaseUrl}/chat/completions` с `Authorization: Bearer {token}`; `response_format` не отправляет.
  - `class LlmClientFactory(IHttpClientFactory http, IConfiguration config, TimeProvider time)`; `IChatCompletionClient Create(LlmProvider provider)` — ключ = `config[provider.ApiKeyEnvVar]`, пусто → `InvalidOperationException("Не задана переменная {name}")`; токен-провайдеры GigaChat кэшируются по `provider.Id`. Именованный `HttpClient` `"llm"` с `AddStandardResilienceHandler` (повторы 429/5xx, 3 попытки) регистрируется в Task 18.

- [ ] **Step 1: Написать падающие тесты**

```csharp
public class OpenAiCompatibleClientTests
{
    [Fact]
    public async Task Sends_auth_headers_schema_and_reads_usage()
    {
        // provider: BaseUrl https://llm.api.cloud.yandex.net/v1, Headers {"OpenAI-Project":"folder1"}, SupportsJsonSchema = true
        var c = await client.CompleteAsync(new("gpt://folder1/yandexgpt-lite/latest", [new("user", "hi")], 0.2, JsonNode.Parse("""{"type":"object"}""")), default);
        var req = handler.Requests.Single();
        Assert.Equal("https://llm.api.cloud.yandex.net/v1/chat/completions", req.RequestUri!.ToString());
        Assert.Equal("Bearer key1", req.Headers.Authorization!.ToString());
        Assert.Equal("folder1", req.Headers.GetValues("OpenAI-Project").Single());
        Assert.Equal("json_schema", JsonNode.Parse(handler.Bodies.Single())!["response_format"]!["type"]!.GetValue<string>());
        Assert.Equal(("ответ", 10, 5), (c.Content, c.PromptTokens, c.CompletionTokens));
    }
    [Fact] public async Task Omits_response_format_when_not_supported() { /* SupportsJsonSchema=false → в теле нет "response_format" */ }
}

public class GigaChatClientTests
{
    [Fact]
    public async Task Fetches_token_once_and_refreshes_after_expiry()
    {
        // oauth отвечает {"access_token":"t1","expires_at": now+30мин в ms}
        await client.CompleteAsync(Req, default); await client.CompleteAsync(Req, default);
        Assert.Single(handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/oauth"));
        Assert.Equal("Bearer t1", handler.Requests.Last().Headers.Authorization!.ToString());
        time.Advance(TimeSpan.FromMinutes(30));
        await client.CompleteAsync(Req, default);
        Assert.Equal(2, handler.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("/oauth")));
    }
}
```
(`StubHttpHandler` дополнить списком `Bodies` с телами запросов.)

- [ ] **Step 2: Запустить — падает.** Run: `dotnet test tests/NewsFactory.Llm.Tests --filter "FullyQualifiedName~ClientTests"`.
- [ ] **Step 3: Реализовать клиентов и фабрику.**
- [ ] **Step 4: Запустить — проходит.**
- [ ] **Step 5: Commit** — `git commit -m "feat(llm): openai-compatible and gigachat chat clients"`

---

### Task 10: `LlmService` — маршрутизация, повтор, журнал, бюджет; промпты по умолчанию

**Files:**
- Create: `src/NewsFactory.Llm/ILlmService.cs`, `LlmService.cs`, `LlmBudgetGuard.cs`, `Prompts/relevance.md`, `Prompts/clustering.md`, `Prompts/scoring.md`, `Prompts/rewrite.md`, `Prompts/factcheck.md`, `DefaultPrompts.cs`
- Create: `tests/NewsFactory.TestSupport/FakeLlmService.cs`
- Test: `tests/NewsFactory.Llm.Tests/LlmServiceTests.cs`, `LlmBudgetGuardTests.cs`

**Interfaces:**
- Consumes: `LlmJson`, `PromptRenderer`, исключения (Task 8); `IChatCompletionClient`, `LlmClientFactory` (Task 9); `NewsFactoryDbContext` (Task 2).
- Produces:
  - `record LlmCallContext(long? StoryId = null, long? RawItemId = null)`
  - `interface ILlmService { Task<T> CompleteAsync<T>(LlmTask task, IReadOnlyDictionary<string, string> vars, LlmCallContext ctx, Func<T, IEnumerable<string>>? validate = null, CancellationToken ct = default); }`
  - `class LlmService(NewsFactoryDbContext db, ILlmClientResolver clients, LlmBudgetGuard budget, TimeProvider time) : ILlmService`. Алгоритм:
    1. `await budget.EnsureAvailableAsync(ct)`;
    2. настройка задачи `LlmTaskSettings` (+ провайдер), активный `PromptTemplate` задачи (наибольшая `Version` с `IsActive`);
    3. user-сообщение = `PromptRenderer.Render(template, vars)`; system-сообщение = `"Отвечай только JSON-объектом, соответствующим схеме:\n" + schema`; схема — `JsonSchemaExporter.GetJsonSchemaAsNode(JsonSerializerOptions.Web, typeof(T))`;
    4. вызов клиента; `LlmJson.Parse<T>(content, validate)`; при ошибках — один повтор с добавленными сообщениями `assistant: <прошлый ответ>` и `user: "Ответ не прошёл проверку: <ошибки>. Пришли исправленный JSON."`;
    5. каждый вызов (включая повтор и исключение HTTP) пишется в `LlmCalls` (`Request` = JSON сообщений, `Response` = контент, токены, `LatencyMs`, `Success`, `Error`);
    6. вторая неудача → `LlmResponseInvalidException`.
  - `interface ILlmClientResolver { IChatCompletionClient Resolve(LlmProvider provider); }` — реализуется `LlmClientFactory` (добавить реализацию интерфейса).
  - `class LlmBudgetGuard(NewsFactoryDbContext db, TimeProvider time, TimeZoneInfo appTimeZone)`; `Task EnsureAvailableAsync(CancellationToken ct)` — сумма `PromptTokens + CompletionTokens` вызовов с `CreatedAt` в текущих локальных сутках ≥ `AppSetting "DailyTokenBudget"` (нет записи → 500 000) → `LlmBudgetExceededException(resumeAt = начало следующих локальных суток в UTC)`.
  - `static class DefaultPrompts { string Get(LlmTask task); }` — читает embedded `Prompts/*.md`.
  - Содержание промптов по умолчанию (переменные — ровно эти):
    - `relevance.md` (`{{city}}`, `{{title}}`, `{{text}}`): решить, происходит ли событие в городе `{{city}}` или прямо касается его жителей; простое упоминание названия (трасса, направление, соседний район) — не про город. Поля `relevant`, `reason`.
    - `clustering.md` (`{{city}}`, `{{title}}`, `{{text}}`, `{{stories}}`): новость и список «id: краткое описание»; вернуть `storyId` сюжета об **том же событии** или `null`; при `null` заполнить `newSummary` — 1–2 предложения о событии.
    - `scoring.md` (`{{city}}`, `{{sources}}`): оценка интересности 1–10 для жителей города по критериям из §6.5 спеки (высоко/низко — дословно оттуда).
    - `rewrite.md` (`{{city}}`, `{{sources}}`, `{{hint}}`): дословно промпт редакции из §6.6 спеки, затем системная часть из §6.6 («только факты из источников; заголовок не обещает того, чего нет; не указывать источники»), описание внутреннего формата body из Global Constraints, затем «Пожелание редактора: {{hint}}» и «Источники:\n{{sources}}».
    - `factcheck.md` (`{{sources}}`, `{{draft}}`): найти в черновике и заголовке утверждения, которых нет в источниках или которые искажены (числа, даты, имена, адреса, масштаб события), и заголовки, преувеличивающие суть; пустой `issues`, если всё верно.
  - `FakeLlmService : ILlmService` (TestSupport) — `Respond<T>(LlmTask task, Func<IReadOnlyDictionary<string,string>, T> responder)`; запоминает `Calls (Task, Vars, Ctx)`; если задан `validate` — применяет его к ответу и бросает `LlmResponseInvalidException` при ошибках (чтобы стадии тестировались с тем же контрактом).

- [ ] **Step 1: Написать падающие тесты**

```csharp
[Collection("db")]
public class LlmServiceTests(PostgresFixture db)
{
    [Fact] public async Task Renders_active_template_routes_to_task_model_and_logs_call() { /* фейковый клиент возвращает {"score":7,"reason":"ok"}; в запросе Model == настройка Scoring; user-сообщение содержит отрендеренный текст; в LlmCalls 1 запись Success с токенами */ }
    [Fact] public async Task Retries_once_with_validation_error_then_succeeds() { /* 1-й ответ {"score":11,...}, 2-й валидный → результат 2-го; второй запрос содержит "Ответ не прошёл проверку"; LlmCalls == 2 */ }
    [Fact] public async Task Throws_after_second_invalid_answer() { /* оба невалидны → LlmResponseInvalidException; LlmCalls == 2, оба Success=false */ }
    [Fact] public async Task Uses_highest_active_template_version() { /* v1 active, v2 active, v3 inactive → текст v2 */ }
}

[Collection("db")]
public class LlmBudgetGuardTests(PostgresFixture db)
{
    [Fact] public async Task Throws_with_next_local_midnight_when_budget_spent() { /* budget 1000, вызовы на 1000 токенов сегодня (Самара) → resumeAt == 2026-10-24T20:00:00Z при now 2026-10-24T10:00Z */ }
    [Fact] public async Task Yesterdays_calls_do_not_count() { }
}
```

- [ ] **Step 2: Запустить — падает.** Run: `dotnet test tests/NewsFactory.Llm.Tests --filter "FullyQualifiedName~LlmService|FullyQualifiedName~Budget"`.
- [ ] **Step 3: Реализовать `LlmService`, `LlmBudgetGuard`, `ILlmClientResolver`, `DefaultPrompts` и файлы промптов (EmbeddedResource в csproj), `FakeLlmService`.**
- [ ] **Step 4: Запустить — проходит.**
- [ ] **Step 5: Commit** — `git commit -m "feat(llm): llm service with routing, retry, logging, budget and default prompts"`

---

### Task 11: Обработка сырой новости — фильтр по городу и склейка

**Files:**
- Create: `src/NewsFactory.Processing/KeywordMatcher.cs`, `RelevanceStage.cs`, `ClusteringStage.cs`, `Jobs/ProcessRawItemJob.cs`
- Test: `tests/NewsFactory.Processing.Tests/KeywordMatcherTests.cs`, `ProcessRawItemJobTests.cs`

**Interfaces:**
- Consumes: `ILlmService`, `RelevanceResult`, `ClusteringResult`, `LlmBudgetExceededException`, `LlmResponseInvalidException` (Tasks 8, 10); `PostgresLocks` (Task 2); `IPipelineQueue` (Task 1); `FakeLlmService`, `RecordingPipelineQueue` (TestSupport).
- Produces:
  - `static bool KeywordMatcher.Matches(string text, IEnumerable<string> keywords)` — регистронезависимо, `ё`≡`е`, совпадение основы как начала слова (`\b` + основа).
  - `class RelevanceStage(NewsFactoryDbContext db, ILlmService llm)`; `Task<City?> FindCityAsync(RawItem item, CancellationToken ct)` — по `SourceCities` источника в порядке `CityId`: `IsDedicated` → этот город; иначе ключевые слова города в `Title + Text` → LLM `Relevance` (`city`, `title`, `text` — текст обрезан до 6000 символов) → первый город с `relevant = true`. Нет города → `null`, `item.RelevanceReason` = причина последнего отказа LLM или `"нет ключевых слов"`.
  - `class ClusteringStage(NewsFactoryDbContext db, ILlmService llm, TimeProvider time)`; `Task<(Story Story, bool Created)> AssignAsync(RawItem item, City city, CancellationToken ct)` — должен вызываться внутри транзакции с `AcquireCityLockAsync`. Кандидаты: сюжеты города с `CreatedAt >= now − 72 ч` и `Status != Rejected`. Нет кандидатов → новый сюжет без вызова LLM, `Summary` = `item.Title`. Иначе LLM `Clustering` (`stories` = строки `"{id}: {summary}"`), `validate`: `storyId` не из списка → ошибка `"storyId {x} нет в списке"`. `null` → новый сюжет со `Summary = NewSummary ?? item.Title`.
  - `class ProcessRawItemJob(NewsFactoryDbContext db, RelevanceStage relevance, ClusteringStage clustering, IPipelineQueue queue)`; `Task RunAsync(long rawItemId, CancellationToken ct)`:
    1. обрабатывает только `Status == New` (иначе выход — идемпотентность);
    2. город не найден → `Irrelevant`;
    3. иначе `Relevant`, `CityId`; транзакция + `AcquireCityLockAsync(city.Id)` → `AssignAsync` → `item.StoryId`, `Clustered`; коммит;
    4. после коммита: новый сюжет → `queue.EnqueueStory(id)`; присоединение — правило §6.4.5 спеки: статус ∈ {`New`, `Processing`, `LowInterest`, `ReadyForReview`, `ProcessingFailed`} и текущий черновик отсутствует или `AuthorUserId == null` → `queue.EnqueueStory(id)`; иначе `story.HasUpdates = true` (сохранить), без постановки;
    5. `LlmBudgetExceededException` → `queue.DelayRawItem(id, ex.ResumeAt)`, статус не меняется;
    6. `LlmResponseInvalidException` → `Failed`, причина в `RelevanceReason`.

- [ ] **Step 1: Написать падающие тесты**

```csharp
public class KeywordMatcherTests
{
    [Theory]
    [InlineData("В ОТРАДНОМ отключат воду", true)]
    [InlineData("Отрадненский район", true)]
    [InlineData("Безотрадный день", false)]
    public void Matches_stem_at_word_start(string text, bool expected) => Assert.Equal(expected, KeywordMatcher.Matches(text, ["отрадн"]));
}

[Collection("db")]
public class ProcessRawItemJobTests(PostgresFixture db)
{
    [Fact] public async Task Dedicated_source_skips_llm_and_creates_story() { /* llm.Calls пуст; Status Clustered; queue.Stories == [story.Id] */ }
    [Fact] public async Task Regional_item_without_keywords_is_irrelevant_without_llm() { }
    [Fact] public async Task Regional_item_rejected_by_llm_is_irrelevant_with_reason() { /* "Отрадный — Богатое" → Relevant=false, reason="трасса" → Irrelevant, RelevanceReason == "трасса" */ }
    [Fact] public async Task Joins_existing_story_and_reprocesses_llm_draft() { /* сюжет ReadyForReview с LLM-черновиком, LLM Clustering → его id → StoryId == он, queue.Stories содержит его */ }
    [Fact] public async Task Joining_story_with_human_draft_only_flags_updates() { /* AuthorUserId = 1 → HasUpdates == true, queue.Stories пуст */ }
    [Fact] public async Task Joining_published_story_only_flags_updates() { }
    [Fact] public async Task Rejected_and_old_stories_are_not_candidates() { /* Rejected и созданный 73 ч назад → LLM Clustering не вызывается, создан новый сюжет */ }
    [Fact] public async Task Unknown_story_id_from_llm_marks_item_failed() { /* FakeLlm отвечает storyId=999 → validate → LlmResponseInvalidException → Failed */ }
    [Fact] public async Task Budget_exceeded_delays_item() { /* FakeLlm бросает LlmBudgetExceededException(resumeAt) → queue.Delayed == [(id, resumeAt)], Status New */ }
    [Fact] public async Task Two_parallel_items_about_same_event_make_one_story()
    {
        // FakeLlm Clustering: если кандидаты есть — вернуть первый id, иначе null.
        // Два RawItem одного выделенного источника, два ProcessRawItemJob в разных DbContext через Task.WhenAll.
        // Ожидание: в БД ровно 1 Story, у обоих RawItem одинаковый StoryId.
    }
}
```

- [ ] **Step 2: Запустить — падает.** Run: `dotnet test tests/NewsFactory.Processing.Tests --filter "FullyQualifiedName~KeywordMatcher|FullyQualifiedName~ProcessRawItemJob"`.
- [ ] **Step 3: Реализовать `KeywordMatcher`, `RelevanceStage`, `ClusteringStage`, `ProcessRawItemJob`.**
- [ ] **Step 4: Запустить — проходит.**
- [ ] **Step 5: Commit** — `git commit -m "feat(processing): city relevance filter and story clustering"`

---

### Task 12: Детерминированная проверка фактов и сборка текста источников

**Files:**
- Create: `src/NewsFactory.Processing/DeterministicFactChecker.cs`, `SourceTextBuilder.cs`
- Test: `tests/NewsFactory.Processing.Tests/DeterministicFactCheckerTests.cs`, `SourceTextBuilderTests.cs`

**Interfaces:**
- Consumes: `FactCheckIssue`, `FactCheckIssueSource` (Task 1).
- Produces:
  - `static IReadOnlyList<FactCheckIssue> DeterministicFactChecker.Check(string draftText, string sourcesText)` — извлекает из черновика:
    - даты `«15 ноября»` / `«15.11»` / `«15.11.2026»` → нормализует в пару (день, месяц);
    - время `10:00` / `10.00` / `10 часов` → (час, минута);
    - прочие числа (включая номера домов, `1,5`/`1.5`), кроме чисел внутри хэштегов и чисел, уже вошедших в дату/время;
    каждое значение ищется в таком же нормализованном множестве из `sourcesText`. Не найдено → `FactCheckIssue(<фрагмент из черновика>, "Нет в источниках", Deterministic)`, без повторов.
  - `record SourceText(string SourceName, string Title, string Text, DateTimeOffset? PublishedAt)`; `static string SourceTextBuilder.Build(IReadOnlyList<SourceText> items, int perItemMax = 6000, int totalMax = 20000)` — блоки `"### Источник {n}: {SourceName}\n{Title}\n{Text}"` через пустую строку, по возрастанию `PublishedAt`; текст каждого обрезается до `perItemMax` по границе слова с `…`; блоки добавляются, пока сумма ≤ `totalMax`; если первый блок сам длиннее — обрезается до `totalMax`.

- [ ] **Step 1: Написать падающие тесты**

```csharp
public class DeterministicFactCheckerTests
{
    const string Src = "15 ноября с 10:00 до 17.00 на ул. Гагарина, 47 не будет воды. Затронуты 1,5 тыс. жителей.";
    [Fact] public void Equivalent_forms_pass() =>
        Assert.Empty(DeterministicFactChecker.Check("**Без воды 15.11!**\n\nС 10.00 до 17:00, Гагарина 47, 1.5 тыс. человек. #Отрадный2026", Src));
    [Fact] public void Invented_number_and_date_are_reported()
    {
        var issues = DeterministicFactChecker.Check("16 ноября без воды останутся 3 тысячи жителей дома 47", Src);
        Assert.Equal(["16 ноября", "3"], issues.Select(i => i.Claim));
        Assert.All(issues, i => Assert.Equal(FactCheckIssueSource.Deterministic, i.Source));
    }
}

public class SourceTextBuilderTests
{
    [Fact] public void Orders_by_date_and_numbers_sources() { /* два элемента в обратном порядке → "### Источник 1: B" идёт первым */ }
    [Fact] public void Truncates_each_item_and_total()
    {
        var big = new string('а', 5000) + " конец";
        var text = SourceTextBuilder.Build(Enumerable.Range(0, 6).Select(i => new SourceText($"S{i}", "T", big, null)).ToList(), perItemMax: 3000, totalMax: 10000);
        Assert.True(text.Length <= 10000);
        Assert.DoesNotContain("конец", text);
        Assert.Contains("### Источник 3:", text);
        Assert.DoesNotContain("### Источник 4:", text);
    }
}
```

- [ ] **Step 2: Запустить — падает.** Run: `dotnet test tests/NewsFactory.Processing.Tests --filter "FullyQualifiedName~DeterministicFactChecker|FullyQualifiedName~SourceTextBuilder"`.
- [ ] **Step 3: Реализовать.**
- [ ] **Step 4: Запустить — проходит.**
- [ ] **Step 5: Commit** — `git commit -m "feat(processing): deterministic fact checker and source text builder"`

---

### Task 13: Обработка сюжета — оценка, рерайт, проверка фактов

**Files:**
- Create: `src/NewsFactory.Processing/Jobs/ProcessStoryJob.cs`
- Test: `tests/NewsFactory.Processing.Tests/ProcessStoryJobTests.cs`

**Interfaces:**
- Consumes: `ILlmService`, `ScoringResult`, `RewriteResult`, `FactCheckResult`, исключения LLM (Tasks 8, 10); `SourceTextBuilder`, `DeterministicFactChecker` (Task 12); `Story.TransitionTo` (Task 1).
- Produces:
  - `class ProcessStoryJob(NewsFactoryDbContext db, ILlmService llm, IPipelineQueue queue)`; `Task RunAsync(long storyId, bool forceDraft, string? hint, CancellationToken ct)`:
    1. сюжет в `Rejected`/`Scheduled`/`Published`/`PublishFailed` → выход без изменений;
    2. `TransitionTo(Processing)` (если уже `Processing` — продолжить), сохранить;
    3. `sources` = `SourceTextBuilder.Build` по всем `RawItems` сюжета (имя источника — `Source.Name`);
    4. если `hint == null` — оценка `Scoring` → `Score`, `ScoreReason`;
    5. `!forceDraft && Score < city.ScoreThreshold` → `LowInterest`, конец;
    6. рерайт `Rewrite` (`hint` → строка или `""`) → новый `Draft { Version = max+1, AuthorUserId = null, IsCurrent = true, RegenerationHint = hint }`, у прежних `IsCurrent = false`; хэштеги нормализуются (`#` в начале, без пробелов);
    7. проверка: `DeterministicFactChecker.Check(title + "\n" + body, sources)` + LLM `FactCheck` (`draft` = `title + "\n\n" + body`) → объединённый `FactCheckIssues` (LLM-замечания с `Source = Llm`);
    8. `ReadyForReview`, `HasUpdates = false`, сохранить;
    9. `LlmBudgetExceededException` → `queue.DelayStory(id, resumeAt)`, статус вернуть к прежнему (до шага 2);
    10. `LlmResponseInvalidException` или иная ошибка → `ProcessingFailed`, `LastError` = сообщение; не пробрасывать.

- [ ] **Step 1: Написать падающие тесты** (FakeLlmService, PostgresFixture)

```csharp
[Collection("db")]
public class ProcessStoryJobTests(PostgresFixture db)
{
    [Fact] public async Task Low_score_goes_to_low_interest_without_rewrite() { /* Score 4, порог 6 → LowInterest, Drafts пуст, Calls только Scoring */ }
    [Fact] public async Task High_score_creates_current_draft_with_fact_issues()
    {
        // Score 8; Rewrite: title "Без воды 16 ноября!", body (≥50 симв.), hashtags ["Отрадный","#новости","#ЖКХ"]; FactCheck: issues [{claim:"x",problem:"y"}]
        // Источник: "15 ноября не будет воды ..."
        // Ожидание: ReadyForReview; Draft v1 IsCurrent, AuthorUserId null; Hashtags[0] == "#Отрадный";
        //           FactCheckIssues содержит ("16 ноября", Deterministic) и ("x", Llm)
    }
    [Fact] public async Task Force_draft_ignores_threshold() { }
    [Fact] public async Task Hint_skips_rescoring_and_new_version_supersedes_old() { /* существующий v1 → после RunAsync(hint:"короче"): v2 IsCurrent, v1 !IsCurrent, Scoring не вызывался, Rewrite получил hint "короче" */ }
    [Fact] public async Task Invalid_llm_answer_marks_processing_failed() { }
    [Fact] public async Task Budget_exceeded_delays_and_restores_status() { /* был ReadyForReview → остаётся ReadyForReview, queue.Delayed содержит story */ }
    [Fact] public async Task Published_story_is_not_touched() { }
}
```

- [ ] **Step 2: Запустить — падает.** Run: `dotnet test tests/NewsFactory.Processing.Tests --filter "FullyQualifiedName~ProcessStoryJobTests"`.
- [ ] **Step 3: Реализовать `ProcessStoryJob`.**
- [ ] **Step 4: Запустить — проходит.**
- [ ] **Step 5: Commit** — `git commit -m "feat(processing): story scoring, rewrite and fact check job"`

---

### Task 14: Разметка Max

**Files:**
- Create: `src/NewsFactory.Publishing/PostContent.cs`, `Exceptions.cs`, `Max/MaxMarkupConverter.cs`
- Test: `tests/NewsFactory.Publishing.Tests/MaxMarkupConverterTests.cs`

**Interfaces:**
- Produces:
  - `record PostImage(string LocalPath, string? Credit)`; `record PostContent(string Title, string Body, IReadOnlyList<string> Hashtags, PostImage? Image)`
  - `class PostTooLongException(int length, int max) : Exception` — сообщение `"Пост длиннее {max} символов ({length})"`.
  - `static string MaxMarkupConverter.Convert(PostContent post)` — результат: `**{Title}**` + `\n\n` + body (строки `- x` → `• x`; `**…**` без изменений) + (`\n\nФото: {Credit}`, если есть `Image?.Credit`) + `\n\n` + хэштеги через пробел. Длина > 4000 → `PostTooLongException`.

- [ ] **Step 1: Написать падающие тесты**

```csharp
public class MaxMarkupConverterTests
{
    [Fact]
    public void Formats_title_bullets_credit_and_hashtags() =>
        Assert.Equal("**Заголовок**\n\nАбзац **важно**.\n\n• раз\n• два\n\nФото: otradny.org\n\n#Отрадный #новости",
            MaxMarkupConverter.Convert(new("Заголовок", "Абзац **важно**.\n\n- раз\n- два", ["#Отрадный", "#новости"], new("/x.jpg", "otradny.org"))));

    [Fact]
    public void No_credit_line_without_image() =>
        Assert.DoesNotContain("Фото:", MaxMarkupConverter.Convert(new("T", "B", ["#a"], null)));

    [Fact]
    public void Throws_when_longer_than_4000() =>
        Assert.Throws<PostTooLongException>(() => MaxMarkupConverter.Convert(new("T", new string('а', 4000), ["#a"], null)));
}
```

- [ ] **Step 2: Запустить — падает.** Run: `dotnet test tests/NewsFactory.Publishing.Tests --filter "FullyQualifiedName~MaxMarkupConverterTests"`.
- [ ] **Step 3: Реализовать.**
- [ ] **Step 4: Запустить — проходит.**
- [ ] **Step 5: Commit** — `git commit -m "feat(publishing): max markup converter with length guard"`

---

### Task 15: `MaxPublisher`

**Files:**
- Create: `src/NewsFactory.Publishing/IPublisher.cs`, `Max/MaxOptions.cs`, `Max/MaxPublisher.cs`
- Create fixtures: `tests/NewsFactory.Publishing.Tests/Fixtures/max/*.json`
- Test: `tests/NewsFactory.Publishing.Tests/MaxPublisherTests.cs`

**Interfaces:**
- Consumes: `PostContent`, `MaxMarkupConverter`, `PostTooLongException` (Task 14); `Channel`, `Platform` (Task 1); `StubHttpHandler`.
- Produces:
  - `record PublishResult(string? ExternalMessageId)`
  - `interface IPublisher { Platform Platform { get; } Task<PublishResult> PublishAsync(Channel channel, PostContent content, CancellationToken ct); }`
  - Исключения публикации (в `Exceptions.cs`): `PublisherTransientException` — сообщение **точно не отправлено** (нет соединения/DNS, 429, 5xx, любая ошибка загрузки картинки); `PublisherPermanentException` — 4xx, кроме 429; `PublisherAmbiguousException` — таймаут/обрыв **во время** запроса `POST /messages` (могло дойти).
  - `class MaxOptions { string BaseUrl = "https://platform-api2.max.ru"; string BotToken; TimeSpan[] AttachmentRetryDelays = [1s, 2s, 4s, 8s, 16s]; }` (`BotToken` ← `MAX_BOT_TOKEN`)
  - `class MaxPublisher(HttpClient http, IOptions<MaxOptions> options) : IPublisher` (`Platform = Max`):
    1. `text = MaxMarkupConverter.Convert(content)` (до любых запросов);
    2. если есть картинка: `POST {BaseUrl}/uploads?type=image` (`Authorization: {BotToken}`) → `{url, token?}`; `POST {url}` multipart, поле `data`, файл `LocalPath` → ответ `{"photos":{"<любой ключ>":{"token":"…"}}}` → берётся первый `token` (иначе `token` из первого ответа; нет ни одного → `PublisherTransientException`);
    3. `POST {BaseUrl}/messages?chat_id={channel.ExternalChatId}` с `{"text": text, "format": "markdown", "attachments": [{"type":"image","payload":{"token":…}}]}` (без картинки — без `attachments`);
    4. ответ 400 с `"code":"attachment.not.ready"` → ждать по очереди `AttachmentRetryDelays` и повторять; исчерпано → `PublisherTransientException`;
    5. успех → `ExternalMessageId` = `message.body.mid` (нет поля → `null`);
    6. классификация ошибок — как в описании исключений; `TaskCanceledException`/`HttpRequestException` без ответа на шаге 3 → `PublisherAmbiguousException`, на шаге 2 → `PublisherTransientException`.

- [ ] **Step 1: Написать падающие тесты**

```csharp
public class MaxPublisherTests
{
    [Fact]
    public async Task Sends_text_message_to_channel()
    {
        var r = await Publisher().PublishAsync(Channel("-100"), Post(image: null), default);
        var req = handler.Requests.Single();
        Assert.Equal("https://platform-api2.max.ru/messages?chat_id=-100", req.RequestUri!.ToString());
        Assert.Equal("tok", req.Headers.GetValues("Authorization").Single());
        var body = JsonNode.Parse(handler.Bodies.Single())!;
        Assert.Equal("markdown", body["format"]!.GetValue<string>());
        Assert.Null(body["attachments"]);
        Assert.Equal("mid.123", r.ExternalMessageId);
    }
    [Fact] public async Task Uploads_image_then_attaches_token() { /* 3 запроса: /uploads?type=image, upload url (multipart с полем "data"), /messages с attachments[0].payload.token == "ph1" */ }
    [Fact] public async Task Retries_when_attachment_not_ready() { /* /messages: 400 attachment.not.ready, затем 200 → успех; AttachmentRetryDelays = [0,0,0,0,0] */ }
    [Fact] public async Task Server_error_is_transient() => await Assert.ThrowsAsync<PublisherTransientException>(/* /messages → 503 */);
    [Fact] public async Task Unauthorized_is_permanent() => await Assert.ThrowsAsync<PublisherPermanentException>(/* 401 */);
    [Fact] public async Task Timeout_on_send_is_ambiguous() => await Assert.ThrowsAsync<PublisherAmbiguousException>(/* handler бросает TaskCanceledException на /messages */);
    [Fact] public async Task Too_long_post_fails_before_any_request()
    {
        await Assert.ThrowsAsync<PostTooLongException>(() => Publisher().PublishAsync(Channel("-100"), Post(body: new string('а', 4100)), default));
        Assert.Empty(handler.Requests);
    }
}
```

- [ ] **Step 2: Запустить — падает.** Run: `dotnet test tests/NewsFactory.Publishing.Tests --filter "FullyQualifiedName~MaxPublisherTests"`.
- [ ] **Step 3: Реализовать.**
- [ ] **Step 4: Запустить — проходит.**
- [ ] **Step 5: Commit** — `git commit -m "feat(publishing): max publisher with image upload and error classification"`

---

### Task 16: Сервис публикации и `PublishJob`

**Files:**
- Create: `src/NewsFactory.Publishing/PublicationService.cs`, `Jobs/PublishJob.cs`, `DailyLimitExceededException.cs`
- Test: `tests/NewsFactory.Publishing.Tests/PublicationServiceTests.cs`, `PublishJobTests.cs`

**Interfaces:**
- Consumes: `DailyLimitPolicy` (Task 3); `IPipelineQueue` (Task 1); `IPublisher`, исключения публикации (Task 15); `PostContent` (Task 14).
- Produces:
  - `class DailyLimitExceededException(DailyLimitCheck check) : Exception`
  - `class PublicationService(NewsFactoryDbContext db, IPipelineQueue queue, TimeProvider time)`:
    - `Task<IReadOnlyList<long>> ScheduleAsync(long storyId, long userId, DateTimeOffset? at, long? storyImageId, bool overrideLimit, CancellationToken ct)` — `at == null` = «сейчас». Требует статус `ReadyForReview` или `PublishFailed` и наличие текущего черновика. Для каждого включённого канала города: проверка `DailyLimitPolicy.Check` по публикациям канала на дату `at ?? now`; `WouldExceed && !overrideLimit` → `DailyLimitExceededException` (ничего не создаётся); создаётся `Publication { Status = Scheduled, ScheduledAt = at ?? now, DraftId = текущий, StoryImageId, ApprovedByUserId = userId, OverrideLimit }`; `HangfireJobId = queue.SchedulePublication(id, at ?? now)`. Сюжет → `Scheduled`. Возвращает id публикаций.
    - `Task CancelAsync(long storyId, CancellationToken ct)` — для публикаций сюжета в `Scheduled`: `queue.CancelJob(HangfireJobId)`, `Cancelled`; сюжет → `ReadyForReview`.
  - `class PublishJob(NewsFactoryDbContext db, IEnumerable<IPublisher> publishers, IPipelineQueue queue, TimeProvider time, ILogger<PublishJob> log)`; `Task RunAsync(long publicationId, CancellationToken ct)` с атрибутом `[AutomaticRetry(Attempts = 0)]`:
    1. статус `Published`/`Cancelled`/`Unknown` → выход;
    2. статус `Sending` → `Unknown`, `LastError = "Процесс прервался во время отправки — проверьте канал"`, сюжет → `PublishFailed`; выход **без** вызова публикатора;
    3. `Sending`, `Attempts++`, сохранить;
    4. собрать `PostContent` из `Draft` публикации и `StoryImage` (`LocalPath`, `Credit`), вызвать публикатор канала;
    5. успех → `Published`, `PublishedAt = now`, `ExternalMessageId`; если все неотменённые публикации сюжета `Published` → сюжет `Published`;
    6. `PublisherTransientException`: `Attempts < 3` → `Scheduled`, `HangfireJobId = queue.SchedulePublication(id, now + 2^Attempts мин)`; иначе `Failed` + сюжет `PublishFailed`;
    7. `PublisherPermanentException`, `PostTooLongException` → `Failed` + сюжет `PublishFailed`, `LastError`;
    8. `PublisherAmbiguousException` → `Unknown` + сюжет `PublishFailed`.

- [ ] **Step 1: Написать падающие тесты** (PostgresFixture, RecordingPipelineQueue, фейковый `IPublisher` со сценарием ответов)

```csharp
[Collection("db")]
public class PublicationServiceTests(PostgresFixture db)
{
    [Fact] public async Task Publish_now_creates_publication_and_schedules_immediately() { /* at null → Publication Scheduled, ScheduledAt == now, queue.Scheduled[0].RunAt == now, story Scheduled */ }
    [Fact] public async Task Exceeding_limit_without_override_throws_and_creates_nothing() { /* 4 Published сегодня → DailyLimitExceededException, Publications.Count == 4 */ }
    [Fact] public async Task Override_creates_publication_and_records_flag() { }
    [Fact] public async Task Cannot_publish_story_not_ready() { /* LowInterest → InvalidStatusTransitionException */ }
    [Fact] public async Task Cancel_returns_story_to_review_and_cancels_job() { }
}

[Collection("db")]
public class PublishJobTests(PostgresFixture db)
{
    [Fact] public async Task Success_marks_published_with_message_id() { }
    [Fact] public async Task Publication_left_in_sending_is_not_resent()
    {
        // Publication.Status = Sending (имитация падения процесса)
        // RunAsync → Status Unknown, story PublishFailed, publisher.Calls == 0
    }
    [Fact] public async Task Transient_error_reschedules_with_backoff_then_fails_after_3_attempts() { /* RunAt: now+2м, now+4м; третья ошибка → Failed, story PublishFailed */ }
    [Fact] public async Task Ambiguous_error_marks_unknown() { }
    [Fact] public async Task Cancelled_publication_is_skipped() { }
}
```

- [ ] **Step 2: Запустить — падает.** Run: `dotnet test tests/NewsFactory.Publishing.Tests --filter "FullyQualifiedName~PublicationService|FullyQualifiedName~PublishJob"`.
- [ ] **Step 3: Реализовать.**
- [ ] **Step 4: Запустить — проходит.**
- [ ] **Step 5: Commit** — `git commit -m "feat(publishing): publication service and safe publish job"`

---

### Task 17: Хост — Hangfire, DI, сиды, логи, health

**Files:**
- Create: `src/NewsFactory.Api/Program.cs`, `AppOptions.cs`, `HangfirePipelineQueue.cs`, `HangfireCollectQueue.cs`, `SeedData.cs`, `appsettings.json`, `appsettings.Development.json`
- Test: `tests/NewsFactory.Api.Tests/HostStartupTests.cs`, `SeedDataTests.cs`

**Interfaces:**
- Consumes: всё из Tasks 1–16.
- Produces:
  - `class AppOptions { string TimeZone = "Europe/Samara"; }` (секция `App`); `TimeZoneInfo` регистрируется синглтоном.
  - `class HangfirePipelineQueue(IBackgroundJobClient jobs) : IPipelineQueue` — `EnqueueRawItem` → `Enqueue<ProcessRawItemJob>`; `Delay*` и `SchedulePublication` → `Schedule(..., runAt)`; `EnqueueStory` → `Enqueue<ProcessStoryJob>(j => j.RunAsync(id, forceDraft, hint, CancellationToken.None))`; `CancelJob` → `Delete`.
  - `class HangfireCollectQueue(IBackgroundJobClient jobs) : ICollectQueue`.
  - `Program.cs`: Serilog (консоль + файл `logs/app-.log`, ежедневный, хранение 14 файлов); `AddDbContext<NewsFactoryDbContext>` (Npgsql, `ConnectionStrings:Postgres`); `TimeProvider.System`; Hangfire с `UsePostgreSqlStorage`, сервер (worker count 4), глобальный `AutomaticRetryAttribute { Attempts = 2 }` (у `PublishJob` свой атрибут с 0); `RecurringJob.AddOrUpdate<CollectDueSourcesJob>("collect-due-sources", j => j.RunAsync(CancellationToken.None), "*/5 * * * *")`; регистрация коллекторов (`ISourceCollector` × 3), `PoliteHttpFetcher` (именованный `HttpClient` с таймаутом 30 с), `HostThrottle` (1 с), `VkOptions`/`MaxOptions` из конфигурации (`Vk:ServiceToken` ← `VK_SERVICE_TOKEN`, `Max:BotToken` ← `MAX_BOT_TOKEN` — маппинг env-имён в `Program.cs`), LLM (`LlmClientFactory` как `ILlmClientResolver`, `HttpClient "llm"` с `AddStandardResilienceHandler`, `LlmBudgetGuard`, `LlmService`), стадии и джобы, `MaxPublisher` как `IPublisher`, `PublicationService`; при старте `Database.MigrateAsync()` → `SeedData.ApplyAsync`; `/health` (проверки Npgsql и Hangfire); `/hangfire` в плане 1 доступна только в `Development` (авторизация — план 2).
  - `static Task SeedData.ApplyAsync(NewsFactoryDbContext db, IConfiguration config, CancellationToken ct)` — идемпотентно (вставка только отсутствующего по естественному ключу, существующее не изменяется):
    - город `otradny` («Отрадный», `Europe/Samara`, `["отрадн"]`, порог 6);
    - источники из §3 спеки (ключ — `Url`): 450media RSS `https://450media.ru/rss/` (регион., 24 ч), samaragis RSS `https://samaragis.ru/feed/` (регион., 24 ч), otradny.org HTML (выделенный, 24 ч), 63.ru HTML (выделенный, 720 ч) — их `HtmlSourceConfig` переносятся константами в `SeedData` из `Fixtures/html/configs.json` (Task 5), VK `https://vk.ru/trkotradny` (`{"screenName":"trkotradny"}`, выделенный, 48 ч), vestnikotradnogo.ru и otradniy.bezformata.com — `IsEnabled = false`, `Config = null` (vestnik — выделенный, 72 ч; bezformata — регион., 24 ч); у всех интервал 60 мин, `PhotosAllowedWithCredit = false`;
    - канал Max города: `ExternalChatId` = `MAX_CHANNEL_CHAT_ID`, лимит 4, `IsEnabled` = переменная задана;
    - провайдеры: `YandexGPT` (OpenAiCompatible, `https://llm.api.cloud.yandex.net/v1`, ключ `YANDEX_API_KEY`, `Headers {"OpenAI-Project": YANDEX_FOLDER_ID}`, модели `gpt://{folder}/yandexgpt-lite/latest`, `gpt://{folder}/yandexgpt/latest`, `SupportsJsonSchema = true`) — только если `YANDEX_FOLDER_ID` задан, иначе предупреждение в лог; `GigaChat` (GigaChat, `https://gigachat.devices.sberbank.ru/api/v1`, ключ `GIGACHAT_AUTH_KEY`, `AuthUrl = https://ngw.devices.sberbank.ru:9443/api/v2/oauth`, `AuthScope = GIGACHAT_API_PERS`, модели `GigaChat-2`, `GigaChat-2-Pro`, `GigaChat-2-Max`, `SupportsJsonSchema = false`);
    - настройки задач (если YandexGPT создан): Relevance/Clustering/Scoring → `yandexgpt-lite`, температура 0.1; Rewrite → `yandexgpt`, 0.6; FactCheck → `yandexgpt`, 0.1;
    - промпты v1 всех задач из `DefaultPrompts`, `IsActive = true`;
    - `AppSetting DailyTokenBudget = 500000`.

- [ ] **Step 1: Написать падающие тесты** (`WebApplicationFactory<Program>` + `PostgresFixture`, строка подключения подменяется)

```csharp
[Collection("db")]
public class HostStartupTests(PostgresFixture db)
{
    [Fact]
    public async Task App_starts_migrates_and_reports_healthy()
    {
        await using var app = Factory(db);
        var resp = await app.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
    [Fact]
    public void All_jobs_resolve_from_di()
    {
        using var scope = Factory(db).Services.CreateScope();
        foreach (var t in new[] { typeof(CollectSourceJob), typeof(CollectDueSourcesJob), typeof(ProcessRawItemJob), typeof(ProcessStoryJob), typeof(PublishJob) })
            Assert.NotNull(scope.ServiceProvider.GetRequiredService(t));
    }
}

[Collection("db")]
public class SeedDataTests(PostgresFixture db)
{
    [Fact]
    public async Task Seed_is_idempotent_and_keeps_user_changes()
    {
        await db.ResetAsync();
        var cfg = Config(("YANDEX_FOLDER_ID", "f1"), ("MAX_CHANNEL_CHAT_ID", "-100"));
        await using (var ctx = db.CreateContext()) await SeedData.ApplyAsync(ctx, cfg, default);
        await using (var ctx = db.CreateContext()) { (await ctx.Channels.SingleAsync()).DailyLimit = 3; await ctx.SaveChangesAsync(); await SeedData.ApplyAsync(ctx, cfg, default); }
        await using var read = db.CreateContext();
        Assert.Equal(7, await read.Sources.CountAsync());
        Assert.Equal(3, (await read.Channels.SingleAsync()).DailyLimit);
        Assert.Equal(5, await read.PromptTemplates.CountAsync());
        Assert.Equal("gpt://f1/yandexgpt/latest", (await read.LlmTaskSettings.SingleAsync(s => s.Task == LlmTask.Rewrite)).Model);
    }
    [Fact] public async Task Without_folder_id_yandex_provider_is_not_seeded() { }
    [Fact] public async Task Without_chat_id_channel_is_disabled() { }
}
```

- [ ] **Step 2: Запустить — падает.** Run: `dotnet test tests/NewsFactory.Api.Tests`.
- [ ] **Step 3: Реализовать `Program.cs` (с `public partial class Program;` для тестов), очереди, `SeedData`, `AppOptions`, appsettings.**
- [ ] **Step 4: Запустить — проходит.** Затем полный прогон: `dotnet test` → Expected: все тесты PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat(api): host wiring, hangfire jobs, seed data, health"`

---

### Task 18: Live-тесты и ручная приёмка

**Files:**
- Create: `tests/NewsFactory.LiveTests/MaxLiveTests.cs`, `YandexLiveTests.cs`, `SourcesLiveTests.cs`, `README.md`

**Interfaces:**
- Consumes: `MaxPublisher` (Task 15), `LlmService`/`OpenAiCompatibleClient` (Tasks 9–10), коллекторы (Tasks 4–6).

Все тесты пропускаются через `Assert.SkipUnless(<переменная задана>, ...)`.

- [ ] **Step 1: Написать live-тесты**
  - `MaxLiveTests.Publishes_text_and_image_to_test_channel` — нужны `MAX_BOT_TOKEN`, `MAX_TEST_CHAT_ID`; публикует пост с заголовком `"[ТЕСТ] NewsContentFactory {время}"`, тело с абзацем, жирным и списком, хэштегами и картинкой `Fixtures/test.jpg`; `Assert.NotNull(result.ExternalMessageId)`.
  - `YandexLiveTests.Rewrites_fixture_story` — нужны `YANDEX_API_KEY`, `YANDEX_FOLDER_ID`; прогоняет `scoring`, `rewrite` и `factcheck` по тексту из `Fixtures/story.txt` (взять реальную новость otradny.org) через настоящий `LlmService` на Testcontainers-Postgres с сидами; проверяет, что ответы валидны, и печатает их в вывод теста.
  - `SourcesLiveTests.Source_collects(string url)` для всех 7 источников — нужен `LIVE_SOURCES=1` (+ `VK_SERVICE_TOKEN` для VK); печатает число элементов и первый заголовок; для vestnik и bezformata — только проверка, что страница открывается (HTTP 200), и вывод `<title>` и наличия `rss`-ссылок.
- [ ] **Step 2: Запустить без переменных** — Run: `dotnet test tests/NewsFactory.LiveTests` → Expected: все тесты Skipped.
- [ ] **Step 3: Ручная приёмка (выполняет владелец проекта на машине с российским IP и доверенным Russian Trusted Root CA)**
  1. Создать тестовый канал в Max, добавить бота администратором с правом `write`; узнать `chat_id` канала.
  2. `MAX_BOT_TOKEN=… MAX_TEST_CHAT_ID=… dotnet test tests/NewsFactory.LiveTests --filter MaxLiveTests` → пост с картинкой появился в канале, жирный текст и пункты отображаются.
  3. `YANDEX_API_KEY=… YANDEX_FOLDER_ID=… dotnet test … --filter YandexLiveTests` → PASS, рерайт читаем и соответствует стилю.
  4. `LIVE_SOURCES=1 VK_SERVICE_TOKEN=… dotnet test … --filter SourcesLiveTests` → результаты по всем 7 источникам записать в `tests/NewsFactory.LiveTests/README.md` (что доступно, есть ли RSS у vestnik/otradny.org).
- [ ] **Step 4: Commit** — `git commit -m "test: live checks for max, yandexgpt and sources"`

---

## Не входит в этот план (планы 2 и 3)

- REST API админки, аутентификация, SignalR-счётчики, кнопка «Проверить» для источника, загрузка картинок и выбор фото из источников (`StoryImage` заполняется в плане 2), React SPA — **план 2**.
- Dockerfile-ы, `docker-compose.yml`, nginx, certbot не нужен (SSH-тоннель), бэкапы `pg_dump`, установка Russian Trusted Root CA и tzdata в образ, `NewsFactory.EvalBench` — **план 3**.
