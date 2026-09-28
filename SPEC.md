# МАСТЕР-ПРОМПТ: Админ-панель для стоматологических клиник и сетей клиник (MVP, этап 1) — ASP.NET Core

> Вставь этот файл в корень репозитория как `SPEC.md` и дай Claude Code команду:
> «Прочитай SPEC.md целиком и реализуй проект по этапам из раздела 16. После каждого этапа запускай тесты и отмечай выполненное в PROGRESS.md».

---

## 0. Роль и задача для агента

Ты — senior full-stack инженер. Твоя задача — с нуля построить production-ready веб-приложение: **админ-панель для стоматологических клиник и сетей клиник**. Администраторы управляют записями пациентов, складом (приход, перемещения, списания, инвентаризация), кассой и формируют отчёты.

Работай автономно. Не задавай уточняющих вопросов: если в спецификации есть пробел, прими разумное решение, зафиксируй его в `DECISIONS.md` (одна строка на решение) и продолжай.

Архитектура должна с первого дня поддерживать будущие модули (заказ-наряды в зуботехнические лаборатории, B2B-маркетплейс поставщиков, приложение пациента), но **реализовывать их сейчас не нужно** (см. раздел 18).

---

## 1. Контекст продукта

- Рынок: Казахстан. Валюта по умолчанию — KZT. Интерфейс на русском, i18n-готовность (ru по умолчанию, kk и en — позже, ключи переводов с первого дня).
- Клиенты: отдельные клиники (1 филиал) и сети (2–50 филиалов).
- Пользователи этапа 1: владелец / админ сети, старший администратор, администратор (регистратура), кладовщик, кассир, врач (минимальный кабинет: своё расписание и завершение визита).
- Ключевая ценность: прозрачность. Владелец видит, куда уходят деньги и материалы; ни одно изменение нельзя сделать незаметно.

---

## 2. Технологический стек (зафиксирован, не менять)

**Backend**
- .NET 10 (LTS), ASP.NET Core, C# 14, `Nullable` enable, `TreatWarningsAsErrors` для всех проектов
- API: Minimal APIs, эндпоинты сгруппированы по модулям (`MapGroup`), OpenAPI через `Microsoft.AspNetCore.OpenApi` + Scalar UI в dev
- EF Core 10 + Npgsql (PostgreSQL 16), миграции EF Core (`dotnet ef`), `Npgsql.EntityFrameworkCore.PostgreSQL` с `UseSnakeCaseNamingConvention()` (EFCore.NamingConventions)
- Dapper — для отчётов и тяжёлых выборок (сырой SQL)
- Валидация: FluentValidation (через endpoint filter)
- Маппинг DTO вручную (статические методы / extension-методы). **Не использовать** AutoMapper и MediatR (коммерческие лицензии) — use-cases как обычные сервисы-хендлеры
- Фоновые задачи и cron: Hangfire + Hangfire.PostgreSql (dashboard `/jobs` только для владельца платформы в dev)
- Кэш и rate limiting: Redis 7 (`StackExchange.Redis`), встроенный `Microsoft.AspNetCore.RateLimiting`
- Аутентификация: JWT Bearer (access 15 мин) + refresh 30 дней в httpOnly cookie; пароли — `PasswordHasher<TUser>` из ASP.NET Core Identity (без полной Identity-схемы)
- Экспорт: ClosedXML (Excel), QuestPDF (PDF; Community License — бесплатна при выручке до $1M в год, зафиксировать в DECISIONS.md)
- Логи: Serilog (консоль + Seq в dev), OpenTelemetry-трейсинг
- Тесты: xUnit v3, Testcontainers (PostgreSQL, Redis), `WebApplicationFactory`, Shouldly (не FluentAssertions — коммерческая лицензия с v8), Bogus для фабрик и seed, NetArchTest для архитектурных правил
- Качество кода: `dotnet format`, .NET analyzers (`AnalysisLevel=latest-recommended`), `Directory.Build.props` и `Directory.Packages.props` (Central Package Management)

**Frontend**
- Next.js 15 (App Router) + TypeScript strict
- Tailwind CSS + shadcn/ui
- TanStack Query (серверное состояние), React Hook Form + Zod
- TanStack Table (таблицы), FullCalendar (resource timeline / timeGrid) для расписания
- Recharts для графиков
- Типы API генерируются из OpenAPI (`openapi-typescript`) — никаких рукописных дублей DTO

**Инфраструктура**
- Docker Compose: `api`, `worker` (тот же образ, запуск Hangfire-сервера), `web`, `postgres`, `redis`, `mailpit`, `minio`, `seq` (dev)
- Файлы: S3-совместимое хранилище (MinIO в dev)
- Makefile (обёртки над `dotnet` и `docker compose`): `make up`, `make migrate`, `make seed`, `make test`, `make lint`, `make gen-types`, `make rebuild-balances`
- Консольные команды приложения: `dotnet run --project src/Dental.Api -- migrate|seed|rebuild-balances`

---

## 3. Структура репозитория

```
/
├── SPEC.md  PROGRESS.md  DECISIONS.md  README.md  Makefile  docker-compose.yml  .env.example
├── backend/
│   ├── Dental.sln
│   ├── Directory.Build.props  Directory.Packages.props  .editorconfig
│   ├── src/
│   │   ├── Dental.Api/                  # хост: Program.cs, DI, middleware, эндпоинты модулей, OpenAPI
│   │   │   └── Endpoints/               # PatientsEndpoints.cs, ScheduleEndpoints.cs, ... (MapGroup по модулю)
│   │   ├── Dental.Application/          # use-cases (сервисы), DTO (records), валидаторы, интерфейсы портов
│   │   │   ├── Common/                  # Result<T>, ошибки (ErrorCodes), пагинация, ICurrentUser, ITenantContext
│   │   │   ├── Permissions/             # коды прав (константы), пресеты ролей, лимиты
│   │   │   └── <Module>/                # Patients/, Schedule/, Visits/, Inventory/, ...
│   │   ├── Dental.Domain/               # сущности, value objects (Money, Quantity), доменная логика
│   │   │                                #   без зависимостей от EF/ASP.NET (FEFO, скидки, остатки, слоты, зарплата)
│   │   ├── Dental.Infrastructure/       # EF Core DbContext, конфигурации сущностей, миграции, интерсепторы
│   │   │   ├── Persistence/             # AppDbContext, Configurations/, Migrations/, Interceptors/
│   │   │   ├── Reports/                 # SQL-отчёты на Dapper + экспорт ClosedXML/QuestPDF
│   │   │   ├── Jobs/                    # Hangfire-задачи
│   │   │   ├── Messaging/               # IMessageProvider: Console, WhatsApp (заглушка), Sms (заглушка), Email (SMTP)
│   │   │   └── Storage/                 # S3 (AWSSDK.S3, совместимо с MinIO)
│   │   └── Dental.Seed/                 # генерация демо-данных (Bogus)
│   └── tests/
│       ├── Dental.Domain.Tests/         # unit
│       ├── Dental.Api.Tests/            # интеграционные через WebApplicationFactory + Testcontainers
│       ├── Dental.Reports.Tests/        # эталонные суммы отчётов
│       └── Dental.Architecture.Tests/   # NetArchTest: Domain не зависит от EF/ASP.NET, модули не лезут друг в друга
└── frontend/
    ├── app/(auth)/login
    ├── app/(dashboard)/[orgSlug]/...   # все экраны раздела 10
    ├── components/ (ui/, calendar/, tables/, forms/, charts/)
    ├── lib/ (api-client.ts, api-types.ts [generated], permissions.ts, format.ts)
    └── messages/ru.json
```

Зависимости проектов: `Api → Application, Infrastructure`; `Infrastructure → Application, Domain`; `Application → Domain`; `Domain` — ни от чего. Проверяется архитектурными тестами.

Модули бэкенда (каждый — своя папка в Application, файл эндпоинтов в Api, конфигурации EF в Infrastructure, тесты):
`auth`, `orgs` (организации, филиалы, сотрудники, роли), `patients`, `schedule` (графики, записи), `catalog` (услуги, прайсы, техкарты), `visits`, `cashdesk` (смены, платежи, балансы), `inventory` (склады, номенклатура, партии, движения, заявки, инвентаризации), `suppliers` (справочник поставщиков, заказы поставщику — без маркетплейса), `payroll` (упрощённо, см. 8.7), `reports`, `audit`, `notifications`.

---

## 4. Архитектурные принципы (обязательны)

1. **Мультиарендность.** Таблица `organizations` с полем `type` (`clinic_network` сейчас; `lab`, `supplier`, `platform` — зарезервированы). Каждая бизнес-таблица содержит `organization_id NOT NULL`. Таблицы уровня филиала также содержат `branch_id`.
   - Изоляция в два слоя: (а) EF Core **global query filters** по `OrganizationId` из scoped `ITenantContext` (заполняется из JWT в middleware) для всех сущностей, реализующих `ITenantEntity`; `SaveChangesInterceptor` проставляет `OrganizationId` при вставке и запрещает менять его; (б) PostgreSQL Row Level Security с `current_setting('app.org_id')`: `DbConnectionInterceptor` выполняет `SELECT set_config('app.org_id', @orgId, false)` при открытии каждого соединения (и сбрасывает при возврате в пул). Приложение подключается к БД под ролью без `BYPASSRLS`; миграции — под отдельной ролью-владельцем. Напиши тест, доказывающий, что пользователь организации A не может прочитать или изменить данные организации B ни одним эндпоинтом.
2. **Деньги** хранятся в `BIGINT` в тиынах (минимальных единицах), поле `currency CHAR(3)`. В коде — value object `Money` (`long Minor`, `string Currency`) с EF value converter. Никаких `double`/`float`; проценты — `decimal` / `NUMERIC(5,2)`.
3. **Количества на складе** — `decimal` / `NUMERIC(14,3)`, у номенклатуры есть базовая единица и коэффициенты упаковок (упаковка → штуки → граммы).
4. **Время** хранится в UTC (`timestamptz` ↔ `DateTimeOffset`/`DateTime` UTC; даты — `DateOnly`, время смены — `TimeOnly`). Для часовых поясов — `TimeZoneInfo` (IANA-идентификаторы) через абстракцию `IClock` (для тестов — `FakeTimeProvider` / `TimeProvider`). У организации есть поле `timezone` (по умолчанию `Asia/Almaty`). Все отчёты «по дням» считаются в часовом поясе организации.
5. **Ничего не удаляется физически** из бизнес-данных: `deleted_at` для справочников; для визитов, платежей и движений склада — только отмена или сторно с новой записью.
6. **Складской учёт — через журнал движений.** Остаток = сумма движений. Таблица `stock_balances` — материализованный кэш, обновляется в той же транзакции, что и движение. Есть команда `make rebuild-balances` и тест на совпадение.
7. **Аудит.** Любая мутация через сервисный слой пишет запись в `audit_log` (кто, когда, сущность, действие, diff было/стало, причина, IP). Реализуй через `SaveChangesInterceptor`, который собирает diff из `ChangeTracker` для сущностей с атрибутом `[Audited]`, плюс явный `IAuditService.Log(...)` для бизнес-событий (причина, флаг подозрительности). Не пиши аудит вручную в каждом сервисе.
8. **Оптимистическая блокировка:** поле `version INT` (`IsConcurrencyToken()`, инкремент в интерсепторе) у записей, визитов и документов склада; клиент присылает `version`; `DbUpdateConcurrencyException` → HTTP 409 `CONCURRENCY_CONFLICT`.
9. **Идемпотентность** для платежей и проведения складских документов: заголовок `Idempotency-Key`, endpoint filter + таблица `idempotency_keys` (ключ, org, хэш запроса, сохранённый ответ, TTL 24 ч).
10. **Единый формат ошибок:** `{ "error": { "code": "STOCK_INSUFFICIENT", "message": "...", "details": {...} } }`. Коды ошибок — константы в `Dental.Application/Common/ErrorCodes.cs`. Сервисы возвращают `Result<T>`, доменные нарушения — `DomainException(code)`; глобальный `IExceptionHandler` превращает их в этот формат (валидация FluentValidation → 400 `VALIDATION_FAILED` с полями в `details`). На фронте коды переводятся через i18n.
11. **Пагинация** курсорная для журналов, offset-пагинация для справочников. Фильтры и сортировки — через query-параметры.
12. **Транзакции:** один use-case = одна транзакция (`IUnitOfWork` / `DbContext.Database.BeginTransactionAsync`). Для проведения складских документов и нумерации — уровень изоляции `ReadCommitted` + явные блокировки строк (`SELECT … FOR UPDATE` через `FromSql`).
13. **Async везде**, `CancellationToken` пробрасывается от эндпоинта до БД.

---

## 5. Роли и права

### 5.1. Модель
- `users` — глобальная учётная запись (email / телефон + пароль).
- `memberships` — связь user ↔ organization с ролью и списком доступных филиалов (`branch_ids` или флаг `all_branches`).
- `roles` — пресеты и кастомные роли организации: набор кодов прав + **лимиты**.
- Права проверяются на бэкенде через ASP.NET Core authorization: динамические политики `IAuthorizationPolicyProvider` по коду права, на эндпоинтах — `.RequirePermission(Permissions.Visits.EditClosed)` (extension над `RequireAuthorization`). Права и лимиты текущего членства грузятся один раз на запрос в `ICurrentUser` (кэш в Redis на 5 мин, сброс при изменении роли). Проверка доступа к филиалу — `ICurrentUser.EnsureBranchAccess(branchId)`. На фронте права используются только для скрытия кнопок; источник истины — бэкенд.

### 5.2. Коды прав
```
org.settings.manage          branches.manage             staff.manage         roles.manage
patients.view                patients.edit               patients.merge       patients.view_medical
schedule.view_all            schedule.view_own           schedule.manage      doctor_schedules.manage
visits.complete              visits.edit_open            visits.edit_closed   visits.cancel
discounts.apply              prices.manage               techcards.manage
cash.shift.open_close        cash.payment.create         cash.payment.refund  cash.expense.create
inventory.view               inventory.receive           inventory.transfer.create   inventory.transfer.receive
inventory.writeoff           inventory.count             inventory.count.approve     inventory.items.manage
purchase.request.create      purchase.order.create       purchase.order.approve
payroll.view_own             payroll.view_all            payroll.manage
reports.branch               reports.network             reports.finance       reports.payroll
audit.view
```

### 5.3. Лимиты роли (JSON в `roles.limits`)
- `max_discount_pct` — максимальная скидка без подтверждения
- `max_writeoff_amount` — максимальная сумма ручного списания (в тиынах) без подтверждения
- `max_refund_amount`
- `can_edit_closed_shift_visits` (bool)

При превышении лимита действие не блокируется, а создаётся **запрос на подтверждение** (`approval_requests`) со статусом `pending`; сущность получает статус «ожидает подтверждения». Подтверждать может пользователь с правом и лимитом, достаточным для этого действия.

### 5.4. Пресеты ролей (создаются в seed для каждой новой организации)

| Право / лимит | Админ | Старший админ | Кладовщик | Кассир | Врач | Владелец |
|---|---|---|---|---|---|---|
| Записи: создание / перенос | ✅ | ✅ | — | — | только свои (просмотр) | ✅ |
| Изменение закрытого визита | — | ✅ | — | — | — | ✅ |
| max_discount_pct | 5 | 15 | — | 5 | 0 | 100 |
| Приход на склад | ✅ | ✅ | ✅ | — | — | ✅ |
| Ручное списание (лимит) | 20 000 ₸ | 100 000 ₸ | 50 000 ₸ | — | — | ∞ |
| Перемещение между филиалами | — | — | ✅ (создание / приёмка) | — | — | ✅ |
| Утверждение инвентаризации | — | ✅ | — | — | — | ✅ |
| Прайс, техкарты | — | — | — | — | — | ✅ |
| Отчёты филиала | ✅ (без финансов) | ✅ | склад | касса | свои | ✅ |
| Отчёты сети, P&L, зарплаты | — | — | — | — | — | ✅ |
| Журнал аудита | — | ✅ (свой филиал) | — | — | — | ✅ |

---

## 6. Схема базы данных

Именование в БД — snake_case (через EFCore.NamingConventions), в C# — PascalCase. Конфигурации — отдельные классы `IEntityTypeConfiguration<T>`. JSONB-поля мапятся на типизированные классы через `OwnsOne(...).ToJson()` или `HasColumnType("jsonb")`; массивы (`uuid[]`, `text[]`) — нативно через Npgsql. Enum-ы хранятся строками (`HasConversion<string>()`). Исключающие ограничения, RLS-политики, `pg_trgm`-индексы и партиционирование `audit_log` добавляются в миграциях через `migrationBuilder.Sql(...)`.

Общие поля почти всех таблиц: `id UUID PK` (генерируется в приложении через `Guid.CreateVersion7()`), `organization_id`, `created_at`, `updated_at`, `created_by`, `updated_by`. Ниже они опущены.

### 6.1. Организация и персонал
- **organizations**: name, slug (unique), type, timezone, currency, settings JSONB (часы работы по умолчанию, длительность слота, правила напоминаний), logo_url
- **branches**: name, address, phone, working_hours JSONB, is_active
- **rooms**: branch_id, name
- **chairs**: branch_id, room_id, name, is_active
- **users**: email (unique, nullable), phone (unique, nullable), password_hash, full_name, is_active, last_login_at
- **memberships**: user_id, role_id, all_branches BOOL, branch_ids UUID[], position (`doctor`, `assistant`, `admin`, …), specialty (для врачей), color (для календаря), is_active, fired_at
- **roles**: name, is_preset, permissions TEXT[], limits JSONB
- **approval_requests**: type (`discount`, `writeoff`, `refund`, `closed_visit_edit`, `purchase_order`), entity_type, entity_id, payload JSONB, requested_by, status (`pending`/`approved`/`rejected`), decided_by, decided_at, comment

### 6.2. Пациенты
- **patients**: last_name, first_name, middle_name, birth_date, gender, iin (nullable, unique в пределах org), phone, phone_extra, email, address, source (справочник источников), notes, tags TEXT[], is_vip, merged_into_id (nullable), deleted_at
- **patient_consents**: patient_id, type, signed_at, file_url
- **patient_balances**: patient_id, balance BIGINT (кэш: + аванс / − долг)
- **lead_sources**: name
- Индексы: trigram-поиск по ФИО и телефону (`pg_trgm`), индекс по iin.

### 6.3. Каталог услуг
- **service_categories**: name, parent_id, sort
- **services**: category_id, code, name, duration_min, is_active
- **price_lists**: name, branch_id (NULL = для всей сети), valid_from, is_active
- **price_list_items**: price_list_id, service_id, price
- **price_history** — через audit_log, отдельная таблица не нужна
- **tech_cards**: service_id, version, is_active
- **tech_card_items**: tech_card_id, item_id (номенклатура), quantity (в базовых единицах)

### 6.4. Расписание и визиты
- **doctor_schedules**: membership_id, branch_id, weekday, start_time, end_time, chair_id (опционально), valid_from, valid_to
- **schedule_exceptions**: membership_id, date_from, date_to, type (`vacation`, `sick`, `day_off`, `extra_shift`), start_time, end_time
- **time_blocks**: branch_id, doctor_id | chair_id, starts_at, ends_at, reason
- **appointments**: branch_id, patient_id, doctor_id, chair_id, starts_at, ends_at, status (`scheduled`, `confirmed`, `arrived`, `in_chair`, `completed`, `cancelled`, `no_show`), source (`admin`, `phone`, `online`, `walk_in`), comment, cancel_reason_id, cancel_comment, rescheduled_from_id, confirmed_at, confirmed_via, version
- **appointment_services**: appointment_id, service_id, planned_price, qty
- **cancel_reasons**: name, type (`cancel`, `reschedule`)
- **waitlist**: branch_id, patient_id, doctor_id (nullable), service_id (nullable), preferred_from, preferred_to, preferred_times JSONB, status, comment
- **visits** (создаётся при статусе `arrived`, закрывается при `completed`): appointment_id, branch_id, patient_id, doctor_id, assistant_id, status (`open`, `closed`, `cancelled`), closed_at, cash_shift_id (смена, в которую закрыт), subtotal, discount_total, total, paid_total, version
- **visit_items**: visit_id, service_id, doctor_id, qty, unit_price, discount_pct, discount_amount, total, tooth_numbers INT[] (ISO 3950, задел на одонтограмму)
- **visit_material_usage**: visit_id, visit_item_id, item_id, quantity, batch_id, cost — фактическое списание (по техкарте, врач может скорректировать)
- Ограничение: исключающее ограничение (`EXCLUDE USING gist`) на пересечение времени записей одного врача и одного кресла для активных статусов.

### 6.5. Касса
- **cash_registers**: branch_id, name
- **cash_shifts**: cash_register_id, opened_by, opened_at, opening_balance, closed_by, closed_at, closing_balance_expected, closing_balance_actual, difference, status
- **payments**: branch_id, patient_id, visit_id (nullable — аванс), cash_shift_id, method (`cash`, `card`, `kaspi_qr`, `transfer`, `insurance`, `balance`), amount, type (`payment`, `refund`, `advance`), refunded_payment_id, idempotency_key (unique), comment
- **expenses**: branch_id, category_id, amount, paid_at, cash_shift_id (nullable), counterparty, comment, document_url
- **expense_categories**: name, type (`rent`, `utilities`, `salary`, `supplies`, `lab`, `marketing`, `other`)
- **cash_operations**: cash_shift_id, type (`collection`, `deposit`), amount, comment

### 6.6. Склад
- **warehouses**: branch_id (NULL = центральный склад сети), type (`central`, `branch`, `cabinet`), parent_warehouse_id, name, responsible_id
- **item_categories**: name, parent_id
- **items** (номенклатура): category_id, sku, name, manufacturer, base_unit (`pcs`, `g`, `ml`, `pack`), track_batches BOOL, track_serials BOOL (импланты), track_expiry BOOL, is_active
- **item_units**: item_id, unit_name, factor_to_base (например, «упаковка» = 50 шт)
- **item_stock_levels**: item_id, warehouse_id, min_qty, optimal_qty
- **batches**: item_id, batch_number, serial_number (nullable), expires_at, unit_cost, supplier_id
- **stock_documents**: type (`receipt`, `transfer`, `writeoff`, `inventory`, `return_to_supplier`, `visit_consumption`), number (сквозная нумерация по org и типу), status (`draft`, `pending_approval`, `posted`, `in_transit`, `received`, `cancelled`), warehouse_from_id, warehouse_to_id, supplier_id, purchase_order_id, invoice_number, invoice_date, reason_id, comment, posted_at, posted_by, received_at, received_by, total_cost, version
- **stock_document_lines**: document_id, item_id, batch_id, qty (в базовых ед.), qty_input + unit_id (как ввёл пользователь), unit_cost, total_cost, expected_qty (для инвентаризации и приёмки), actual_qty
- **stock_movements** (неизменяемый журнал): document_id, line_id, warehouse_id, item_id, batch_id, qty (+ приход / − расход), unit_cost, moved_at, patient_id (для серийных имплантов)
- **stock_balances** (кэш): warehouse_id, item_id, batch_id, qty, updated_at — UNIQUE(warehouse_id, item_id, batch_id)
- **writeoff_reasons**: name, type (`expired`, `damaged`, `defect`, `lost`, `other`)
- **inventory_counts** — это `stock_documents` с type=`inventory`; флаг `warehouse_locked` на складе, пока пересчёт в статусе `draft`

### 6.7. Поставщики и закупки (без маркетплейса)
- **suppliers**: name, bin, contact_person, phone, email, whatsapp, payment_terms_days, notes
- **supplier_items**: supplier_id, item_id, supplier_sku, last_price
- **purchase_requests** (заявка на пополнение): branch_id, warehouse_id, status (`draft`, `submitted`, `processed`, `rejected`), source (`auto`, `manual`), comment
- **purchase_request_lines**: request_id, item_id, qty, current_qty, min_qty, optimal_qty
- **purchase_orders**: supplier_id, warehouse_id (куда придёт), status (`draft`, `pending_approval`, `sent`, `partially_received`, `received`, `cancelled`), expected_at, total, sent_via
- **purchase_order_lines**: order_id, item_id, qty, unit_price, received_qty
- **supplier_invoices**: supplier_id, purchase_order_id, number, date, amount, due_date, paid_amount, status

### 6.8. Зарплата (упрощённо для MVP)
- **payroll_schemes**: membership_id, type (`percent_revenue`, `percent_revenue_minus_materials`, `fixed_plus_percent`, `per_shift`), percent, fixed_amount, shift_rate, valid_from
- **payroll_periods**: branch_id, period_start, period_end, status (`draft`, `approved`, `paid`)
- **payroll_entries**: period_id, membership_id, base_revenue, materials_cost, lab_cost (0 в MVP), accrued, bonus, penalty, total, details JSONB

### 6.9. Аудит и уведомления
- **audit_log**: user_id, branch_id, entity_type, entity_id, action, diff JSONB, reason, ip, user_agent, is_suspicious BOOL, created_at — партиционирование по месяцам
- **notifications**: user_id, type, title, body, entity_type, entity_id, read_at
- **message_templates**: type (`reminder_24h`, `reminder_2h`, `recall_6m`, `birthday`), channel (`whatsapp`, `sms`), text с переменными `{patient_name}`, `{date}`, `{time}`, `{doctor}`, `{branch_address}`
- **outgoing_messages**: patient_id, appointment_id, channel, text, status, provider_message_id, sent_at, error
- **report_subscriptions**: user_id, report_code, params JSONB, schedule_cron, channel (`email`, `telegram`), is_active

---

## 7. Нумерация документов

Каждый тип документа нумеруется по организации в формате `ПРХ-000123`, `ПРМ-…` (перемещение), `СПС-…` (списание), `ИНВ-…`, `ЗКП-…` (заказ поставщику). Счётчик хранится в таблице `document_counters`: `INSERT … ON CONFLICT DO UPDATE SET value = value + 1 RETURNING value` в той же транзакции, что и документ.

---

## 8. Бизнес-правила

### 8.1. Записи
- Нельзя создать запись вне рабочего графика врача: ошибка `DOCTOR_NOT_WORKING`. Старший админ может создать запись с флагом `force`, это пишется в аудит.
- Пересечения по врачу и креслу запрещены на уровне БД (exclusion constraint, расширение `btree_gist`) → `PostgresException` с `SqlState = 23P01` перехватывается и превращается в 409 `SLOT_CONFLICT` с данными о конфликтующей записи.
- Перенос = изменение времени у той же записи, в аудит пишется старое и новое время. Если перенос инициирован пациентом, заполняется причина из справочника (тип `reschedule`).
- Отмена требует `cancel_reason_id`. Физическое удаление записей запрещено.
- `no_show` автоматически проставляется воркером через 60 минут после начала, если статус так и остался `scheduled`/`confirmed` (настраивается).
- При отмене записи воркер ищет подходящих пациентов в `waitlist` и создаёт уведомление администратору.

### 8.2. Визиты и корректировки
- Визит открывается, когда администратор отмечает «пациент пришёл». Услуги копируются из записи, цены берутся из действующего прайса филиала.
- Скидка на позицию или визит: если `discount_pct > role.max_discount_pct`, создаётся `approval_request`, визит нельзя закрыть до подтверждения.
- Закрытие визита: фиксирует цены; создаёт `stock_document` типа `visit_consumption` со списанием по `visit_material_usage` (FEFO — сначала партии с ближайшим сроком годности); выставляет долг пациенту на `total - paid_total`.
- Если материала на складе кабинета/филиала не хватает, визит всё равно закрывается (нельзя блокировать лечение), но остаток уходит в минус, создаётся уведомление кладовщику и флаг `negative_stock` в отчёте. Настройка `allow_negative_stock` (по умолчанию true).
- **Изменение закрытого визита:**
  - если кассовая смена, в которой визит закрыт, ещё открыта — требуется право `visits.edit_closed` и обязательный комментарий;
  - если смена закрыта — дополнительно `limits.can_edit_closed_shift_visits`, иначе создаётся `approval_request`;
  - все корректировки создают сторнирующие и новые движения склада и денег, исходные записи не переписываются;
  - в аудит пишется запись с `is_suspicious = true`.
- Отмена закрытого визита — только владельцем, с возвратом материалов на склад и пересчётом баланса пациента.

### 8.3. Касса
- Платёж без открытой смены на кассе филиала невозможен: `SHIFT_NOT_OPEN`.
- Оплата с баланса пациента (`method=balance`) возможна только в пределах положительного баланса.
- Возврат > `max_refund_amount` → `approval_request`.
- Удалить платёж нельзя, только возврат (refund) со ссылкой на исходный.
- Закрытие смены: ожидаемый остаток наличных считается автоматически, кассир вводит фактический, разница фиксируется; при разнице больше 0 — уведомление владельцу.

### 8.4. Склад
- Проведение документа (`posted`) атомарно создаёт `stock_movements` и обновляет `stock_balances` в одной транзакции с блокировкой строк баланса.
- **Приход:** обязательны поставщик, строки, цена за единицу; для `track_batches` — номер партии; для `track_expiry` — срок годности; для `track_serials` — серийный номер на каждую единицу (qty = 1 на строку). Если приход создан из заказа поставщику, обновляется `received_qty`, статус заказа меняется на `partially_received`/`received`.
- **Перемещение:** проведение отправителем → товар списывается со склада-отправителя и становится `in_transit`; приёмка получателем → приход на склад-получатель. При расхождении получатель указывает `actual_qty`; разница уходит в документ списания с причиной `lost`, требующий подтверждения. Отчёт «Товары в пути».
- **Ручное списание:** обязательна причина; сумма по себестоимости > `max_writeoff_amount` → `approval_request`, документ в статусе `pending_approval`.
- **Инвентаризация:** при создании фиксируются `expected_qty` на момент старта, склад блокируется для списаний и перемещений (визиты продолжают списывать — их движения учитываются при утверждении как поправка). Утверждение создаёт движения на разницу: излишки — приход, недостачи — списание. Результат доступен в отчёте «Расхождения инвентаризации».
- **Себестоимость:** по партиям (фактическая цена партии); для номенклатуры без партий — средневзвешенная.
- **Возврат поставщику:** расход со склада + отметка в `supplier_invoices`.

### 8.5. Пополнение
- Воркер каждую ночь (и по кнопке) формирует `purchase_requests` с `source=auto` для каждого склада, где `qty < min_qty`: предлагаемое количество = `optimal_qty - qty - в_пути - уже_заказано`.
- Филиал сети отправляет заявку (`submitted`) → админ сети видит сводную потребность по всем филиалам и может: (а) создать перемещение с центрального склада, (б) создать заказ поставщику, (в) отклонить.
- Заказ поставщику сверх настраиваемого порога суммы → `approval_request`.
- Заказ можно выгрузить в PDF/Excel или скопировать текстом для WhatsApp (`sent_via` фиксируется).
- Для каждой позиции показывается последняя цена у каждого поставщика из `supplier_items`.

### 8.6. Пациенты
- Дубли: при создании проверяется совпадение телефона или ИИН → предупреждение со ссылкой на существующего пациента.
- Слияние (`patients.merge`): все записи, визиты, платежи и баланс переносятся на основную карточку, дубль получает `merged_into_id`. Операция пишется в аудит; отмена слияния в MVP не требуется.

### 8.7. Зарплата (MVP)
- Расчёт за период по закрытым визитам врача: база = сумма `visit_items.total` (оплаченных полностью или всех — настройка организации).
- `percent_revenue_minus_materials`: база минус себестоимость `visit_material_usage`.
- Ведомость создаётся в статусе `draft`, владелец может добавить бонус или штраф с комментарием и утвердить. После утверждения изменения визитов за этот период → `approval_request` и предупреждение.
- Врач видит свою детализацию (право `payroll.view_own`).

### 8.8. Подозрительные события (`audit_log.is_suspicious = true`)
Изменение закрытого визита; скидка выше лимита роли; возврат платежа; ручное списание дороже 50 000 ₸ (настраивается); расхождение инвентаризации дороже 20 000 ₸; разница кассы при закрытии смены; вход сотрудника с нового устройства вне рабочего времени филиала. По каждому такому событию владелец получает уведомление; есть отдельный экран-фильтр.

---

## 9. API (REST, префикс `/api/v1`)

Minimal APIs: каждый модуль — статический класс `XxxEndpoints` с методом `MapXxxEndpoints(this IEndpointRouteBuilder app)`. Запросы и ответы — `record`-DTO, JSON в camelCase, даты ISO 8601. Для каждой операции задать `WithName`, `WithTags`, `Produces<T>`/`ProducesProblem`, чтобы OpenAPI-документ был полным и фронтовые типы генерировались без `any`. Документ публикуется по `/openapi/v1.json`, `make gen-types` берёт его оттуда.

Все эндпоинты работают в контексте организации (из JWT; активная организация выбирается через `POST /auth/switch-org`). Филиал передаётся параметром `branch_id` или берётся из прав пользователя.

```
AUTH
POST   /auth/login                  POST /auth/refresh        POST /auth/logout
POST   /auth/switch-org             GET  /auth/me             (права, лимиты, филиалы)
POST   /auth/password/forgot|reset

ORG
GET|PATCH           /org
GET|POST            /branches           GET|PATCH /branches/{id}
GET|POST            /branches/{id}/rooms|chairs
GET|POST            /staff              GET|PATCH /staff/{id}    POST /staff/{id}/fire
GET|POST            /roles              GET|PATCH /roles/{id}
GET                 /approvals?status=  POST /approvals/{id}/approve|reject

PATIENTS
GET    /patients?q=&tag=&source=&cursor=     POST /patients
GET|PATCH /patients/{id}
GET    /patients/{id}/visits|payments|appointments|balance
POST   /patients/{id}/merge  {duplicate_id}
GET    /patients/duplicates  (кандидаты на слияние)
GET|POST /lead-sources

SCHEDULE
GET    /calendar?branch_id=&from=&to=&doctor_ids=&view=doctors|chairs
       → записи, графики, блокировки, исключения одним ответом
GET    /slots/available?branch_id=&doctor_id=&service_ids=&date=
GET|POST|PATCH|DELETE /doctor-schedules     GET|POST /schedule-exceptions
GET|POST|DELETE       /time-blocks
POST   /appointments                    GET|PATCH /appointments/{id}
POST   /appointments/{id}/move   {starts_at, doctor_id?, chair_id?, reason_id?}
POST   /appointments/{id}/status {status, reason_id?, comment?}
POST   /appointments/{id}/remind  (ручная отправка напоминания)
GET|POST|PATCH /waitlist
GET|POST /cancel-reasons

VISITS
POST   /appointments/{id}/arrive        → создаёт visit
GET|PATCH /visits/{id}
POST|PATCH|DELETE /visits/{id}/items[/{item_id}]
GET|PUT   /visits/{id}/materials        (предзаполнено из техкарт)
POST   /visits/{id}/close
POST   /visits/{id}/corrections  {changes, reason}   (для закрытых)
POST   /visits/{id}/cancel       {reason}

CATALOG
GET|POST|PATCH /service-categories   /services   /price-lists   /price-lists/{id}/items
POST   /price-lists/{id}/bulk-update  {category_id?, percent}   (массовое повышение цен)
GET|POST /services/{id}/tech-cards   (новая версия техкарты)

CASHDESK
GET|POST /cash-registers
POST   /cash-shifts/open        POST /cash-shifts/{id}/close   GET /cash-shifts?branch_id=
POST   /payments  (Idempotency-Key)   POST /payments/{id}/refund
GET|POST /expenses   GET|POST /expense-categories
POST   /cash-shifts/{id}/operations   (инкассация / внесение)

INVENTORY
GET|POST|PATCH /warehouses     /items     /item-categories
GET|PUT        /items/{id}/stock-levels
GET    /stock/balances?warehouse_id=&category_id=&below_min=&expiring_days=
GET    /stock/movements?item_id=&warehouse_id=&from=&to=
GET    /stock/items/{id}/card        (карточка товара: остатки по складам и партиям + история)
GET|POST        /stock-documents?type=&status=
GET|PATCH       /stock-documents/{id}
POST   /stock-documents/{id}/post | /receive | /cancel | /approve
POST   /stock-documents/{id}/lines/import   (Excel)
GET    /stock-documents/{id}/print           (PDF)
GET|POST|PATCH /writeoff-reasons

PURCHASING
GET|POST|PATCH /suppliers   GET|PUT /suppliers/{id}/items
POST   /purchase-requests/generate?warehouse_id=   (авто)
GET|POST|PATCH /purchase-requests
POST   /purchase-requests/{id}/submit | /reject
GET    /purchase-requests/network-demand            (сводно по сети)
POST   /purchase-requests/process  {request_ids, action: transfer|order, supplier_id?}
GET|POST|PATCH /purchase-orders   POST /purchase-orders/{id}/send|cancel
GET    /purchase-orders/{id}/export?format=pdf|xlsx|text
POST   /purchase-orders/{id}/receive   → создаёт приходный документ-черновик
GET|POST|PATCH /supplier-invoices

PAYROLL
GET|POST /payroll-schemes      GET|POST /payroll-periods
POST   /payroll-periods/{id}/calculate | /approve
PATCH  /payroll-entries/{id}   (бонус / штраф)
GET    /payroll/me

REPORTS
GET    /reports                                    (каталог доступных отчётов по правам)
GET    /reports/{code}?from=&to=&branch_ids=&doctor_ids=&group_by=&format=json|xlsx|pdf
GET|POST|DELETE /report-subscriptions
GET    /dashboard?branch_id=&date=

AUDIT & NOTIFICATIONS
GET    /audit?entity_type=&entity_id=&user_id=&suspicious=&from=&to=
GET    /notifications   POST /notifications/read-all   POST /notifications/{id}/read
GET|PATCH /message-templates
```

---

## 10. Экраны (frontend)

Общий layout: боковое меню по разделам (пункты скрываются по правам); в шапке — переключатель организации, **переключатель филиала** («Все филиалы» для админа сети), глобальный поиск пациента (Ctrl+K), колокольчик уведомлений с бейджем подтверждений. Адаптивность: планшет — полноценно, телефон — дашборд, календарь (день), карточка пациента.

1. **Вход / восстановление пароля / выбор организации.**
2. **Дашборд** (`/[org]/dashboard`): KPI-карточки (записи сегодня по статусам, загрузка %, выручка день/неделя, долги, неподтверждённые записи), список «Требует внимания» (неподтверждённые записи с кнопкой WhatsApp, подтверждения, товары ниже минимума, истекающие сроки, открытые смены со вчера), для сети — таблица филиалов.
3. **Расписание** (`/schedule`): FullCalendar в режимах «врачи» (колонки), «кресла», «неделя врача». Drag & drop для переноса и изменения длительности (с диалогом причины при необходимости). Цвета статусов и врачей. Быстрое создание по клику на слот: поиск пациента или создание нового прямо в модалке, выбор услуг (длительность подставляется). Боковая панель записи: статусы в одну кнопку, «Пациент пришёл», «Написать», «Перенести», «Отменить». Панель листа ожидания.
4. **Графики врачей** (`/schedule/doctors`): недельный шаблон, исключения (отпуск и т. д.), блокировки.
5. **Пациенты** (`/patients`): таблица с поиском и фильтрами (теги, источник, должники, «не были N месяцев»); карточка с вкладками «Обзор», «Записи», «Визиты», «Платежи и баланс», «Документы», «История изменений»; экран поиска и слияния дублей.
6. **Визит** (`/visits/{id}`): позиции услуг с ценой и скидкой, материалы (из техкарт, редактируемые), итоги, кнопки «Оплатить», «Закрыть визит». Для закрытого визита — «Корректировка» с обязательной причиной и отображением «было / стало».
7. **Касса** (`/cash`): текущая смена, приём оплаты (выбор визита или аванса, способ, сумма, сплит-оплата), возврат, расходы, инкассация, закрытие смены со сверкой; история смен.
8. **Склад — остатки** (`/inventory`): фильтры (склад, категория, ниже минимума, истекающие), колонки: товар, склад, количество, единица, себестоимость, сумма, мин/опт, ближайший срок годности. Цветовые индикаторы. Карточка товара: остатки по складам и партиям, история движений.
9. **Склад — документы** (`/inventory/documents`): список с фильтрами по типу и статусу; формы:
   - приход: поставщик, накладная, строки (поиск товара, ввод в любой единице, партия, срок, цена), импорт из Excel, «Создать из заказа поставщику»;
   - перемещение: откуда → куда, строки, отправка, приёмка на стороне получателя с вводом факта;
   - списание: причина, строки, итоговая сумма с предупреждением о лимите;
   - инвентаризация: старт (блокировка), ввод факта (режим для планшета, поиск и сканирование штрихкода — задел), список расхождений, утверждение.
10. **Номенклатура и нормы** (`/inventory/items`): справочник товаров, единицы, мин/опт по складам, массовое редактирование норм.
11. **Закупки** (`/purchasing`): заявки на пополнение (авто и ручные), сводная потребность сети (таблица «товар × филиал» с кнопками «переместить с центрального» / «заказать»), заказы поставщикам (создание, экспорт PDF/Excel/текст, приёмка), счета поставщиков и долги перед ними.
12. **Услуги и прайсы** (`/catalog`): дерево категорий, услуги, прайсы по филиалам, массовое изменение цен на %, техкарты с версиями.
13. **Персонал** (`/staff`): сотрудники, роли (редактор прав чекбоксами по группам + лимиты), доступ к филиалам, схемы зарплаты.
14. **Зарплата** (`/payroll`): периоды, расчёт, детализация по врачу до визита, бонусы/штрафы, утверждение; «Мои начисления» для врача.
15. **Отчёты** (`/reports`): каталог отчётов по группам; единый экран отчёта — фильтры сверху (период с пресетами «сегодня / неделя / месяц / прошлый месяц / произвольный», филиалы, врачи, группировка), график + таблица, клик по строке → детализация до конкретного визита или документа, экспорт Excel/PDF, «Подписаться» (ежедневно / еженедельно, email или Telegram), сохранённые фильтры.
16. **Подтверждения** (`/approvals`): очередь запросов с контекстом («было / стало», кто запросил, сумма), кнопки «Одобрить» / «Отклонить» с комментарием.
17. **Журнал действий** (`/audit`): таблица с фильтрами, вкладка «Подозрительные», просмотр diff.
18. **Настройки** (`/settings`): организация, филиалы, кабинеты и кресла, часы работы, кассы, склады, справочники (причины отмен и списаний, источники, категории расходов), шаблоны сообщений, лимиты подтверждений, настройки воркеров (no-show, время отправки отчётов).

UX-требования: скелетоны при загрузке, оптимистичные обновления в календаре с откатом при 409, горячие клавиши в календаре, подтверждение перед разрушительными действиями, все суммы форматируются как `12 500 ₸`, даты — `28.09.2026`, пустые состояния с призывом к действию.

---

## 11. Отчёты (реализовать все)

Каждый отчёт — класс в `Dental.Infrastructure/Reports/`, реализующий `IReport` (код, требуемое право, типизированные параметры, описание колонок, `ExecuteAsync`). SQL — в embedded `.sql`-ресурсах, выполнение через Dapper на том же соединении (RLS действует). Никаких N+1 и выборок сущностей через EF для отчётов. Регистрация — через DI-сканирование, каталог `/reports` строится из зарегистрированных `IReport`. Для каждого — тест на эталонных данных seed.

**Операционные (`reports.branch`)**
1. `appointments_summary` — записи за период по статусам, по дням, по врачам; % неявок и отмен.
2. `cancellations` — отмены и переносы с разбивкой по причинам.
3. `utilization` — загрузка врачей и кресел: занятое время / рабочее время по графику; тепловая карта «день недели × час».
4. `patients_flow` — новые / повторные пациенты, по источникам привлечения.
5. `lost_patients` — пациенты без визита N месяцев и без будущей записи (для обзвона), экспорт с телефонами.

**Финансовые (`reports.finance`)**
6. `revenue` — выручка (закрытые визиты) и поступления (платежи) по дням / врачам / услугам / категориям / филиалам; средний чек.
7. `payments_by_method` — поступления по способам оплаты, возвраты.
8. `patient_debts` — долги пациентов с возрастом долга (0–30, 31–90, 90+ дней).
9. `advances` — авансы (положительные балансы).
10. `cash_shifts` — смены, расхождения, инкассации.
11. `discounts` — скидки по сотрудникам и врачам, сумма, превышения лимита.
12. `service_margin` — маржинальность услуг: цена − себестоимость материалов (факт) → маржа и % по услуге.
13. `pnl` (`reports.network`) — выручка − материалы − расходы по категориям − зарплата (из утверждённых ведомостей) = прибыль; по филиалам и сети.

**Врачи (`reports.branch`; для врача — только свои)**
14. `doctor_performance` — выручка, пациенты, визиты, средний чек, загрузка, % неявок.

**Склад (`inventory.view`)**
15. `stock_on_date` — остатки на произвольную дату (из журнала движений) в количестве и деньгах.
16. `stock_movement` — оборотная ведомость: начальный остаток, приход, расход (визиты / списания / перемещения отдельно), конечный остаток.
17. `consumption` — расход материалов по врачам и услугам; расход на единицу услуги в сравнении с нормой техкарты (выявление перерасхода).
18. `writeoffs` — списания по причинам и сотрудникам.
19. `expiring` — истекающие и просроченные партии.
20. `inventory_discrepancies` — расхождения по инвентаризациям, в деньгах.
21. `abc_analysis` — ABC по сумме расхода за период.
22. `in_transit` — товары в пути между складами.
23. `purchases` — закупки по поставщикам, динамика цен на позицию.

**Сеть (`reports.network`)**
24. `branches_comparison` — одна таблица: филиалы × ключевые KPI (выручка, средний чек, загрузка, неявки, маржа, расход материалов на 1 000 ₸ выручки), с подсветкой лучшего и худшего.

**Зарплата (`reports.payroll`)**
25. `payroll` — начисления по сотрудникам за период.

**Ежедневная сводка владельцу** (подписка по умолчанию, 21:00 по времени организации): выручка и поступления за день по филиалам, записи на завтра, неявки, подозрительные события за день, товары ниже минимума. Отправка на email; Telegram-бот — через интерфейс канала, чтобы добавить его позже без переделки.

---

## 12. Фоновые задачи (Hangfire)

Задачи регистрируются как `RecurringJob` при старте воркера; каждая задача обходит организации и выполняется в контексте тенанта (`ITenantContext` задаётся явно). Задачи идемпотентны, при сбое — автоматический retry Hangfire. Событийные задачи (`waitlist_match`) ставятся через `BackgroundJob.Enqueue` после коммита транзакции (outbox-таблица `outbox_messages` + диспетчер, чтобы не потерять событие).


- `send_reminders` — каждые 5 минут: напоминания за 24 ч и за 2 ч по шаблонам. Провайдер отправки — интерфейс `IMessageProvider` с реализациями `ConsoleMessageProvider` (dev) и заглушками `WhatsAppProvider`/`SmsProvider` (конкретный провайдер подключим позже).
- `mark_no_shows` — каждые 15 минут.
- `generate_replenishment_requests` — ежедневно в 03:00 по часовому поясу организации.
- `expiry_alerts` — ежедневно: партии, истекающие в ближайшие 30 дней.
- `send_report_subscriptions` — каждые 5 минут проверка расписаний подписок.
- `waitlist_match` — при событии отмены записи.
- `audit_partition_maintenance` — ежемесячно создаёт партиции.

---

## 13. Безопасность

- Rate limiting на `/auth/*` (`AddRateLimiter`, политика fixed window по IP + логину).
- Блокировка учётной записи после 10 неудачных входов на 15 минут.
- CORS строго на домен фронта; CSRF-защита для refresh-cookie (SameSite=Strict + double submit token).
- Все секреты — через переменные окружения / `IOptions<T>` с `ValidateOnStart()`, `.env.example` без реальных значений; в dev — `dotnet user-secrets`.
- Security headers (HSTS, X-Content-Type-Options, CSP для Scalar), `UseForwardedHeaders` за прокси.
- Загрузка файлов только через presigned URL, проверка MIME и размера (до 20 МБ).
- Поля с ИИН и телефонами маскируются в логах (Serilog destructuring policy / атрибут `[Sensitive]`).
- Уволенный сотрудник (`fired_at`) немедленно теряет доступ: refresh-токены отзываются.
- Резервные копии: скрипт `pg_dump` в S3 с ротацией (описать в README).
- Хостинг и хранение персональных данных должны быть размещены так, чтобы соответствовать законодательству РК о персональных данных (конфигурация региона вынесена в env).

---

## 14. Тестирование (обязательно, CI падает при провале)

- **Unit** (`Dental.Domain.Tests`): расчёт скидок и лимитов, FEFO-подбор партий, расчёт остатков, расчёт зарплаты по всем 4 схемам, расчёт доступных слотов.
- **API** (`Dental.Api.Tests`): `WebApplicationFactory` + Testcontainers PostgreSQL (одна БД на прогон, очистка через Respawn между тестами). Для каждого модуля happy path + права (403 без права) + изоляция тенантов.
- **Архитектура** (`Dental.Architecture.Tests`): Domain не ссылается на EF Core/ASP.NET; эндпоинты не обращаются к `DbContext` напрямую; все сущности с `OrganizationId` реализуют `ITenantEntity` и имеют query filter.
- **Критические сценарии (e2e на уровне API):**
  1. Запись → «пришёл» → визит с услугой по техкарте → оплата частично → закрытие → остаток на складе уменьшился по FEFO → у пациента долг.
  2. Корректировка закрытого визита после закрытия смены администратором без лимита → создан approval_request → старший админ одобряет → сторно и новые движения, аудит с `is_suspicious`.
  3. Перемещение центральный → филиал с недостачей при приёмке → документ списания на разницу в статусе `pending_approval`.
  4. Инвентаризация: визит во время пересчёта → при утверждении расхождение учитывает движение визита.
  5. Автозаявка → сводная потребность сети → заказ поставщику → частичная приёмка → статус `partially_received`.
  6. Попытка двойной записи в одно кресло одновременно (два параллельных запроса) → один успешен, второй 409 `SLOT_CONFLICT`.
  7. Пользователь организации A получает 404 на любой ресурс организации B.
  8. `rebuild-balances` даёт те же остатки, что и кэш, после всех сценариев выше.
- **Отчёты:** на данных seed каждый отчёт возвращает эталонные итоговые суммы (зафиксировать в тестах).
- **Frontend:** Playwright smoke-тесты: вход, создание записи в календаре, перенос drag & drop, приём оплаты, приходный документ, открытие отчёта и экспорт.
- Покрытие (coverlet) ≥ 80% для `Dental.Domain` и `Dental.Application`.

---

## 15. Seed-данные (`make seed`)

Демо-сеть «Дентал Плюс» из 3 филиалов + центральный склад:
- 12 сотрудников (владелец, 2 старших админа, 3 админа, кладовщик, кассир, 6 врачей разных специальностей) с паролем `demo12345`; логины строго: `owner@demo.kz`, `senior1@demo.kz`, `senior2@demo.kz`, `admin1@demo.kz`…`admin3@demo.kz`, `storekeeper@demo.kz`, `cashier@demo.kz`, `doctor1@demo.kz`…`doctor6@demo.kz` (см. раздел 19);
- 6 кресел, графики врачей;
- ~60 услуг в 8 категориях (терапия, хирургия, ортопедия, гигиена, детская, имплантация, ортодонтия, диагностика) с реалистичными ценами в тенге;
- ~120 позиций номенклатуры (композиты, адгезивы, анестетики, перчатки, маски, слепочные массы, импланты с серийным учётом), техкарты для основных услуг;
- 3 поставщика с ценами;
- 500 пациентов, записи и визиты за последние 90 дней + 2 недели вперёд, платежи, несколько долгов, авансов, отмен, неявок, перемещений, списаний, одна инвентаризация с расхождениями, пара подозрительных событий — чтобы все отчёты и дашборд были наполнены.

---

## 16. Этапы реализации и критерии приёмки

Выполняй строго по порядку. После каждого этапа: `dotnet build` без предупреждений, миграции применяются с нуля (`make migrate` на пустую БД), `make lint` и `make test` зелёные, обновлён `PROGRESS.md` (что сделано, что отложено), коммит с понятным сообщением.

**Этап 1 — Каркас.** Docker Compose, solution и проекты по разделу 3, Directory.Build.props/Packages.props, Serilog, OpenAPI + Scalar, tenancy-контекст + query filters + RLS-интерсептор, auth (login/refresh/me/switch-org), роли и права с пресетами, audit-хелпер, формат ошибок, frontend-скелет с layout, входом, переключателями организации и филиала, генерацией типов.
✅ Вход под seed-пользователями работает; тест изоляции тенантов зелёный; меню скрывает разделы без прав. `start.bat` поднимает проект с нуля, `check-roles.bat` — 0 FAIL (SKIP для ещё не реализованных эндпоинтов допустим). Критерий проверяется в конце **каждого** этапа.

**Этап 2 — Организация и справочники.** Филиалы, кабинеты, кресла, сотрудники, роли (UI-редактор), услуги, категории, прайсы, номенклатура, единицы, склады, поставщики, справочники причин.
✅ Всё редактируется через UI; изменения цен видны в журнале аудита.

**Этап 3 — Пациенты и расписание.** Пациенты (поиск, дубли, слияние), графики врачей, исключения, блокировки, записи, календарь с drag & drop, статусы, лист ожидания, no-show воркер, напоминания через ConsoleProvider.
✅ Сценарий 6 из раздела 14 проходит; перенос в календаре пишет аудит; запись вне графика даёт понятную ошибку.

**Этап 4 — Визиты и касса.** Визит, позиции, скидки с лимитами, approval_requests, материалы из техкарт, закрытие, смены, платежи, возвраты, расходы, балансы пациентов, корректировки закрытых визитов.
✅ Сценарии 1 и 2 проходят (списание склада — после этапа 5, пока через заглушку-интерфейс).

**Этап 5 — Склад.** Документы всех типов, движения, балансы, FEFO, партии, серийники, перемещения с приёмкой, списания с лимитами, инвентаризация с блокировкой, импорт из Excel, печать PDF, `rebuild-balances`. Подключить реальное списание к закрытию визита.
✅ Сценарии 1, 3, 4, 8 проходят.

**Этап 6 — Закупки.** Нормы остатков, автозаявки, сводная потребность сети, заказы поставщикам, экспорт, приёмка по заказу, счета поставщиков.
✅ Сценарий 5 проходит.

**Этап 7 — Отчёты и дашборд.** Все 25 отчётов, единый экран отчёта, экспорт Excel/PDF, детализация, подписки и ежедневная сводка (email через mailpit), дашборд филиала и сети.
✅ Тесты эталонных сумм проходят; сводка приходит в mailpit.

**Этап 8 — Зарплата, подозрительные события, аудит-UI, подтверждения-UI.**
✅ Ведомость считается по 4 схемам; владелец видит ленту подозрительных событий и получает уведомления.

**Этап 9 — Полировка.** Playwright smoke-тесты, адаптивность для планшета, пустые состояния, i18n-ключи для всех строк, README (запуск, архитектура, роли, как добавить отчёт, бэкапы), нагрузочная проверка: календарь на 3 филиала и месяц данных открывается быстрее 1 с, отчёт `revenue` за год — быстрее 2 с на seed ×10.

---

## 17. Правила работы для агента

- Не переходи к следующему этапу, пока текущий не соответствует критериям приёмки.
- Бизнес-логика — в `Dental.Domain` и `Dental.Application`, не в эндпоинтах, не в EF-конфигурациях и не в React-компонентах.
- Каждое изменение схемы — через EF Core миграцию (`dotnet ef migrations add`) с понятным именем; не редактируй применённые миграции; после добавления миграции проверь сгенерированный SQL (`dotnet ef migrations script`).
- Не используй `.Result`/`.Wait()`, не отключай nullable-предупреждения через `!` без комментария.
- Не добавляй зависимости вне стека раздела 2 без записи причины в `DECISIONS.md`.
- Не оставляй `TODO` без записи в `PROGRESS.md` в разделе «Отложено».
- Если тест падает — чини код, а не тест, если только тест не противоречит этой спецификации.
- Строки интерфейса — только через i18n-ключи.
- Перед завершением этапа сам пройди основные сценарии через API и убедись, что seed-данные отображаются в UI.

---

## 18. Вне рамок этапа 1 (заложить в архитектуру, не реализовывать)

- Медицинская карта: одонтограмма, протоколы осмотров, снимки DICOM/STL, планы лечения (поле `tooth_numbers` уже есть в `visit_items`).
- Заказ-наряды в зуботехнические лаборатории и кабинет лаборатории (`organizations.type = 'lab'`, `payroll_entries.lab_cost` уже есть).
- B2B-маркетплейс поставщиков с мастер-каталогом (`organizations.type = 'supplier'`, таблицы `suppliers` и `supplier_items` станут локальной проекцией).
- Приложение и онлайн-запись для пациентов (`appointments.source = 'online'` уже предусмотрен).
- Реальные интеграции: WhatsApp Business API, SMS, Kaspi Pay / эквайринг, 1С, ЭЦП, телефония (интерфейсы провайдеров заложены).
- Страховые компании и корпоративные договоры.
---

## 19. Контракт для скриптов запуска и проверки (обязателен)

В корне уже есть `start.bat`, `stop.bat`, `check-roles.bat` и `scripts/*.ps1`, `scripts/roles-matrix.json`. Не переписывай их логику — реализуй API и проект под этот контракт. Если контракт меняется, обнови скрипты и матрицу в том же коммите и запиши причину в DECISIONS.md.

**Порты (dev):** API `http://localhost:5000`, фронтенд `http://localhost:3000`, Mailpit `8025`, Seq `5341`. Фронтенд берёт адрес API из `NEXT_PUBLIC_API_URL`.

**Компоненты, которые ожидает `start.ps1`:**
- `docker-compose.yml` в корне с сервисами `postgres`, `redis`, `mailpit`, `minio`, `seq` (+ `api`, `worker`, `web` для полного запуска в Docker); `.env.example`.
- `backend/Dental.sln`; `backend/src/Dental.Api` принимает аргументы `migrate`, `seed`, `rebuild-balances`, `worker` (запуск Hangfire-сервера без HTTP) и без аргументов стартует HTTP-хост.
- `seed` **идемпотентен**: повторный запуск не дублирует данные (если демо-организация `dental-plus` уже есть — пропуск; `seed --reset` пересоздаёт её).
- `frontend/package.json` со скриптом `dev` (Next.js, принимает `-p <port>`); страница входа — `/login`.

**Эндпоинты:**
- `GET /health` → 200 без авторизации (проверка БД и Redis).
- `POST /api/v1/auth/login` тело `{ "login": "<email или телефон>", "password": "..." }` → 200 `{ "accessToken": "...", "expiresIn": 900 }` + refresh-cookie; неверные данные → 401 `INVALID_CREDENTIALS`.
- `GET /api/v1/auth/me` без токена → 401; с токеном → 200:
```json
{
  "user": { "id": "...", "email": "...", "fullName": "..." },
  "organization": { "id": "...", "slug": "dental-plus", "name": "..." },
  "role": { "id": "...", "code": "owner|senior_admin|admin|storekeeper|cashier|doctor", "name": "..." },
  "permissions": ["patients.view", "..."],
  "limits": { "maxDiscountPct": 5, "maxWriteoffAmount": 2000000, "maxRefundAmount": null, "canEditClosedShiftVisits": false },
  "allBranches": false,
  "branches": [ { "id": "...", "name": "..." } ]
}
```
  `role.code` — код пресета (у кастомной роли — `custom`). Лимиты в тиынах; `null` = без ограничения; лимит, не применимый к роли, отдаётся как `null` или `0`, но поля присутствуют всегда.
- Эндпоинт без нужного права → 403 `FORBIDDEN` (не 404). Для чужой организации — 404 (сценарий 7).

**Матрица ролей** `scripts/roles-matrix.json` — исполняемая версия таблицы 5.4: какие права обязаны быть и каких не должно быть у каждого пресета, лимиты и ожидаемые коды ответов эндпоинтов. Пресеты ролей в seed должны ей соответствовать.
