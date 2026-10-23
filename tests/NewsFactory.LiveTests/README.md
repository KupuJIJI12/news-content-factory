# Live-проверки

Тесты этого проекта ходят в настоящие сервисы и **пропускаются**, если нужные переменные окружения не заданы.
Запускать на машине с российским IP и доверенным корневым сертификатом «Russian Trusted Root CA»
(нужен для Max и GigaChat).

| Тест | Переменные | Что проверяет |
|---|---|---|
| `MaxLiveTests` | `MAX_BOT_TOKEN`, `MAX_TEST_CHAT_ID` | пост с картинкой, жирным и списком появляется в тестовом канале |
| `YandexLiveTests` | `YANDEX_API_KEY`, `YANDEX_FOLDER_ID`, `NEWSFACTORY_TEST_PG` (или Docker) | оценка, рерайт и проверка фактов реальной новости; ответы печатаются в вывод |
| `SourcesLiveTests` | `LIVE_SOURCES=1` (+ `VK_SERVICE_TOKEN` для VK) | все 7 источников отдают новости / открываются |

Пример (PowerShell):

```powershell
$env:MAX_BOT_TOKEN="..."; $env:MAX_TEST_CHAT_ID="..."
dotnet test --project tests/NewsFactory.LiveTests --filter-class "*MaxLiveTests" --output Detailed
```

## Результаты проверки источников

Проверено из облачной среды разработки 2026-10-23:

| Источник | Результат |
|---|---|
| 450media.ru/rss/ | ✅ RSS, полный текст в `yandex:full-text` |
| samaragis.ru/feed/ | ✅ RSS, полный текст в `content:encoded` |
| otradny.org/sitenews/ | ✅ HTML, селекторы подобраны |
| 63.ru/text/tags/otradnyy/ | ✅ HTML, селекторы по data-атрибутам |
| vk.ru/trkotradny | ⏳ нужен `VK_SERVICE_TOKEN` |
| vestnikotradnogo.ru | ❌ 403 для нашего User-Agent — проверить с российского IP |
| otradniy.bezformata.com | ❌ недоступен — проверить с российского IP |
