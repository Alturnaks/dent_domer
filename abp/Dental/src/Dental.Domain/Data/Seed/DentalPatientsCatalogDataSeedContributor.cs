using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Approvals;
using Dental.Branches;
using Dental.Catalog;
using Dental.Patients;
using Dental.References;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;

namespace Dental.Data.Seed;

/// <summary>
/// Демо-данные модулей «Пациенты», «Справочники», «Каталог», «Подтверждения» для арендатора dental-plus
/// (перенесено из backend Dental.Seed: SeedClinic/DemoSeeder). Идемпотентно по разделам.
/// Вызывается и в контексте арендатора, и в контексте хоста (тогда находит демо-арендатора) —
/// так порядок относительно DentalDemoDataSeedContributor не важен.
/// </summary>
public class DentalPatientsCatalogDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly ITenantRepository _tenants;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _uowManager;
    private readonly IGuidGenerator _guid;
    private readonly IConfiguration _configuration;
    private readonly IRepository<LeadSource, Guid> _sources;
    private readonly IRepository<CancelReason, Guid> _cancelReasons;
    private readonly IRepository<ServiceCategory, Guid> _categories;
    private readonly IRepository<ClinicService, Guid> _services;
    private readonly IRepository<PriceList, Guid> _priceLists;
    private readonly IRepository<Patient, Guid> _patients;
    private readonly IRepository<PatientBalance, Guid> _balances;
    private readonly IRepository<PatientConsent, Guid> _consents;
    private readonly IRepository<Branch, Guid> _branches;
    private readonly IRepository<ApprovalRequest, Guid> _approvals;
    private readonly IIdentityUserRepository _users;

    public ILogger<DentalPatientsCatalogDataSeedContributor> Logger { get; set; } = NullLogger<DentalPatientsCatalogDataSeedContributor>.Instance;

    public DentalPatientsCatalogDataSeedContributor(
        ITenantRepository tenants, ICurrentTenant currentTenant, IUnitOfWorkManager uowManager, IGuidGenerator guid, IConfiguration configuration,
        IRepository<LeadSource, Guid> sources, IRepository<CancelReason, Guid> cancelReasons,
        IRepository<ServiceCategory, Guid> categories, IRepository<ClinicService, Guid> services, IRepository<PriceList, Guid> priceLists,
        IRepository<Patient, Guid> patients, IRepository<PatientBalance, Guid> balances, IRepository<PatientConsent, Guid> consents,
        IRepository<Branch, Guid> branches, IRepository<ApprovalRequest, Guid> approvals, IIdentityUserRepository users)
    {
        _tenants = tenants;
        _currentTenant = currentTenant;
        _uowManager = uowManager;
        _guid = guid;
        _configuration = configuration;
        _sources = sources;
        _cancelReasons = cancelReasons;
        _categories = categories;
        _services = services;
        _priceLists = priceLists;
        _patients = patients;
        _balances = balances;
        _consents = consents;
        _branches = branches;
        _approvals = approvals;
        _users = users;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        if (!_configuration.GetValue("Dental:SeedDemo", true))
        {
            return;
        }
        Guid tenantId;
        if (context.TenantId is { } t)
        {
            tenantId = t;
        }
        else
        {
            var tenant = await _tenants.FindByNameAsync(DentalDemoDataSeedContributor.DemoTenantName.ToUpperInvariant());
            if (tenant is null)
            {
                return;
            }
            tenantId = tenant.Id;
        }
        var demo = await _tenants.FindAsync(tenantId);
        if (demo is null || demo.Name != DentalDemoDataSeedContributor.DemoTenantName)
        {
            return; // демо-данные — только для демо-арендатора
        }

        using (_currentTenant.Change(tenantId))
        using (var uow = _uowManager.Begin(requiresNew: true, isTransactional: true))
        {
            var sources = await SeedReferencesAsync(tenantId);
            var services = await SeedCatalogAsync(tenantId);
            await SeedBranchPriceListAsync(tenantId, services);
            await SeedPatientsAsync(tenantId, sources);
            await SeedApprovalsAsync(tenantId);
            await uow.CompleteAsync();
        }
    }

    private async Task<List<LeadSource>> SeedReferencesAsync(Guid tenantId)
    {
        if (!await _cancelReasons.AnyAsync())
        {
            var reasons = new (string, CancelReasonType)[]
            {
                ("Пациент заболел", CancelReasonType.Cancel), ("Не может прийти", CancelReasonType.Cancel),
                ("Передумал лечиться", CancelReasonType.Cancel), ("Болезнь врача", CancelReasonType.Cancel),
                ("Попросил перенести", CancelReasonType.Reschedule), ("Перенос по инициативе клиники", CancelReasonType.Reschedule),
            };
            await _cancelReasons.InsertManyAsync(reasons.Select(r => new CancelReason(_guid.Create(), tenantId, r.Item1, r.Item2)), autoSave: true);
        }
        if (!await _sources.AnyAsync())
        {
            await _sources.InsertManyAsync(new[] { "Instagram", "2GIS", "Рекомендация", "Сайт", "Проходил мимо", "Kaspi" }
                .Select(n => new LeadSource(_guid.Create(), tenantId, n)), autoSave: true);
        }
        return await _sources.GetListAsync();
    }

    private static readonly (string Category, (string Code, string Name, int Duration, int PriceTenge)[] Items)[] Catalog =
    [
        ("Диагностика", [("D01", "Консультация стоматолога", 20, 5000), ("D02", "Прицельный снимок (RVG)", 10, 3000), ("D03", "Ортопантомограмма", 15, 8000),
            ("D04", "КТ одной челюсти", 20, 18000), ("D05", "Консультация ортодонта", 30, 8000), ("D06", "Консультация хирурга-имплантолога", 30, 7000),
            ("D07", "Составление плана лечения", 30, 10000)]),
        ("Терапия", [("T01", "Лечение кариеса (1 поверхность)", 45, 18000), ("T02", "Лечение кариеса (2 поверхности)", 60, 25000), ("T03", "Реставрация фотополимером", 60, 30000),
            ("T04", "Лечение пульпита (1 канал)", 90, 35000), ("T05", "Лечение пульпита (2 канала)", 120, 48000), ("T06", "Лечение пульпита (3 канала)", 150, 60000),
            ("T07", "Перелечивание канала", 120, 45000), ("T08", "Анестезия инфильтрационная", 10, 3000), ("T09", "Анестезия проводниковая", 10, 3500),
            ("T10", "Изолирующая прокладка", 10, 4000), ("T11", "Временная пломба", 15, 3000), ("T12", "Художественная реставрация", 90, 45000)]),
        ("Хирургия", [("S01", "Удаление зуба простое", 30, 12000), ("S02", "Удаление зуба сложное", 60, 25000), ("S03", "Удаление зуба мудрости", 60, 35000),
            ("S04", "Резекция верхушки корня", 60, 40000), ("S05", "Вскрытие абсцесса", 30, 10000), ("S06", "Синус-лифтинг закрытый", 90, 120000),
            ("S07", "Костная пластика", 90, 150000)]),
        ("Ортопедия", [("O01", "Коронка металлокерамическая", 60, 65000), ("O02", "Коронка из диоксида циркония", 60, 120000), ("O03", "Коронка E.max", 60, 130000),
            ("O04", "Винир керамический", 60, 140000), ("O05", "Съёмный протез полный", 60, 180000), ("O06", "Вкладка культевая", 45, 30000),
            ("O07", "Временная коронка", 30, 15000), ("O08", "Снятие слепков", 30, 8000)]),
        ("Гигиена", [("H01", "Профессиональная гигиена полости рта", 60, 25000), ("H02", "Air Flow", 30, 12000), ("H03", "Ультразвуковая чистка", 30, 12000),
            ("H04", "Фторирование", 15, 5000), ("H05", "Отбеливание ZOOM", 90, 120000), ("H06", "Герметизация фиссур", 20, 8000),
            ("H07", "Обучение гигиене", 15, 3000)]),
        ("Детская стоматология", [("P01", "Осмотр ребёнка", 20, 4000), ("P02", "Лечение молочного зуба", 40, 15000), ("P03", "Удаление молочного зуба", 20, 7000),
            ("P04", "Серебрение зубов", 15, 5000), ("P05", "Пульпотомия молочного зуба", 45, 20000), ("P06", "Детская гигиена", 30, 10000)]),
        ("Имплантация", [("I01", "Установка импланта Osstem", 90, 220000), ("I02", "Установка импланта Straumann", 90, 380000), ("I03", "Формирователь десны", 30, 15000),
            ("I04", "Абатмент стандартный", 30, 45000), ("I05", "Коронка на имплант", 60, 150000), ("I06", "Мини-имплант", 60, 90000)]),
        ("Ортодонтия", [("R01", "Установка брекет-системы (1 челюсть)", 120, 250000), ("R02", "Установка керамических брекетов", 120, 350000), ("R03", "Активация брекет-системы", 30, 15000),
            ("R04", "Снятие брекет-системы", 60, 30000), ("R05", "Ретейнер несъёмный", 45, 25000), ("R06", "Элайнеры (этап)", 30, 150000), ("R07", "Пластинка ортодонтическая", 30, 60000)]),
    ];

    /// <summary>Категории, услуги и сетевой прайс 2026 (цены в тиынах).</summary>
    private async Task<List<ClinicService>> SeedCatalogAsync(Guid tenantId)
    {
        if (await _services.AnyAsync())
        {
            return await _services.GetListAsync();
        }
        var sort = 0;
        var services = new List<ClinicService>();
        var network = new PriceList(_guid.Create(), tenantId, "Прайс сети 2026", null, new DateOnly(2026, 1, 1));
        foreach (var (category, items) in Catalog)
        {
            var cat = await _categories.InsertAsync(new ServiceCategory(_guid.Create(), tenantId, category, null, sort++), autoSave: true);
            foreach (var (code, name, duration, price) in items)
            {
                var s = new ClinicService(_guid.Create(), tenantId, cat.Id, code, name, duration);
                services.Add(s);
                network.SetPrice(_guid.Create(), s.Id, price * 100L);
            }
        }
        // Подкатегория для демонстрации дерева.
        var therapy = (await _categories.GetListAsync()).First(c => c.Name == "Терапия");
        var endo = await _categories.InsertAsync(new ServiceCategory(_guid.Create(), tenantId, "Эндодонтия", therapy.Id, 0), autoSave: true);
        foreach (var s in services.Where(s => s.Code is "T04" or "T05" or "T06" or "T07"))
        {
            s.CategoryId = endo.Id;
        }
        await _services.InsertManyAsync(services, autoSave: true);
        await _priceLists.InsertAsync(network, autoSave: true);
        Logger.LogInformation("Seed: {Count} services", services.Count);
        return services;
    }

    /// <summary>Прайс филиала «Есиль» (Астана): терапия и гигиена +10%, с округлением до 100 ₸.</summary>
    private async Task SeedBranchPriceListAsync(Guid tenantId, List<ClinicService> services)
    {
        if (await _priceLists.AnyAsync(l => l.BranchId != null))
        {
            return;
        }
        var branch = (await _branches.GetListAsync()).FirstOrDefault(b => b.Name.Contains("Есиль"));
        var network = (await _priceLists.GetListAsync(l => l.BranchId == null, includeDetails: true)).FirstOrDefault();
        if (branch is null || network is null)
        {
            return;
        }
        var cats = (await _categories.GetListAsync()).Where(c => c.Name is "Терапия" or "Гигиена" or "Эндодонтия").Select(c => c.Id).ToHashSet();
        var astana = new PriceList(_guid.Create(), tenantId, "Прайс Есиль (Астана)", branch.Id, new DateOnly(2026, 3, 1));
        foreach (var s in services.Where(s => cats.Contains(s.CategoryId)))
        {
            var basePrice = network.Items.FirstOrDefault(i => i.ServiceId == s.Id)?.Price;
            if (basePrice is { } p)
            {
                astana.SetPrice(_guid.Create(), s.Id, PriceList.ApplyPercent(p, 10, 10_000));
            }
        }
        await _priceLists.InsertAsync(astana, autoSave: true);
    }

    private static readonly string[] MaleLast = ["Ахметов", "Ибрагимов", "Касымов", "Сейтказиев", "Нурпеисов", "Жумабаев", "Иванов", "Петров", "Смирнов", "Ким", "Ли", "Омаров", "Бекмуханов", "Садыков", "Мухамеджанов", "Кузнецов", "Попов", "Токаев", "Абенов", "Ержанов"];
    private static readonly string[] FemaleLast = ["Ахметова", "Ибрагимова", "Касымова", "Сейтказиева", "Нурпеисова", "Жумабаева", "Иванова", "Петрова", "Смирнова", "Ким", "Ли", "Омарова", "Бекмуханова", "Садыкова", "Мухамеджанова", "Кузнецова", "Попова", "Токаева", "Абенова", "Ержанова"];
    private static readonly string[] MaleFirst = ["Нурлан", "Ерлан", "Данияр", "Арман", "Тимур", "Руслан", "Азамат", "Алексей", "Дмитрий", "Сергей", "Бауыржан", "Асхат", "Максим", "Ерасыл", "Алихан", "Санжар"];
    private static readonly string[] FemaleFirst = ["Айгерим", "Динара", "Асель", "Жанна", "Гульнара", "Мадина", "Анна", "Елена", "Ольга", "Мария", "Алия", "Салтанат", "Камила", "Дана", "Аружан", "Томирис"];
    private static readonly string[] MaleMiddle = ["Серикович", "Маратович", "Болатович", "Ерланович", "Сергеевич", "Александрович", "Нурланович", "Кайратович"];
    private static readonly string[] FemaleMiddle = ["Сериковна", "Маратовна", "Болатовна", "Ерлановна", "Сергеевна", "Александровна", "Нурлановна", "Кайратовна"];
    private static readonly string[] Tags = ["ортодонтия", "имплантация", "ребёнок", "страховка", "боится лечения"];
    private static readonly string[] Operators = ["701", "702", "705", "707", "747", "771", "775", "777", "778"];

    /// <summary>500 пациентов (детерминированно), пара дублей для экрана слияния, долги/авансы, согласия.</summary>
    private async Task SeedPatientsAsync(Guid tenantId, List<LeadSource> sources)
    {
        if (await _patients.AnyAsync())
        {
            return;
        }
        var rnd = new Random(20260928);
        T Pick<T>(IReadOnlyList<T> a) => a[rnd.Next(a.Count)];
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var phones = new HashSet<string>();
        var list = new List<Patient>();
        for (var i = 0; i < 500; i++)
        {
            var male = rnd.Next(2) == 0;
            var birth = new DateOnly(1950, 1, 1).AddDays(rnd.Next(0, 25_550));
            var p = new Patient(_guid.Create(), tenantId,
                male ? Pick(MaleLast) : Pick(FemaleLast),
                male ? Pick(MaleFirst) : Pick(FemaleFirst),
                rnd.NextDouble() < 0.8 ? (male ? Pick(MaleMiddle) : Pick(FemaleMiddle)) : null);
            p.SetBirthDate(birth, today);
            p.Gender = male ? Gender.Male : Gender.Female;
            string phone;
            do
            {
                phone = "7" + Pick(Operators) + rnd.Next(0, 10_000_000).ToString("D7");
            } while (!phones.Add(phone));
            p.SetPhones(phone, null);
            if (rnd.NextDouble() < 0.6)
            {
                p.SetIin(MakeIin(birth, male, rnd));
            }
            p.SourceId = Pick(sources).Id;
            if (rnd.NextDouble() < 0.15)
            {
                p.SetTags([Pick(Tags)]);
            }
            p.IsVip = rnd.NextDouble() < 0.04;
            if (rnd.NextDouble() < 0.3)
            {
                p.Email = $"patient{i + 1}@mail.kz";
            }
            list.Add(p);
        }
        // Пара дублей для экрана слияния (тот же телефон, ФИО и дата рождения, без ИИН).
        var src = list[10];
        var dup = new Patient(_guid.Create(), tenantId, src.LastName, src.FirstName);
        dup.SetBirthDate(src.BirthDate, today);
        dup.Gender = src.Gender;
        dup.SetPhones(src.Phone, null);
        dup.SourceId = src.SourceId;
        list.Add(dup);

        var iins = new HashSet<string>();
        foreach (var p in list.Where(p => p.Iin != null && !iins.Add(p.Iin!)))
        {
            p.SetIin(null);
        }

        await _patients.InsertManyAsync(list, autoSave: true);

        var balances = list.Select(p =>
        {
            var b = new PatientBalance(_guid.Create(), tenantId, p.Id);
            var roll = rnd.NextDouble();
            if (roll < 0.06)
            {
                b.Add(-rnd.Next(5, 150) * 1000 * 100L); // долг 5 000 – 150 000 ₸
            }
            else if (roll < 0.09)
            {
                b.Add(rnd.Next(5, 50) * 1000 * 100L); // аванс
            }
            return b;
        }).ToList();
        await _balances.InsertManyAsync(balances, autoSave: true);

        var consents = list.Take(120).Select(p => new PatientConsent(_guid.Create(), tenantId, p.Id,
            "Согласие на обработку персональных данных", DateTime.UtcNow.AddDays(-rnd.Next(1, 300)), null)).ToList();
        consents.AddRange(list.Skip(5).Take(40).Select(p => new PatientConsent(_guid.Create(), tenantId, p.Id,
            "Информированное добровольное согласие", DateTime.UtcNow.AddDays(-rnd.Next(1, 300)), null)));
        await _consents.InsertManyAsync(consents, autoSave: true);
        Logger.LogInformation("Seed: {Count} patients", list.Count);
    }

    /// <summary>ИИН с корректной контрольной суммой.</summary>
    private static string MakeIin(DateOnly birth, bool male, Random rnd)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var century = birth.Year >= 2000 ? (male ? 5 : 6) : (male ? 3 : 4);
            var body = $"{birth:yyMMdd}{century}{rnd.Next(1000, 10000)}";
            var sum = IinValidator.CheckDigit(body);
            if (sum != 10)
            {
                return body + sum;
            }
        }
        return $"{birth:yyMMdd}300000";
    }

    /// <summary>Несколько запросов на подтверждение, чтобы экран не был пустым (без обработчиков — только статус).</summary>
    private async Task SeedApprovalsAsync(Guid tenantId)
    {
        if (await _approvals.AnyAsync())
        {
            return;
        }
        var admin = await _users.FindByNormalizedEmailAsync("ADMIN1@DEMO.KZ");
        var senior = await _users.FindByNormalizedEmailAsync("SENIOR1@DEMO.KZ");
        var branch = (await _branches.GetListAsync()).FirstOrDefault(b => b.Name.Contains("Абая"));
        if (admin is null || senior is null || branch is null)
        {
            return;
        }
        var patient = (await _patients.GetListAsync()).OrderBy(p => p.CreationTime).FirstOrDefault();

        var discount = new ApprovalRequest(_guid.Create(), tenantId, ApprovalTypes.Discount, "Visit", _guid.Create(), branch.Id, admin.Id);
        discount.SetDetails(12, $"Скидка 12% на визит пациента {patient?.FullName}", "{\"discountPct\":12,\"total\":8500000}");
        var refund = new ApprovalRequest(_guid.Create(), tenantId, ApprovalTypes.Refund, "Payment", _guid.Create(), branch.Id, admin.Id);
        refund.SetDetails(35_000 * 100, "Возврат 35 000 ₸ — отказ от лечения", "{\"amount\":3500000,\"method\":\"card\"}");
        var writeoff = new ApprovalRequest(_guid.Create(), tenantId, ApprovalTypes.Writeoff, "StockWriteoff", _guid.Create(), branch.Id, admin.Id);
        writeoff.SetDetails(62_000 * 100, "Списание просроченных материалов на 62 000 ₸", "{\"amount\":6200000}");
        writeoff.Decide(true, senior.Id, DateTime.UtcNow.AddDays(-1), "Согласовано, просрочка подтверждена");
        await _approvals.InsertManyAsync([discount, refund, writeoff], autoSave: true);
    }
}
