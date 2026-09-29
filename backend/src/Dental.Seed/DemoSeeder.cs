using Dapper;
using Dental.Application.Auth;
using Dental.Application.Common;
using Dental.Application.Permissions;
using Dental.Domain.Audit;
using Dental.Domain.Cash;
using Dental.Domain.Inventory;
using Dental.Domain.Organizations;
using Dental.Domain.Patients;
using Dental.Domain.Payroll;
using Dental.Domain.Scheduling;
using Dental.Infrastructure;
using Dental.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dental.Seed;

/// <summary>
/// Демо-сеть «Дентал Плюс» (SPEC §15). Идемпотентно: если организация dental-plus есть — пропуск.
/// --reset очищает бизнес-данные и создаёт всё заново.
/// </summary>
public static class DemoSeeder
{
    public const string Slug = "dental-plus";
    public const string Password = "demo12345";

    public static async Task RunAsync(IServiceProvider root, bool reset, CancellationToken ct)
    {
        var logger = root.GetRequiredService<ILoggerFactory>().CreateLogger("seed");
        var dbOptions = root.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        if (reset)
        {
            await ResetAsync(dbOptions.OwnerConnectionString, ct);
            logger.LogWarning("All business data wiped (--reset)");
        }

        await using (var check = new NpgsqlConnection(dbOptions.OwnerConnectionString))
        {
            var exists = await check.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS (SELECT 1 FROM organizations WHERE slug = @Slug)", new { Slug }, cancellationToken: ct));
            // Демо-данные старой структуры ролей (кладовщик/кассир) пересоздаются автоматически.
            var obsolete = exists && await check.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM roles WHERE code IN ('storekeeper','cashier'))", cancellationToken: ct));
            if (obsolete)
            {
                await ResetAsync(dbOptions.OwnerConnectionString, ct);
                logger.LogWarning("Obsolete demo roles found — demo data recreated");
            }
            else if (exists)
            {
                logger.LogInformation("Demo organization '{Slug}' already exists — seed skipped (use --reset to recreate)", Slug);
                return;
            }
        }

        await using var scope = root.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var ctx = new SeedContext(sp, logger, ct);
        await ctx.RunAsync();
        logger.LogInformation("Demo data created. Logins: owner@demo.kz, senior1-2@demo.kz, admin1-3@demo.kz, doctor1-6@demo.kz / {Password}", Password);
    }

    private static async Task ResetAsync(string ownerCs, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(ownerCs);
        await conn.OpenAsync(ct);
        var tables = (await conn.QueryAsync<string>(new CommandDefinition("""
            SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory'
            """, cancellationToken: ct))).ToList();
        if (tables.Count == 0) return;
        var list = string.Join(", ", tables.Select(t => "\"" + t + "\""));
        await conn.ExecuteAsync(new CommandDefinition($"TRUNCATE {list} CASCADE", cancellationToken: ct));
    }
}

/// <summary>Состояние seed: детерминированный генератор, созданные сущности.</summary>
internal sealed partial class SeedContext(IServiceProvider sp, ILogger logger, CancellationToken ct)
{
    private readonly AppDbContext _db = sp.GetRequiredService<AppDbContext>();
    private readonly ITenantContext _tenant = sp.GetRequiredService<ITenantContext>();
    private readonly IPasswordHashing _hashing = sp.GetRequiredService<IPasswordHashing>();
    private readonly Random _rnd = new(20260928);

    public Organization Org { get; private set; } = null!;
    public List<Branch> Branches { get; } = [];
    public List<Chair> Chairs { get; } = [];
    public Dictionary<string, Role> Roles { get; } = [];
    public List<(User User, Membership Membership)> Staff { get; } = [];
    public List<(User User, Membership Membership)> Doctors { get; } = [];
    public List<Warehouse> Warehouses { get; } = [];
    public Warehouse Central => Warehouses.First(w => w.Type == WarehouseType.Central);
    public List<CashRegister> Registers { get; } = [];
    public List<CancelReason> CancelReasons { get; } = [];
    public List<LeadSource> LeadSources { get; } = [];
    public List<WriteoffReason> WriteoffReasons { get; } = [];
    public List<ExpenseCategory> ExpenseCategories { get; } = [];

    public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;
    public TimeZoneInfo Tz { get; private set; } = TimeZoneInfo.Utc;

    public async Task RunAsync()
    {
        await SeedOrganizationAsync();
        await SeedStaffAsync();
        await SeedReferenceDataAsync();
        await SeedExtendedAsync();
    }

    private async Task SeedOrganizationAsync()
    {
        Org = new Organization
        {
            Name = "Дентал Плюс",
            Slug = DemoSeeder.Slug,
            Type = OrganizationType.ClinicNetwork,
            Timezone = "Asia/Almaty",
            Currency = "KZT",
            Settings = new OrganizationSettings(),
        };
        _tenant.Set(Org.Id);
        Tz = Dental.Infrastructure.Jobs.TimeZones.Find(Org.Timezone);
        _db.Organizations.Add(Org);

        var branchData = new[]
        {
            ("Дентал Плюс — Абая", "г. Алматы, пр. Абая, 150", "+7 727 355-10-10"),
            ("Дентал Плюс — Сатпаева", "г. Алматы, ул. Сатпаева, 30А", "+7 727 355-20-20"),
            ("Дентал Плюс — Есиль", "г. Астана, пр. Мангилик Ел, 55/20", "+7 717 255-30-30"),
        };
        foreach (var (name, addr, phone) in branchData)
        {
            var b = new Branch { OrganizationId = Org.Id, Name = name, Address = addr, Phone = phone, WorkingHours = WorkingHours.Default() };
            Branches.Add(b);
            _db.Branches.Add(b);
            var room1 = new Room { OrganizationId = Org.Id, BranchId = b.Id, Name = "Кабинет 1" };
            var room2 = new Room { OrganizationId = Org.Id, BranchId = b.Id, Name = "Кабинет 2" };
            _db.Rooms.AddRange(room1, room2);
            var c1 = new Chair { OrganizationId = Org.Id, BranchId = b.Id, RoomId = room1.Id, Name = "Кресло 1" };
            var c2 = new Chair { OrganizationId = Org.Id, BranchId = b.Id, RoomId = room2.Id, Name = "Кресло 2" };
            Chairs.AddRange([c1, c2]);
            _db.Chairs.AddRange(c1, c2);
        }

        foreach (var preset in RolePresets.All)
        {
            var role = new Role
            {
                OrganizationId = Org.Id,
                Name = preset.Name,
                Code = preset.Code,
                IsPreset = true,
                Permissions = preset.Permissions.ToList(),
                Limits = new RoleLimits
                {
                    MaxDiscountPct = preset.Limits.MaxDiscountPct,
                    MaxWriteoffAmount = preset.Limits.MaxWriteoffAmount,
                    MaxRefundAmount = preset.Limits.MaxRefundAmount,
                    CanEditClosedShiftVisits = preset.Limits.CanEditClosedShiftVisits,
                },
            };
            Roles[preset.Code] = role;
            _db.Roles.Add(role);
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedStaffAsync()
    {
        var b = Branches;
        var staff = new (string Email, string Name, string Phone, string Role, StaffPosition Pos, bool All, Guid[] BranchIds, string? Specialty, string? Color)[]
        {
            ("owner@demo.kz", "Жаксылыков Нурлан Серикович", "77010000001", RolePresets.Owner, StaffPosition.Owner, true, [], null, null),
            ("senior1@demo.kz", "Ахметова Динара Болатовна", "77010000002", RolePresets.SeniorAdmin, StaffPosition.SeniorAdmin, false, [b[0].Id, b[1].Id], null, null),
            ("senior2@demo.kz", "Ким Елена Викторовна", "77010000003", RolePresets.SeniorAdmin, StaffPosition.SeniorAdmin, false, [b[2].Id], null, null),
            ("admin1@demo.kz", "Смагулова Айгерим Маратовна", "77010000004", RolePresets.Admin, StaffPosition.Admin, false, [b[0].Id], null, null),
            ("admin2@demo.kz", "Петрова Ольга Сергеевна", "77010000005", RolePresets.Admin, StaffPosition.Admin, false, [b[1].Id], null, null),
            ("admin3@demo.kz", "Нурланова Асель Ерлановна", "77010000006", RolePresets.Admin, StaffPosition.Admin, false, [b[2].Id], null, null),
            ("doctor1@demo.kz", "Иманбаев Тимур Русланович", "77010000011", RolePresets.Doctor, StaffPosition.Doctor, false, [b[0].Id], "Терапевт", "#2563eb"),
            ("doctor2@demo.kz", "Сейткали Мадина Нурлановна", "77010000012", RolePresets.Doctor, StaffPosition.Doctor, false, [b[0].Id], "Хирург-имплантолог", "#dc2626"),
            ("doctor3@demo.kz", "Волков Андрей Петрович", "77010000013", RolePresets.Doctor, StaffPosition.Doctor, false, [b[1].Id], "Ортопед", "#16a34a"),
            ("doctor4@demo.kz", "Абдрахманова Жанна Ерболовна", "77010000014", RolePresets.Doctor, StaffPosition.Doctor, false, [b[1].Id], "Детский стоматолог", "#9333ea"),
            ("doctor5@demo.kz", "Тулегенов Арман Бахытович", "77010000015", RolePresets.Doctor, StaffPosition.Doctor, false, [b[2].Id], "Ортодонт", "#ea580c"),
            ("doctor6@demo.kz", "Лебедева Ирина Александровна", "77010000016", RolePresets.Doctor, StaffPosition.Doctor, false, [b[2].Id], "Гигиенист", "#0891b2"),
        };

        foreach (var s in staff)
        {
            var user = new User { Email = s.Email, FullName = s.Name, Phone = s.Phone, IsActive = true, LastOrganizationId = Org.Id };
            user.PasswordHash = _hashing.Hash(user, DemoSeeder.Password);
            _db.Users.Add(user);
            var m = new Membership
            {
                OrganizationId = Org.Id,
                UserId = user.Id,
                RoleId = Roles[s.Role].Id,
                AllBranches = s.All,
                BranchIds = s.BranchIds.ToList(),
                Position = s.Pos,
                Specialty = s.Specialty,
                Color = s.Color,
            };
            _db.Memberships.Add(m);
            Staff.Add((user, m));
            if (s.Pos == StaffPosition.Doctor) Doctors.Add((user, m));
        }
        // Схемы оплаты врачей — все 4 типа (SPEC §6.8), действуют с начала года.
        var validFrom = new DateOnly(DateTime.UtcNow.Year, 1, 1);
        var schemes = new (PayrollSchemeType Type, decimal Percent, long Fixed, long Shift)[]
        {
            (PayrollSchemeType.PercentRevenue, 30, 0, 0),
            (PayrollSchemeType.PercentRevenueMinusMaterials, 40, 0, 0),
            (PayrollSchemeType.FixedPlusPercent, 10, 150_000_00, 0),
            (PayrollSchemeType.PerShift, 0, 0, 25_000_00),
            (PayrollSchemeType.PercentRevenue, 25, 0, 0),
            (PayrollSchemeType.PercentRevenueMinusMaterials, 35, 0, 0),
        };
        for (var i = 0; i < Doctors.Count && i < schemes.Length; i++)
        {
            var sc = schemes[i];
            _db.PayrollSchemes.Add(new PayrollScheme
            {
                OrganizationId = Org.Id, MembershipId = Doctors[i].Membership.Id, Type = sc.Type, Percent = sc.Percent,
                FixedAmount = sc.Fixed, ShiftRate = sc.Shift, ValidFrom = validFrom,
            });
        }
        await _db.SaveChangesAsync(ct);
        logger.LogInformation("Seed: {Count} staff members", Staff.Count);
    }

    private async Task SeedReferenceDataAsync()
    {
        var orgId = Org.Id;
        CancelReasons.AddRange(new[]
        {
            ("Пациент заболел", CancelReasonType.Cancel), ("Не может прийти", CancelReasonType.Cancel),
            ("Передумал лечиться", CancelReasonType.Cancel), ("Болезнь врача", CancelReasonType.Cancel),
            ("Попросил перенести", CancelReasonType.Reschedule), ("Перенос по инициативе клиники", CancelReasonType.Reschedule),
        }.Select(r => new CancelReason { OrganizationId = orgId, Name = r.Item1, Type = r.Item2 }));
        _db.CancelReasons.AddRange(CancelReasons);

        LeadSources.AddRange(new[] { "Instagram", "2GIS", "Рекомендация", "Сайт", "Проходил мимо", "Kaspi" }
            .Select(n => new LeadSource { OrganizationId = orgId, Name = n }));
        _db.LeadSources.AddRange(LeadSources);

        WriteoffReasons.AddRange(new[]
        {
            ("Истёк срок годности", WriteoffReasonType.Expired), ("Повреждение", WriteoffReasonType.Damaged),
            ("Брак", WriteoffReasonType.Defect), ("Недостача / утеря", WriteoffReasonType.Lost), ("Прочее", WriteoffReasonType.Other),
        }.Select(r => new WriteoffReason { OrganizationId = orgId, Name = r.Item1, Type = r.Item2 }));
        _db.WriteoffReasons.AddRange(WriteoffReasons);

        ExpenseCategories.AddRange(new[]
        {
            ("Аренда", ExpenseCategoryType.Rent), ("Коммунальные услуги", ExpenseCategoryType.Utilities),
            ("Зарплата", ExpenseCategoryType.Salary), ("Хозяйственные расходы", ExpenseCategoryType.Supplies),
            ("Зуботехническая лаборатория", ExpenseCategoryType.Lab), ("Маркетинг", ExpenseCategoryType.Marketing),
            ("Прочее", ExpenseCategoryType.Other),
        }.Select(r => new ExpenseCategory { OrganizationId = orgId, Name = r.Item1, Type = r.Item2 }));
        _db.ExpenseCategories.AddRange(ExpenseCategories);

        var storekeeper = Staff.First(s => s.Membership.Position == StaffPosition.SeniorAdmin).Membership.Id;
        Warehouses.Add(new Warehouse { OrganizationId = orgId, Type = WarehouseType.Central, Name = "Центральный склад", ResponsibleId = storekeeper });
        foreach (var b in Branches)
        {
            Warehouses.Add(new Warehouse { OrganizationId = orgId, BranchId = b.Id, Type = WarehouseType.Branch, Name = "Склад " + b.Name.Split('—').Last().Trim(), ResponsibleId = storekeeper });
            Registers.Add(new CashRegister { OrganizationId = orgId, BranchId = b.Id, Name = "Касса регистратуры" });
        }
        _db.Warehouses.AddRange(Warehouses);
        _db.CashRegisters.AddRange(Registers);

        _db.MessageTemplates.AddRange(
            new MessageTemplate { OrganizationId = orgId, Type = "reminder_24h", Channel = MessageChannel.Whatsapp, Text = "Здравствуйте, {patient_name}! Напоминаем о записи {date} в {time} к врачу {doctor}. Адрес: {branch_address}. Ответьте «1» для подтверждения." },
            new MessageTemplate { OrganizationId = orgId, Type = "clinic_cancel", Channel = MessageChannel.Whatsapp, Text = "Здравствуйте, {patient_name}! К сожалению, врач {doctor} не сможет принять вас {date} в {time}. Мы свяжемся с вами и подберём другое удобное время. Приносим извинения." },
            new MessageTemplate { OrganizationId = orgId, Type = "reminder_2h", Channel = MessageChannel.Whatsapp, Text = "{patient_name}, ждём вас сегодня в {time} ({doctor}). Адрес: {branch_address}." },
            new MessageTemplate { OrganizationId = orgId, Type = "recall_6m", Channel = MessageChannel.Whatsapp, Text = "{patient_name}, прошло полгода с последнего визита. Рекомендуем профилактический осмотр — запишитесь по телефону клиники." },
            new MessageTemplate { OrganizationId = orgId, Type = "birthday", Channel = MessageChannel.Whatsapp, Text = "{patient_name}, коллектив «Дентал Плюс» поздравляет вас с днём рождения! Дарим скидку 10% на гигиену в этом месяце." });

        // Ежедневная сводка владельцу — подписка по умолчанию (21:00 по времени организации).
        var owner = Staff.First(s => s.Membership.Position == StaffPosition.Owner).User.Id;
        _db.ReportSubscriptions.Add(new ReportSubscription { OrganizationId = orgId, UserId = owner, ReportCode = "daily_summary", ScheduleCron = "0 21 * * *", Channel = MessageChannel.Email });

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Расширение seed модулями следующих этапов (услуги, склад, пациенты, визиты…).</summary>
    private async Task SeedExtendedAsync()
    {
        await SeedCatalogAsync();
        await SeedSchedulesAsync();
        await SeedPatientsAsync();
        await SeedAppointmentsAsync();
    }
}
