# Dental Admin — админ-панель для стоматологических клиник и сетей

Полное описание продукта и архитектуры — в [SPEC.md](SPEC.md). Этот README будет дописан на этапе 9.

## Что нужно на машине
- .NET 10 SDK
- Node.js 22+ и npm
- Docker Desktop (для Compose и Testcontainers)
- Git
- Claude Code

## Как запустить разработку через Claude Code
```powershell
cd C:\Users\kuany\Downloads\stom
git init
git add . ; git commit -m "chore: spec and agent files"
claude
```
Первая команда агенту:

> Прочитай CLAUDE.md, затем SPEC.md целиком. Реализуй проект по этапам из SPEC §16, начиная с этапа 1. После каждого этапа запускай сборку, миграции с нуля, линт и тесты, обновляй PROGRESS.md и делай коммит. Решения по пробелам спецификации записывай в DECISIONS.md. Не останавливайся с вопросами.

Для следующих сессий:

> Прочитай CLAUDE.md и PROGRESS.md и продолжи с текущего этапа.

## Запуск и проверка (после Этапа 1)
| Файл | Что делает |
|---|---|
| `start.bat` | проверяет Docker/.NET 10/Node, поднимает Postgres/Redis/Mailpit/MinIO/Seq, собирает, применяет миграции и seed, открывает API и фронт в отдельных окнах, открывает браузер и запускает проверку ролей. Ключи: `-NoSeed`, `-Worker`, `-NoCheck`, `-NoBrowser` |
| `check-roles.bat` | проверяет сайт (health, OpenAPI, /login) и входит под всеми 12 демо-пользователями: код роли, права, лимиты, 403 на запрещённом. Отчёт — `.run\check-report.txt` |
| `stop.bat` | закрывает окна API/фронта и останавливает контейнеры; `stop.bat -Purge` — ещё и удаляет базу |

Демо-логины (пароль `demo12345`): `owner@demo.kz`, `senior1@demo.kz`, `senior2@demo.kz`, `admin1@demo.kz`…`admin3@demo.kz`, `storekeeper@demo.kz`, `cashier@demo.kz`, `doctor1@demo.kz`…`doctor6@demo.kz`.

## Файлы
| Файл | Назначение |
|---|---|
| `SPEC.md` | мастер-спецификация (источник истины) |
| `CLAUDE.md` | правила для агента, читается автоматически в каждой сессии |
| `PROGRESS.md` | этапы, журнал, отложенное |
| `DECISIONS.md` | принятые решения по пробелам спецификации |
| `scripts/roles-matrix.json` | ожидаемые права и лимиты каждой роли (таблица 5.4 в исполняемом виде) |
