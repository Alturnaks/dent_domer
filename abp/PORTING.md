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

## Общие сервисы модулей
- **Пациенты** — `Dental.Patients.PatientManager` (дубли, создание с `PatientBalance`, слияние). Баланс — `PatientBalance.Add(delta)` (тиыны, + аванс / − долг), строка создаётся вместе с пациентом; `GetOrCreateBalanceAsync(patientId)`.
  Точки расширения: `IPatientMergeContributor` (модуль, хранящий `PatientId`, переносит свои строки при слиянии), `IPatientActivityProvider` (последний визит, следующая запись, оплачено; заменить `NullPatientActivityProvider` через `[Dependency(ReplaceServices = true)]`; фильтр «не был N мес.» работает через него).
  Вкладки карточки `Pages/Patients/Tabs/_Appointments|_Visits|_Payments.cshtml` (модель `PatientDto`) — заглушки, модули заменяют их. Выбор пациента на других экранах: `dental.patients.patient.getLookup(filter, max)`.
- **Цены** — `Dental.Catalog.IPriceResolver.ResolveAsync(branchId, serviceIds, onDate)`: прайс филиала → сетевой, самый поздний `ValidFrom ≤ даты`. Активная техкарта для списания — `TechCardManager.FindActiveAsync(serviceId)`.
  Номенклатура для техкарт — `Dental.Catalog.IItemLookup` (реализует склад: `Dental.Inventory.CatalogItemLookup`, `[Dependency(ReplaceServices = true)]`; `NullItemLookup` — запасная `TryRegister`).
- **Подтверждения** — `Dental.Approvals.ApprovalManager.CheckOrRequestAsync(type, entityType, entityId, amount, summary, payload, branchId)` → `Allowed` (лимит роли позволяет) или созданный/обновлённый `ApprovalRequest` (сущность модуля переводится в «ожидает подтверждения»).
  Решение исполняет `IApprovalHandler` модуля (класс `*ApprovalHandler : IApprovalHandler, ITransientDependency`, `Type` = `ApprovalTypes.*`); нестандартный лимит — `IApprovalLimitCheck`. Право решающего — `ApprovalTypes.PermissionFor(type)`; свой запрос решает только владелец.
- **Уведомления** — `Dental.Notifications.NotificationManager.NotifyAsync(userId, …)` / `NotifyByPermissionAsync(permission, …, branchId)`; колокольчик в шапке; ссылка по типу сущности — `NotificationAppService.UrlFor`.
- **Справочники** — `LeadSource` (`dental.patients.leadSource`), `CancelReason` (`dental.references.cancelReason`, Cancel/Reschedule); UI `/References` (право `Dental.Org.SettingsManage`).
- Бизнес-ошибки → HTTP-коды: `DentalHttpApiModule.ConfigureErrorStatusCodes` (по умолчанию ABP отдаёт 403).
- Общий JS: `wwwroot/js/dental-common.js` (глобальный бандл) — `dentalUi.money/date/dateTime/phone/enumText/esc`.
- **Склад** (`Dental.Inventory`): `IItemLookup` (имена/единицы товаров для техкарт, закупок), `IVisitStockConsumer` (расход материалов закрытого визита по FEFO + сторно — реализует `StockManager`),
  `IDocumentNumberGenerator.NextAsync(key)` (сквозная нумерация в транзакции; ключ модуля, напр. `"po"`), `IStockLockProvider`.
  Подтверждения: `IApprovalGateway` → `ApprovalManagerGateway` (`ApprovalManager.CheckOrRequestAsync`: списание сверх лимита → `ApprovalRequest`, документ «ожидает подтверждения»;
  отмена документа → `ApprovalManager.CancelPendingAsync`); решения исполняют `WriteoffApprovalHandler` / `TransferShortageApprovalHandler` (`IApprovalHandler`, типы `ApprovalTypes.Writeoff/TransferShortage`).
  `RoleLimitApprovalGateway` (бросает `Dental:RoleLimitExceeded`) — запасная `TryRegister`. Слияние пациентов переносит `StockMovement.PatientId` (`StockMovementsPatientMergeContributor`).
  События: `StockDocumentChangedEto` (ILocalEventBus; action posted/sent/received/cancelled/storno) — закупки подписываются (счёт поставщика по накладной, received_qty заказа, сторно счёта).
- **Демо-seed модулей**: `Dental.Data.Seed.IDentalDemoModuleSeeder` (`Order`, `SeedDemoAsync(tenantId)`) — вызывается `DentalDemoDataSeedContributor` после филиалов и сотрудников
  (либо свой `IDataSeedContributor`, который сам находит демо-арендатора, как `DentalPatientsCatalogDataSeedContributor`).
- **Web**: XSRF-cookie `SameSite=Lax` (`DentalWebModule`) — иначе POST из UI по http получали 400.

## Статус
- Фундамент: арендатор, филиалы, персонал, роли/лимиты, настройки — готово.
- Пациенты (список/поиск/фильтры, карточка, согласия, дубли и слияние, глобальный поиск), Каталог (дерево категорий, услуги, прайсы сеть/филиал, массовое изменение %, версии техкарт, `IPriceResolver`), Подтверждения + уведомления, Справочники (источники, причины отмены) — готово. Вкладки записей/визитов/платежей в карточке — заглушки до соответствующих модулей.
- **Склад (Inventory)** — перенесён: склады (центральный/филиалы, блокировка инвентаризацией), дерево категорий, номенклатура с альт. единицами и учётом партий/сроков/серий, поставщики + цены, нормы мин/опт, документы (приход, перемещение отправка/приёмка с недостачей, списание, возврат поставщику, инвентаризация), проведение/сторно, журнал `AppStockMovements` + кэш `AppStockBalances` (UNIQUE NULLS NOT DISTINCT, `FOR UPDATE`), FEFO, средневзвешенная, пересборка остатков, нумерация `AppDocumentCounters`. UI `/Inventory` (остатки, документы, номенклатура + карточка, поставщики, справочники).
- Волна 1 (пациенты, каталог, подтверждения, склад) слита: одна миграция `Port_Wave1` (вместо модульных `Port_PatientsCatalogApprovals` и `Port_Inventory`; + `pg_trgm` и триграммные GIN-индексы на ФИО/телефоны пациентов, только PostgreSQL). Склад ↔ Подтверждения ↔ Каталог ↔ Пациенты связаны (см. «Склад» выше).
