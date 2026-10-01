# Dental — админ-панель стоматологических клиник

Активная версия: **ABP Framework 10.6.1, .NET 10, MVC Razor Pages, PostgreSQL**, решение `abp/Dental/Dental.slnx`.
Исходная версия на Minimal APIs / Next.js сохранена в `backend/` и `frontend/` как источник бизнес-логики.

Продуктовые требования — `SPEC.md`; соглашения переноса — `abp/PORTING.md`; состояние работы — `PROGRESS.md`; решения — `DECISIONS.md`.

## Запуск ABP

Нужен запущенный Docker Desktop. SDK .NET и Node на Windows для Docker-режима не требуются.

```powershell
.\start-abp.bat -WebPort 3102
```

Скрипт собирает приложение, запускает PostgreSQL/Redis, применяет миграции и демо-seed, поднимает MVC и проверяет вход 12 пользователей.
Порт 3102 позволяет работать рядом со старой версией, использующей 3100. По умолчанию `start-abp.bat` использует 3100.

Сайт: http://localhost:3102. На странице входа выберите арендатора `dental-plus`.
Демо-вход: `owner@demo.kz` / `demo12345`; также доступны `senior1`–`senior2`, `admin1`–`admin3`, `doctor1`–`doctor6` с доменом `@demo.kz` и тем же паролем.

Остановка ABP: `stop-abp.bat`. Запуск старой версии: `start.bat`; её данные хранятся отдельно.

## Что перенесено

Фундамент (филиалы, сотрудники, права/лимиты, настройки), пациенты, каталог, подтверждения/уведомления, склад и графики врачей.
Графики: `/DoctorSchedules` — недельные смены, отпуска, исключения и блокировки времени.
Календарь записей, закупки, визиты/касса, зарплата и отчёты ещё предстоит перенести; заглушки соответствующих экранов не означают готовность модулей.

## Проверки

```powershell
pwsh scripts/check-abp-doctor-schedules.ps1 -BaseUrl http://localhost:3102
```

Проверка работает только с локальным демо-арендатором: оставляет пример недельного графика, удаляет созданные исключения и блокировки мягко. Отчёт — `.run/abp-doctor-schedules-check.txt`.

При наличии локального .NET 10 SDK:

```powershell
cd abp/Dental
dotnet build Dental.slnx
dotnet test test/Dental.EntityFrameworkCore.Tests
```

Тесты используют SQLite in-memory; миграции дополнительно проверяются на пустой PostgreSQL-базе.
