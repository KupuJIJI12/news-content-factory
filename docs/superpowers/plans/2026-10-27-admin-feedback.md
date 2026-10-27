# План 2.1: доработки админки по обратной связи от 2026-10-27

**Spec:** `docs/superpowers/specs/2026-10-23-news-content-factory-design.md` (§2 масштабирование на N городов, §6 конвейер, §9 админка).
**База:** ветка `feat/admin` (PR #2), работа продолжается в ней.

## Контекст и решения владельца

Обратная связь (11 пунктов) и решения по вопросам:
- **Пункт 5, пауза:** замораживается только сбор новостей. Уже собранное LLM дообрабатывает.
- **Пункт 8, автонастройка HTML-источника:** сначала RSS-лента сайта, затем эвристика по структуре страницы, затем LLM. Результат проверяется и показывается на подтверждение.
- **Пункт 2, города:** список городов делается сейчас. У каждого города свои источники, канал, ключевые слова и порог; в очереди фильтр по городу.
- **Пункт 6:** если повторная обработка прошла без ошибки, ошибка сюжета должна исчезнуть.

Диагноз багов:
- **Пункты 3, 10 и 11.** Канал Max создаётся сидером один раз, при первом старте. Тогда `MAX_CHANNEL_CHAT_ID` не был задан, поэтому `ExternalChatId=""`, а `IsEnabled=false`. Сидер ничего не перезаписывает, поэтому дальше:
  - превью строится только по включённым каналам, и список пуст;
  - публикация падает на `InvalidOperationException("У города нет включённых каналов")`, а `Guard` её не ловит, отсюда 500.
- **Пункт 6.** `ProcessStoryJob` чистит `LastError` только на ветке успешной генерации. Повтор, закончившийся `LowInterest`, оставляет старую ошибку.
- **Пункт 7.** `ExternalId` у HTML — это абсолютный URL, у RSS — guid или link. Одна статья с вариантом ссылки (параметры, слэш, схема, `www`) записывается как новая.
- **Пункт 4.** Не баг: `{{…}}` — переменные шаблона. В интерфейсе нет их расшифровки.

## Global Constraints

- Секреты только в переменных окружения; ID чата секретом не является и хранится в БД.
- Перечисления в БД хранятся строками, поэтому новые значения enum не требуют миграции.
- TDD: тест пишется первым, сначала падает, затем проходит. Бэкенд: `dotnet test` с `NEWSFACTORY_TEST_PG`. Фронтенд: `npm --prefix admin test` и `npm --prefix admin run build`.
- Время показывается в поясе города сюжета или источника.

## Review Focus

1. **Повторный старт с заданным `MAX_CHANNEL_CHAT_ID`, если в БД канал уже правили руками.** Значение в БД не перетирается; заполняется только пустое.
2. **Источник привязан к двум городам: у одного «весь про этот город», у другого фильтр по ключевым словам.** Релевантность считается по каждому городу правильно, флаг одного города не влияет на другой.
3. **Пауза сбора включена.** Ручная «Проверить» и автонастройка источника по-прежнему работают, а плановый сбор не ставит ни одной задачи.
4. **Автонастройка на странице без списка новостей** (главная-визитка, капча, 403). Понятное сообщение «не удалось», а не 500. Время ограничено.
5. **Нормализация URL.** Ссылки на разные статьи не склеиваются: параметры вроде `?id=123` и `?ELEMENT_ID=` сохраняются, удаляются только трекинговые.

---

### Task 1: Канал из настроек и понятные ошибки публикации (п. 3, 10, 11)

**Files:** `src/NewsFactory.Api/SeedData.cs`, `src/NewsFactory.Publishing/PublicationService.cs`, `src/NewsFactory.Api/Admin/Stories/StoryEndpoints.cs`, `src/NewsFactory.Api/Admin/Preview/PreviewEndpoints.cs`, `admin/src/story/StoryPage.tsx`, `admin/src/api/types.ts`. Тесты: `SeedDataTests`, `StoryActionsTests`, `PreviewTests`, `StoryPage.test.tsx`.

**Interfaces — Produces:**
- `NoEnabledChannelException(cityName)` в `NewsFactory.Publishing`. Эндпоинт publish отвечает `409`, `code: "no_channel"`, текст «У города {name} нет включённого канала — включите его в Настройки → Города».
- `PreviewRequest` получает `long? CityId`. `PreviewResult` получает `bool ChannelConfigured`. Если у города нет включённых каналов, возвращается один результат: `ChannelId 0`, `ChannelName "Max"`, `ChannelConfigured false`.

- [ ] Тесты (RED):
  - `SeedDataTests.Fills_empty_channel_chat_id_from_config_and_enables_it`;
  - `SeedDataTests.Does_not_overwrite_channel_chat_id_edited_in_ui`;
  - `StoryActionsTests.Publish_without_enabled_channel_is_409_no_channel`;
  - `PreviewTests.Preview_without_enabled_channel_still_renders_max`;
  - `StoryPage.test › shows hint when channel is not configured`.
- [ ] Реализация → GREEN. Commit `fix: channel chat id from config, preview and publish without channel`.

### Task 2: Ошибка сюжета пропадает после успешного повтора (п. 6)

**Files:** `src/NewsFactory.Processing/Jobs/ProcessStoryJob.cs`. Тест: `ProcessStoryJobTests.Retry_ending_in_low_interest_clears_last_error`.

- [ ] RED → `LastError = null` при переходе в `Processing` (при новом сбое ошибка запишется снова) → GREEN. Commit `fix: clear story error when a retry succeeds`.

### Task 3: Дубли внутри одного источника (п. 7)

**Files:** `src/NewsFactory.Collection/Text/UrlNormalizer.cs` (новый), `Html/HtmlCollector.cs`, `Rss/RssCollector.cs`, `Jobs/CollectSourceJob.cs`, `src/NewsFactory.Domain/Enums.cs` (`RawItemStatus.Duplicate`).

**Interfaces — Produces:**
- `UrlNormalizer.Normalize(string url)`:
  - схема приводится к `https`, хост — к нижнему регистру, `www.` убирается;
  - фрагмент `#…` удаляется, слэш на конце пути удаляется (кроме корня);
  - удаляются `utm_*`, `yclid`, `gclid`, `fbclid`, `from`, `ref`, `_openstat`; остальные параметры сохраняются в исходном порядке.
- `CollectSourceJob` помечает элемент как `Duplicate` и не отправляет его в обработку, если у того же источника за последние 72 часа есть элемент с тем же нормализованным заголовком: нижний регистр, без пунктуации и лишних пробелов, не короче 15 символов.

- [ ] Тесты (RED):
  - `UrlNormalizerTests` — theory на 8 случаев, включая сохранение `?id=`;
  - `HtmlCollectorTests.External_id_is_normalized_url`;
  - `CollectSourceJobTests.Same_title_from_same_source_is_duplicate`;
  - `CollectSourceJobTests.Same_title_from_other_source_is_not_duplicate`.
- [ ] GREEN. Commit `fix: normalize item urls and skip same-title duplicates within a source`.

### Task 4: Счётчики на всех вкладках очереди (п. 1)

**Files:** `StoryQueries.cs`, `StoryEndpoints.cs`, `admin/src/api/stories.ts`, `admin/src/queue/QueuePage.tsx`.

**Interfaces — Produces:** `GET /api/stories/counts?cityId=` → `{ "Review": n, "LowInterest": n, "Scheduled": n, "Published": n, "Rejected": n, "Errors": n }`. Ключ запроса `['stories','counts',cityId]` обновляется вместе с `['stories']`.

- [ ] Тесты: `StoryQueriesTests.Counts_per_tab`, `QueuePage.test › shows counts on every tab`. Сначала RED, затем GREEN. Commit `feat(admin): counts on every queue tab`.

### Task 5: Переменные промптов с пояснениями (п. 4)

**Files:** `src/NewsFactory.Llm/PromptVariables.cs`, `Admin/Settings/*`, `admin/src/settings/PromptSettings.tsx`.

**Interfaces — Produces:**
- `PromptVariables.Describe(string name)` возвращает описание по-русски.
- `PromptTaskDto.Variables` — список `PromptVariableDto(Name, Description, Required)`.
- В интерфейсе таблица «Переменная — что подставит система — обязательна».

- [ ] Тесты: `PromptVariablesTests.Every_variable_has_description`, `PromptSettings.test › explains variables`. Сначала RED, затем GREEN. Commit `feat(admin): explain prompt variables`.

### Task 6: Пауза сбора новостей (п. 5)

**Files:** `src/NewsFactory.Collection/CollectionPause.cs` (новый), `Jobs/CollectDueSourcesJob.cs`, `Admin/Collection/CollectionEndpoints.cs` (новый), `CountersService.cs`, `admin/src/layout/*`, `admin/src/api/collection.ts`.

**Interfaces — Produces:**
- `CollectionPause`: `IsPausedAsync`, `SetAsync(bool)`. Хранится в `AppSettings["collection.paused"]`.
- API: `GET /api/collection` → `{ paused }`, `PUT /api/collection { paused }`.
- `Counters.CollectionPaused`. В шапке переключатель «Сбор новостей» и бейдж «Сбор на паузе».
- `CollectDueSourcesJob` при паузе ничего не ставит. «Проверить» и автонастройка работают.

- [ ] Тесты:
  - `CollectDueSourcesJobTests.Paused_collection_enqueues_nothing`;
  - `CollectionEndpointsTests.Toggle_pause_round_trip`;
  - `CountersBar.test › shows paused badge`.

  Сначала RED, затем GREEN. Commit `feat: pause news collection`.

### Task 7: Города — API (п. 2, 9)

**Files:** `Admin/Cities/*` (новые), `Admin/Sources/*`, `Admin/Settings/*` (каналы), `Admin/Stories/*`, `Admin/Auth/AuthEndpoints.cs`.

**Interfaces — Produces:**
- `GET /api/cities` → `CityDto(Id, Name, Slug, TimeZone, Keywords, ScoreThreshold, Channel: ChannelSettingsDto?, SourcesCount)`.
- `POST /api/cities` и `PUT /api/cities/{id}` с `CityInput(Name, TimeZone, Keywords, ScoreThreshold)`. Валидация:
  - имя обязательно и уникально;
  - пояс находится через `TimeZoneInfo.TryFindSystemTimeZoneById`;
  - ключевых слов не меньше одного;
  - порог от 1 до 10.

  `Slug` генерируется транслитом, при совпадении к нему добавляется `-2` и так далее.
- `PUT /api/cities/{id}/channel` с `ChannelSettingsInput` создаёт или обновляет канал Max города. `ChannelSettingsDto` получает `CityId`, `CityName`.
- `SourceDto` получает `Cities: [SourceCityDto(CityId, CityName, IsDedicated)]` вместо `IsDedicated`. `SourceInput.Cities: [SourceCityInput(CityId, IsDedicated)]`, нужен хотя бы один существующий город. `GET /api/sources?cityId=`.
- `StoryListItem` и `StoryDetail` получают `CityId`, `CityName`, `TimeZone`. `GET /api/stories?tab=&cityId=` и `counts?cityId=`.
- `/api/settings/city` удаляется: его заменяет `/api/cities`.

- [ ] Тесты:
  - `CitiesEndpointsTests.Create_validate_update`;
  - `CitiesEndpointsTests.Channel_upsert_per_city`;
  - `SourceEndpointsTests.Source_links_to_several_cities_with_own_dedicated_flag`;
  - `StoryQueriesTests.List_and_counts_filter_by_city`.

  Существующие тесты исправить под новые DTO. Сначала RED, затем GREEN. Commit `feat(api): cities with own sources and channels`.

### Task 8: Города — интерфейс (п. 2, 9)

**Files:** `admin/src/settings/CitiesSettings.tsx` (вместо `CitySettings` и `ChannelSettings`), `admin/src/api/cities.ts`, `admin/src/sources/*`, `admin/src/queue/QueuePage.tsx`, `admin/src/story/*`, `admin/src/layout/CountersBar.tsx`.

- **Настройки, вкладка «Города»:** список карточек (город, канал, лимит, число источников) и кнопка «Добавить город». В Drawer:
  - название, часовой пояс, ключевые слова с подсказкой, порог интереса;
  - блок «Канал Max»: ID чата, лимит, вкл/выкл, статус токена бота.
- **Форма источника:** выбор городов (`MultiSelect`). Для каждого выбранного города переключатель «Все новости этого источника — про {город}» с пояснением: «Включите для сайта или паблика самого города. Выключите для областных СМИ: из их новостей возьмутся только те, где есть ключевые слова города».
- **Очередь и источники:** фильтр по городу (показывается, если городов больше одного), бейдж города в строке.
- **Страница сюжета:** время в поясе города сюжета, бейдж города.
- **Шапка:** «Сегодня» по каждому каналу, например «Отрадный 1/4».

- [ ] Тесты:
  - `CitiesSettings.test › creates city and saves channel`;
  - `SourceForm.test › sends cities with dedicated flags`;
  - `QueuePage.test › filters by city`.

  Сначала RED, затем GREEN, плюс build. Commit `feat(admin-ui): cities`.

### Task 9: Автонастройка источника — бэкенд (п. 8)

**Files:** `src/NewsFactory.Collection/AutoSetup/*` (новые: `FeedDiscovery`, `ListPageAnalyzer`, `SourceAutoSetup`), `src/NewsFactory.Llm/Prompts/sourcesetup.md`, `Results.cs` (`SourceSetupResult`), `Enums.cs` (`LlmTask.SourceSetup`), `PromptVariables` (`page`), `SeedData.cs` (задача по умолчанию GigaChat-2-Pro), `Admin/Sources/SourceEndpoints.cs`.

**Interfaces — Produces:**
- `POST /api/sources/autodetect { url }` → `AutoSetupResult(Ok, Method: "Rss"|"Heuristic"|"Llm"|null, Type, Url, Config, Items: SourceTestItem[], Message)`. Общий таймаут 90 с.
- **Шаг 1, RSS:**
  - ищутся `<link rel="alternate" type="application/rss+xml|atom+xml">` и стандартные пути `/rss`, `/rss/`, `/rss.xml`, `/feed`, `/feed/`, `/rss/news`;
  - адрес принимается, если `RssCollector` вернул хотя бы одну новость.
- **Шаг 2, эвристика (`ListPageAnalyzer.Suggest(html, baseUrl)`):**
  - кандидаты — группы из трёх и больше соседних элементов с одинаковыми тегом и классами, в каждом ссылка на тот же сайт с текстом от 15 символов;
  - оценка учитывает число элементов, длину текста ссылок, наличие даты (`DateParsing`) и долю ссылок, похожих на статьи;
  - селекторы строятся из классов: `itemSelector`, `linkSelector`, `dateSelector`;
  - лучший кандидат проверяется настоящим `HtmlCollector`: нужно три и больше новостей с заголовком и текстом от 200 символов хотя бы у одной.
- **Шаг 3, LLM:** в задачу `SourceSetup` передаётся скелет страницы (теги, классы, первые 80 символов текста, до 12 000 символов). Ответ — `SourceSetupResult(ItemSelector, LinkSelector, TitleSelector?, DateSelector?, DateFormat?)`. Проверка та же, что на шаге 2.
- Пауза сбора на автонастройку не влияет.

- [ ] Тесты:
  - `FeedDiscoveryTests.Finds_feed_by_link_tag`;
  - `FeedDiscoveryTests.Tries_common_paths`;
  - `ListPageAnalyzerTests.Finds_otradny_news_list` и `Finds_63ru_news_list` (по реальным фикстурам);
  - `ListPageAnalyzerTests.Page_without_list_gives_nothing`;
  - `SourceAutoSetupTests.Falls_back_to_llm_and_validates`;
  - `SourceAutoSetupTests.Reports_failure_without_exception`.

  Сначала RED, затем GREEN. Commit `feat: automatic source setup (rss, page structure, llm)`.

### Task 10: Автонастройка — интерфейс (п. 8)

**Files:** `admin/src/sources/SourceForm.tsx`, `admin/src/api/sources.ts`.

- Новый источник начинается с поля «Адрес сайта» и кнопки «Настроить автоматически». Результат:
  - заполняет тип, адрес и конфиг;
  - показывает найденные новости через `SourceTestResult` и способ: «найдена RSS-лента», «определено по структуре страницы» или «подобрано LLM»;
  - при неудаче показывает сообщение и открывает ручную настройку.
- Поля селекторов скрыты под «Ручная настройка».

- [ ] Тест: `SourceForm.test › autodetect fills the form and shows found items`. Сначала RED, затем GREEN, плюс build. Commit `feat(admin-ui): automatic source setup`.

---

## Финал

Полный `dotnet test`, `npm --prefix admin test`, build. Независимое ревью всей ветки начиная с `7fb987d`. Push в `feat/admin`, PR #2 обновится.
