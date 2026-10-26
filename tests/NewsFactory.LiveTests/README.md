# Live-проверки

Тесты этого проекта ходят в настоящие сервисы и **пропускаются**, если нужные переменные окружения не заданы.
Запускать на машине с российским IP и доверенным корневым сертификатом «Russian Trusted Root CA»
(нужен для Max и GigaChat).

| Тест | Переменные | Что проверяет |
|---|---|---|
| `MaxLiveTests` | `MAX_BOT_TOKEN`, `MAX_TEST_CHAT_ID` | пост с картинкой, жирным и списком появляется в тестовом канале |
| `GigaChatLiveTests` | `GIGACHAT_AUTH_KEY` (+ `GIGACHAT_SCOPE`, по умолчанию `GIGACHAT_API_PERS`), `NEWSFACTORY_TEST_PG` (или Docker) | **основной провайдер:** оценка (GigaChat-2), рерайт (GigaChat-2-Max) и проверка фактов (GigaChat-2-Pro) реальной новости; ответы и расход токенов печатаются в вывод |
| `YandexLiveTests` | `YANDEX_API_KEY`, `YANDEX_FOLDER_ID`, `NEWSFACTORY_TEST_PG` (или Docker) | необязательный провайдер: тот же сценарий на YandexGPT |
| `SourcesLiveTests` | `LIVE_SOURCES=1` (+ `VK_SERVICE_TOKEN` для VK) | все 7 источников отдают новости / открываются |

`GIGACHAT_AUTH_KEY` — «Authorization key» из личного кабинета GigaChat API (developers.sber.ru → проект GigaChat API → «Получить ключ»), уже в формате Base64.

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
