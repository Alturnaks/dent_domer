# ABP-порт Dental — соглашения для модулей

Решение: `abp/Dental/Dental.slnx` (ABP 10.6, MVC Razor Pages + LeptonX Lite, EF Core + PostgreSQL, мультиарендность).
Старая реализация (источник логики): `backend/src/*`, экраны: `frontend/`. Продукт: `SPEC.md`. Правила ABP: `abp/Dental/.cursor/rules`.

## Модель
| Было | Стало |
|---|---|
| Organization | ABP Tenant (`dental-plus`); настройки — `Dental.Org.*` (см. `Domain.Shared/Settings/DentalSettings.cs`) |
| Branch / Room / Chair | `Dental.Branches.Branch/Room/Chair` (`FullAuditedAggregateRoot<Guid>, IMultiTenant`), часы работы — `List<WorkingDay>` в jsonb |
| User + Membership | `IdentityUser` + `Dental.Staff.Employee` (UserId, Position, Specialty, Color, AllBranches, BranchIds `uuid[]`, IsActive/FiredAt) |
| Role (permissions, limits) | `IdentityRole` (права — ABP permission grants) + `Dental.Roles.RoleLimit` (лимиты) |
| `Perm.*` коды | `DentalPermissions.*` (`Domain.Shared/Permissions`), старый код → новый: `DentalPermissions.LegacyCodes` |

Ссылки между агрегатами — только по Id (`Guid BranchId`, `Guid DoctorId` = **Employee.Id**, не UserId).

## Куда класть код модуля `X`
- `Dental.Domain.Shared/X/` — enum, `XConsts` (длины), коды ошибок в `DentalDomainErrorCodes` (+ перевод ключа в `Localization/Dental/ru.json` и `en.json`).
- `Dental.Domain/X/` — сущности (`FullAuditedAggregateRoot<Guid>, IMultiTenant`, `TenantId { get; private set; }`, конструктор с `(Guid id, Guid? tenantId, ...)`, protected пустой конструктор, инварианты в методах), доменные сервисы (`XManager : DomainService`). Физически не удаляем: ISoftDelete даёт FullAudited; документы — отмена/сторно.
- `Dental.Application.Contracts/X/` — DTO + `IXAppService : IApplicationService`.
- `Dental.Application/X/` — `XAppService : DentalAppService`, `[Authorize(DentalPermissions...)]` на методах. Маппинг — Mapperly (`MapperBase<TSrc,TDst>`, пример — `DentalApplicationMappers.cs`) или вручную. API генерируется автоматически: `/api/app/x/...`, JS-прокси `dental.x.x.*`.
- `Dental.EntityFrameworkCore` — `DbSet` в `DentalDbContext` + метод `ConfigureX(this ModelBuilder)` (образец: `DentalDbContextModelCreatingExtensions.ConfigureDentalFoundation`), таблицы `App{Имя}s`, всегда `b.ConfigureByConvention()`. Репозитории — общие `IRepository<T, Guid>` (включены для всех сущностей).
- `Dental.Web/Pages/X/` — заменить заглушку `Index.cshtml` (сейчас `_UnderDevelopment`). Пункт меню уже есть в `Menus/DentalMenuContributor.cs` (с правами). Образец CRUD: `Pages/Branches` (datatable + `abp.ModalManager` + модалки Create/Edit), `Pages/Staff`.
- Строки UI — только `L["Key"]` / `abp.localization.getResource('Dental')`; ключи в `ru.json` (основной) и `en.json`.

## Общие сервисы (фундамент)
- **Филиалы пользователя** — `Dental.Staff.IBranchScope` (в app-сервисе: свойство `BranchScope`):
  `GetAsync()` → `BranchScopeInfo(AllBranches, BranchIds)`, `EnsureCanAccessAsync(branchId)` (403 `Dental:BranchAccessDenied`),
  `ApplyAsync(query, x => x.BranchId)` — фильтр запроса, `GetCurrentEmployeeAsync()` — профиль сотрудника текущего пользователя.
- **Лимиты** — `Dental.Roles.IRoleLimitsProvider` (свойство `RoleLimits`): `GetForCurrentUserAsync()` → `RoleLimitsData`
  (`AllowsDiscount/AllowsWriteoff/AllowsRefund`), `Ensure*AllowedAsync` (бросает `Dental:RoleLimitExceeded`). Суммы — тиыны (long).
- **Списки выбора**: `IBranchAppService.GetLookupAsync()` (доступные филиалы), `GetRoomsAsync/GetChairsAsync(branchId)`,
  `IEmployeeAppService.GetLookupAsync({ branchId, position })` (по умолчанию врачи), `GetCurrentAsync()` (я + филиалы + лимиты).
- **Настройки организации**: `ISettingProvider.GetOrNullAsync(DentalSettings.Timezone)` и т.п.; UI — `/Settings/Organization`.
- Время — UTC (`AbpClockOptions.Kind = Utc`), отчёты по дням — в `Dental.Org.Timezone`. Деньги — `long` тиыны.

## Права и роли
Добавляете право → константа в `DentalPermissions` + `LegacyCodes` (если было в старом стеке) + ключ `Permission:<имя>` в ru/en.json;
включить в пресеты `Domain.Shared/Roles/DentalRolePresets.cs` (seed добавляет недостающие права ролям при каждом запуске DbMigrator).
Роли: «Владелец», «Старший администратор», «Администратор», «Врач» (+ встроенная `admin`). Права ролей редактируются в ABP UI
(Администрирование → Роли), лимиты — `/RoleLimits`.

## Seed
`Dental.Domain/Data/Seed/`: `DentalRolesDataSeedContributor` (каждый арендатор: роли, права, лимиты),
`DentalDemoDataSeedContributor` (хост: создаёт арендатора `dental-plus`, 3 филиала, по 2 кабинета/кресла, 12 сотрудников; выключается `Dental:SeedDemo=false`).
Seed идемпотентен — модуль добавляет свой `IDataSeedContributor` (демо-данные — внутри `CurrentTenant.Change(tenantId)`, проверка «уже есть»).

## Миграции
```
cd abp/Dental
dotnet build src/Dental.DbMigrator
cd src/Dental.EntityFrameworkCore
dotnet ef migrations add <Name> --startup-project ../Dental.DbMigrator
cd ../.. && dotnet build src/Dental.DbMigrator && dotnet run --project src/Dental.DbMigrator
```
(собирать DbMigrator ПЕРЕД `ef` обязательно — иначе ef возьмёт старую сборку и сгенерирует пустую миграцию). Применённые миграции не редактировать.

## Запуск
- Docker: `start-abp.bat` (→ `abp/docker-compose.yml`: postgres:16 на 55433, redis на 56380, migrator, web на http://localhost:3100). Стоп: `stop-abp.bat` (`-Wipe` — удалить БД).
- Локально: `start-abp.bat -Local` (PostgreSQL в Docker, DbMigrator и Web через dotnet; libs — Node 24 + `abp/docker/install-libs.js`).
- Вручную: БД `dental_abp` (abp/abp) на localhost:5432 → `dotnet run --project src/Dental.DbMigrator`, затем `dotnet run --project src/Dental.Web` (http://localhost:3100).
- Вход: на странице входа «Арендатор → Сменить» → `dental-plus`; `owner@demo.kz` … `doctor6@demo.kz` / `demo12345`. API: `POST /connect/token` (client_id `Dental_App`, grant_type=password, заголовок `__tenant: dental-plus`).
- Хост: `admin` / `1q2w3E*` (без арендатора). В каждом арендаторе есть и встроенный `admin` / `1q2w3E*`.
- Тесты: `dotnet test test/Dental.EntityFrameworkCore.Tests` (SQLite in-memory; абстрактные тесты — в Application.Tests/Domain.Tests, запускаются через EFCore.Tests).

## Статус модулей
- **Склад (Inventory)** — перенесён: склады (центральный/филиалы, блокировка инвентаризацией), дерево категорий, номенклатура с альт. единицами и учётом партий/сроков/серий, поставщики + цены, нормы мин/опт, документы (приход, перемещение отправка/приёмка с недостачей, списание, возврат поставщику, инвентаризация), проведение/сторно, журнал `AppStockMovements` + кэш `AppStockBalances` (UNIQUE NULLS NOT DISTINCT, `FOR UPDATE`), FEFO, средневзвешенная, пересборка остатков, нумерация `AppDocumentCounters`. UI `/Inventory` (остатки, документы, номенклатура + карточка, поставщики, справочники). Миграция `Port_Inventory`.

## Общие сервисы (модули)
- **Склад** (`Dental.Inventory`): `IItemLookup` (имена/единицы товаров для техкарт, закупок), `IVisitStockConsumer` (расход материалов закрытого визита по FEFO + сторно — реализует `StockManager`),
  `IDocumentNumberGenerator.NextAsync(key)` (сквозная нумерация в транзакции; ключ модуля, напр. `"po"`), `IStockLockProvider`.
  Подтверждения: `IApprovalGateway` (по умолчанию `RoleLimitApprovalGateway` — бросает `Dental:RoleLimitExceeded`; регистрация `TryRegister`, модуль Approvals заменяет),
  обработчики решений `WriteoffApprovalHandler` / `TransferShortageApprovalHandler` (`IInventoryApprovalHandler`, типы `InventoryApprovalTypes`).
  События: `StockDocumentChangedEto` (ILocalEventBus; action posted/sent/received/cancelled/storno) — закупки подписываются (счёт поставщика по накладной, received_qty заказа, сторно счёта).
- **Демо-seed модулей**: `Dental.Data.Seed.IDentalDemoModuleSeeder` (`Order`, `SeedDemoAsync(tenantId)`) — вызывается `DentalDemoDataSeedContributor` после филиалов и сотрудников.
- **Web**: XSRF-cookie `SameSite=Lax` (`DentalWebModule`) — иначе POST из UI по http получали 400.
