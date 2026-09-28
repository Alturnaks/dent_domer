using Bogus;
using Dental.Domain.Catalog;
using Dental.Domain.Organizations;
using Dental.Domain.Patients;
using Dental.Domain.Scheduling;
using Microsoft.Extensions.Logging;

namespace Dental.Seed;

/// <summary>Услуги, прайсы, графики врачей, пациенты и записи.</summary>
internal sealed partial class SeedContext
{
    public List<Service> Services { get; } = [];
    public Dictionary<Guid, long> Prices { get; } = [];
    public Dictionary<string, ServiceCategory> Categories { get; } = [];
    public List<Patient> PatientsList { get; } = [];
    public List<Appointment> AppointmentsList { get; } = [];
    public List<DoctorSchedule> Schedules { get; } = [];

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

    private async Task SeedCatalogAsync()
    {
        var sort = 0;
        foreach (var (category, items) in Catalog)
        {
            var cat = new ServiceCategory { OrganizationId = Org.Id, Name = category, Sort = sort++ };
            Categories[category] = cat;
            _db.ServiceCategories.Add(cat);
            foreach (var (code, name, duration, price) in items)
            {
                var s = new Service { OrganizationId = Org.Id, CategoryId = cat.Id, Code = code, Name = name, DurationMin = duration };
                Services.Add(s);
                Prices[s.Id] = price * 100L;
            }
        }
        _db.Services.AddRange(Services);

        var network = new PriceList { OrganizationId = Org.Id, Name = "Прайс сети 2026", ValidFrom = new DateOnly(2026, 1, 1) };
        _db.PriceLists.Add(network);
        _db.PriceListItems.AddRange(Services.Select(s => new PriceListItem { OrganizationId = Org.Id, PriceListId = network.Id, ServiceId = s.Id, Price = Prices[s.Id] }));

        // Филиал в Астане — цены на 10% выше на терапию и гигиену.
        var astana = new PriceList { OrganizationId = Org.Id, Name = "Прайс Есиль (Астана)", BranchId = Branches[2].Id, ValidFrom = new DateOnly(2026, 3, 1) };
        _db.PriceLists.Add(astana);
        foreach (var s in Services.Where(s => s.CategoryId == Categories["Терапия"].Id || s.CategoryId == Categories["Гигиена"].Id))
            _db.PriceListItems.Add(new PriceListItem { OrganizationId = Org.Id, PriceListId = astana.Id, ServiceId = s.Id, Price = (long)Math.Round(Prices[s.Id] * 1.1m / 10000m) * 10000 });
        await _db.SaveChangesAsync(ct);
        logger.LogInformation("Seed: {Count} services", Services.Count);
    }

    public long PriceFor(Guid serviceId, Guid branchId)
    {
        var s = Services.First(x => x.Id == serviceId);
        var raise = branchId == Branches[2].Id && (s.CategoryId == Categories["Терапия"].Id || s.CategoryId == Categories["Гигиена"].Id);
        return raise ? (long)Math.Round(Prices[serviceId] * 1.1m / 10000m) * 10000 : Prices[serviceId];
    }

    private async Task SeedSchedulesAsync()
    {
        var from = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-120);
        var branchChairs = Chairs.GroupBy(c => c.BranchId).ToDictionary(g => g.Key, g => g.ToList());
        for (var i = 0; i < Doctors.Count; i++)
        {
            var d = Doctors[i].Membership;
            var branch = d.BranchIds[0];
            var chair = branchChairs[branch][i % 2];
            // Чётные врачи — утро (09-15), нечётные — день (13-20); суббота 10-15 через одного.
            var (start, end) = i % 2 == 0 ? (new TimeOnly(9, 0), new TimeOnly(15, 0)) : (new TimeOnly(13, 0), new TimeOnly(20, 0));
            for (var wd = 1; wd <= 5; wd++)
                Schedules.Add(new DoctorSchedule { OrganizationId = Org.Id, MembershipId = d.Id, BranchId = branch, Weekday = wd, StartTime = start, EndTime = end, ChairId = chair.Id, ValidFrom = from });
            if (i % 2 == 0)
                Schedules.Add(new DoctorSchedule { OrganizationId = Org.Id, MembershipId = d.Id, BranchId = branch, Weekday = 6, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(15, 0), ChairId = chair.Id, ValidFrom = from });
        }
        _db.DoctorSchedules.AddRange(Schedules);
        // Отпуск одного врача на следующей неделе.
        var nextMonday = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(((int)DayOfWeek.Monday - (int)Now.DayOfWeek + 7) % 7 + 7);
        _db.ScheduleExceptions.Add(new ScheduleException { OrganizationId = Org.Id, MembershipId = Doctors[3].Membership.Id, DateFrom = nextMonday, DateTo = nextMonday.AddDays(4), Type = ScheduleExceptionType.Vacation, Comment = "Отпуск" });
        await _db.SaveChangesAsync(ct);
    }

    private static readonly string[] MaleLast = ["Ахметов", "Ибрагимов", "Касымов", "Сейтказиев", "Нурпеисов", "Жумабаев", "Иванов", "Петров", "Смирнов", "Ким", "Ли", "Омаров", "Бекмуханов", "Садыков", "Мухамеджанов", "Кузнецов", "Попов", "Токаев", "Абенов", "Ержанов"];
    private static readonly string[] FemaleLast = ["Ахметова", "Ибрагимова", "Касымова", "Сейтказиева", "Нурпеисова", "Жумабаева", "Иванова", "Петрова", "Смирнова", "Ким", "Ли", "Омарова", "Бекмуханова", "Садыкова", "Мухамеджанова", "Кузнецова", "Попова", "Токаева", "Абенова", "Ержанова"];
    private static readonly string[] MaleFirst = ["Нурлан", "Ерлан", "Данияр", "Арман", "Тимур", "Руслан", "Азамат", "Алексей", "Дмитрий", "Сергей", "Бауыржан", "Асхат", "Максим", "Ерасыл", "Алихан", "Санжар"];
    private static readonly string[] FemaleFirst = ["Айгерим", "Динара", "Асель", "Жанна", "Гульнара", "Мадина", "Анна", "Елена", "Ольга", "Мария", "Алия", "Салтанат", "Камила", "Дана", "Аружан", "Томирис"];
    private static readonly string[] MaleMiddle = ["Серикович", "Маратович", "Болатович", "Ерланович", "Сергеевич", "Александрович", "Нурланович", "Кайратович"];
    private static readonly string[] FemaleMiddle = ["Сериковна", "Маратовна", "Болатовна", "Ерлановна", "Сергеевна", "Александровна", "Нурлановна", "Кайратовна"];

    private async Task SeedPatientsAsync()
    {
        var f = new Faker("ru") { Random = new Randomizer(20260928) };
        for (var i = 0; i < 500; i++)
        {
            var male = f.Random.Bool();
            var birth = DateOnly.FromDateTime(f.Date.Between(new DateTime(1950, 1, 1), new DateTime(2020, 12, 31)));
            var p = new Patient
            {
                OrganizationId = Org.Id,
                LastName = male ? f.PickRandom(MaleLast) : f.PickRandom(FemaleLast),
                FirstName = male ? f.PickRandom(MaleFirst) : f.PickRandom(FemaleFirst),
                MiddleName = f.Random.Bool(0.8f) ? (male ? f.PickRandom(MaleMiddle) : f.PickRandom(FemaleMiddle)) : null,
                BirthDate = birth,
                Gender = male ? Gender.Male : Gender.Female,
                Phone = "7" + f.PickRandom("701", "702", "705", "707", "747", "771", "775", "777", "778") + f.Random.ReplaceNumbers("#######"),
                Iin = f.Random.Bool(0.6f) ? MakeIin(birth, male, f) : null,
                SourceId = f.PickRandom(LeadSources).Id,
                Tags = f.Random.Bool(0.15f) ? [f.PickRandom("ортодонтия", "имплантация", "ребёнок", "страховка", "боится лечения")] : [],
                IsVip = f.Random.Bool(0.04f),
                Email = f.Random.Bool(0.3f) ? f.Internet.Email() : null,
                CreatedAt = Now.AddDays(-f.Random.Int(1, 400)),
            };
            PatientsList.Add(p);
        }
        // Пара дублей для экрана слияния.
        var dupSrc = PatientsList[10];
        PatientsList.Add(new Patient { OrganizationId = Org.Id, LastName = dupSrc.LastName, FirstName = dupSrc.FirstName, BirthDate = dupSrc.BirthDate, Gender = dupSrc.Gender, Phone = dupSrc.Phone, SourceId = dupSrc.SourceId, CreatedAt = Now.AddDays(-5) });
        _db.Patients.AddRange(PatientsList);
        _db.PatientBalances.AddRange(PatientsList.Select(p => new PatientBalance { OrganizationId = Org.Id, PatientId = p.Id, Balance = 0 }));
        await _db.SaveChangesAsync(ct);
        logger.LogInformation("Seed: {Count} patients", PatientsList.Count);
    }

    /// <summary>ИИН с корректной контрольной суммой.</summary>
    private static string MakeIin(DateOnly birth, bool male, Faker f)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var century = birth.Year >= 2000 ? (male ? 5 : 6) : (male ? 3 : 4);
            var body = $"{birth:yyMMdd}{century}{f.Random.Number(1000, 9999)}";
            var d = body.Select(c => c - '0').ToArray();
            int[] w1 = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
            int[] w2 = [3, 4, 5, 6, 7, 8, 9, 10, 11, 1, 2];
            var sum = Enumerable.Range(0, 11).Sum(i => d[i] * w1[i]) % 11;
            if (sum == 10) sum = Enumerable.Range(0, 11).Sum(i => d[i] * w2[i]) % 11;
            if (sum != 10) return body + sum;
        }
        return $"{birth:yyMMdd}300000";
    }

    /// <summary>Записи за 90 дней назад и 2 недели вперёд внутри графиков врачей, без пересечений.</summary>
    private async Task SeedAppointmentsAsync()
    {
        var f = new Faker("ru") { Random = new Randomizer(777) };
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, Tz).DateTime);
        var servicesByCategory = Services.GroupBy(s => s.CategoryId).ToDictionary(g => g.Key, g => g.ToList());
        var doctorCategories = new Dictionary<Guid, string[]>
        {
            [Doctors[0].Membership.Id] = ["Терапия", "Диагностика"],
            [Doctors[1].Membership.Id] = ["Хирургия", "Имплантация", "Диагностика"],
            [Doctors[2].Membership.Id] = ["Ортопедия", "Диагностика"],
            [Doctors[3].Membership.Id] = ["Детская стоматология", "Гигиена"],
            [Doctors[4].Membership.Id] = ["Ортодонтия", "Диагностика"],
            [Doctors[5].Membership.Id] = ["Гигиена", "Терапия"],
        };

        for (var date = today.AddDays(-90); date <= today.AddDays(14); date = date.AddDays(1))
        {
            foreach (var (_, m) in Doctors)
            {
                var sch = Schedules.FirstOrDefault(s => s.MembershipId == m.Id && s.AppliesTo(date));
                if (sch is null) continue;
                var cursor = SlotCalc(date, sch.StartTime);
                var end = SlotCalc(date, sch.EndTime);
                while (cursor < end)
                {
                    // Загрузка ~70% в прошлом, ~45% в будущем.
                    var fill = date < today ? 0.7f : 0.45f;
                    var cats = doctorCategories[m.Id];
                    var svc = f.PickRandom(servicesByCategory[Categories[f.PickRandom(cats)].Id]);
                    var duration = Math.Max(30, (int)Math.Ceiling(svc.DurationMin / 15.0) * 15);
                    var apptEnd = cursor.AddMinutes(duration);
                    if (apptEnd > end) break;
                    if (f.Random.Bool(fill))
                    {
                        var patient = f.PickRandom(PatientsList.Take(500));
                        var status = date < today
                            ? f.Random.WeightedRandom([AppointmentStatus.Completed, AppointmentStatus.Cancelled, AppointmentStatus.NoShow], [0.82f, 0.11f, 0.07f])
                            : date == today ? AppointmentStatus.Confirmed
                            : f.Random.WeightedRandom([AppointmentStatus.Scheduled, AppointmentStatus.Confirmed], [0.6f, 0.4f]);
                        var a = new Appointment
                        {
                            OrganizationId = Org.Id, BranchId = sch.BranchId, PatientId = patient.Id, DoctorId = m.Id, ChairId = sch.ChairId,
                            StartsAt = cursor, EndsAt = apptEnd, Status = status,
                            Source = f.Random.WeightedRandom([AppointmentSource.Phone, AppointmentSource.Admin, AppointmentSource.WalkIn], [0.6f, 0.3f, 0.1f]),
                            CreatedAt = cursor.AddDays(-f.Random.Int(1, 14)),
                            ConfirmedAt = status == AppointmentStatus.Confirmed ? cursor.AddDays(-1) : null,
                            ConfirmedVia = status == AppointmentStatus.Confirmed ? "whatsapp" : null,
                        };
                        if (status == AppointmentStatus.Cancelled)
                        {
                            a.CancelReasonId = f.PickRandom(CancelReasons.Where(r => r.Type == CancelReasonType.Cancel)).Id;
                        }
                        a.Services = [new AppointmentService { OrganizationId = Org.Id, AppointmentId = a.Id, ServiceId = svc.Id, Qty = 1, PlannedPrice = PriceFor(svc.Id, sch.BranchId) }];
                        AppointmentsList.Add(a);
                    }
                    cursor = apptEnd;
                }
            }
        }
        foreach (var chunk in AppointmentsList.Chunk(1000))
        {
            _db.Appointments.AddRange(chunk);
            await _db.SaveChangesAsync(ct);
        }
        logger.LogInformation("Seed: {Count} appointments", AppointmentsList.Count);
    }

    private DateTimeOffset SlotCalc(DateOnly date, TimeOnly time) => SlotCalculator.ToUtc(date.ToDateTime(time), Tz);
}
