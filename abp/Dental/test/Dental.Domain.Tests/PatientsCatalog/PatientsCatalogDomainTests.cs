using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dental.Catalog;
using Dental.Patients;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Dental.PatientsCatalog;

/// <summary>Инварианты пациентов (ИИН, телефон, слияние) и каталога (выбор цены, массовое изменение, техкарты).</summary>
public abstract class PatientsCatalogDomainTests<TStartupModule> : DentalDomainTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    [Theory]
    [InlineData("990913467293", true)]
    [InlineData("990913467290", false)]
    [InlineData("12345", false)]
    [InlineData("99091346729a", false)]
    public void Iin_Checksum(string iin, bool valid) => IinValidator.IsValid(iin).ShouldBe(valid);

    [Theory]
    [InlineData("+7 (701) 123-45-67", "77011234567")]
    [InlineData("8 701 123 45 67", "77011234567")]
    [InlineData("7011234567", "77011234567")]
    public void Phone_Is_Normalized(string input, string expected) => Patient.NormalizePhone(input).ShouldBe(expected);

    [Fact]
    public void Invalid_Phone_And_BirthDate_Are_Rejected()
    {
        var p = new Patient(Guid.NewGuid(), null, "Иванов", "Иван");
        Should.Throw<BusinessException>(() => p.SetPhones("12345", null)).Code.ShouldBe(DentalDomainErrorCodes.InvalidPhone);
        Should.Throw<BusinessException>(() => p.SetBirthDate(new DateOnly(2100, 1, 1), new DateOnly(2026, 1, 1))).Code.ShouldBe(DentalDomainErrorCodes.InvalidBirthDate);
        p.SetTags(["vip", " VIP ", "ребёнок", ""]);
        p.Tags.Count.ShouldBe(2);
    }

    [Fact]
    public void Price_Resolution_Prefers_Branch_Then_Latest_Network()
    {
        var service = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var lists = new List<PriceListPrices>
        {
            new(null, new DateOnly(2026, 1, 1), true, new Dictionary<Guid, long> { [service] = 100 }),
            new(null, new DateOnly(2026, 6, 1), true, new Dictionary<Guid, long> { [service] = 200 }),
            new(null, new DateOnly(2027, 1, 1), true, new Dictionary<Guid, long> { [service] = 999 }), // ещё не действует
            new(branch, new DateOnly(2026, 3, 1), true, new Dictionary<Guid, long> { [service] = 150 }),
            new(Guid.NewGuid(), new DateOnly(2026, 3, 1), true, new Dictionary<Guid, long> { [service] = 1 }), // чужой филиал
        };
        PriceResolver.Resolve(lists, service, branch, new DateOnly(2026, 7, 1)).ShouldBe(150);
        PriceResolver.Resolve(lists, service, Guid.NewGuid(), new DateOnly(2026, 7, 1)).ShouldBe(200);
        PriceResolver.Resolve(lists, service, null, new DateOnly(2026, 2, 1)).ShouldBe(100);
        PriceResolver.Resolve(lists, Guid.NewGuid(), branch, new DateOnly(2026, 7, 1)).ShouldBeNull();
    }

    [Fact]
    public void Bulk_Price_Change_Rounds_And_Validates()
    {
        var list = new PriceList(Guid.NewGuid(), null, "Прайс", null, new DateOnly(2026, 1, 1));
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        list.SetPrice(Guid.NewGuid(), a, 1_800_000); // 18 000 ₸
        list.SetPrice(Guid.NewGuid(), b, 500_000);
        list.BulkChange(id => id == a, 10, 10_000).ShouldBe(1);
        list.Items.Find(i => i.ServiceId == a)!.Price.ShouldBe(1_980_000);
        list.Items.Find(i => i.ServiceId == b)!.Price.ShouldBe(500_000);
        Should.Throw<BusinessException>(() => list.BulkChange(_ => true, -95, 100));
        Should.Throw<BusinessException>(() => list.SetPrice(Guid.NewGuid(), a, -1));
    }

    [Fact]
    public async Task Merge_Moves_Balance_Consents_And_Fills_Empty_Fields()
    {
        var manager = GetRequiredService<PatientManager>();
        var patients = GetRequiredService<IRepository<Patient, Guid>>();
        var balances = GetRequiredService<IRepository<PatientBalance, Guid>>();
        var consents = GetRequiredService<IRepository<PatientConsent, Guid>>();
        Guid mainId = Guid.NewGuid(), dupId = Guid.NewGuid();

        await WithUnitOfWorkAsync(async () =>
        {
            var main = new Patient(mainId, null, "Ким", "Елена");
            main.SetPhones("+7 701 000 00 01", null);
            var dup = new Patient(dupId, null, "Ким", "Елена");
            dup.SetPhones("+7 701 000 00 02", null);
            dup.SetIin("990913467293");
            dup.Email = "kim@mail.kz";
            dup.SetTags(["имплантация"]);
            await manager.InsertAsync(main);
            await manager.InsertAsync(dup);
            (await manager.GetOrCreateBalanceAsync(mainId)).Add(-5_000);
            (await manager.GetOrCreateBalanceAsync(dupId)).Add(20_000);
            await consents.InsertAsync(new PatientConsent(Guid.NewGuid(), null, dupId, "ИДС", DateTime.UtcNow, null));
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var moved = await manager.MergeAsync(await patients.GetAsync(mainId), await patients.GetAsync(dupId));
            moved["consents"].ShouldBe(1);
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var main = await patients.GetAsync(mainId);
            var dup = await patients.GetAsync(dupId);
            dup.MergedIntoId.ShouldBe(mainId);
            dup.Iin.ShouldBeNull();
            main.Iin.ShouldBe("990913467293");
            main.PhoneExtra.ShouldBe("77010000002");
            main.Email.ShouldBe("kim@mail.kz");
            main.Tags.ShouldContain("имплантация");
            (await balances.FirstAsync(x => x.PatientId == mainId)).Balance.ShouldBe(15_000);
            (await balances.FirstAsync(x => x.PatientId == dupId)).Balance.ShouldBe(0);
            (await consents.CountAsync(c => c.PatientId == mainId)).ShouldBe(1);
            await Should.ThrowAsync<BusinessException>(() => manager.MergeAsync(main, dup));
        });
    }

    [Fact]
    public async Task TechCard_New_Version_Deactivates_Previous()
    {
        var techCards = GetRequiredService<TechCardManager>();
        var services = GetRequiredService<IRepository<ClinicService, Guid>>();
        var categories = GetRequiredService<IRepository<ServiceCategory, Guid>>();
        var serviceId = Guid.NewGuid();
        var item = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            var cat = await categories.InsertAsync(new ServiceCategory(Guid.NewGuid(), null, "Тест"), autoSave: true);
            await services.InsertAsync(new ClinicService(serviceId, null, cat.Id, "X01", "Тестовая услуга", 30), autoSave: true);
            await techCards.CreateVersionAsync(serviceId, [(item, 1m)]);
            var v2 = await techCards.CreateVersionAsync(serviceId, [(item, 0.5m), (item, 0.25m)]);
            v2.Version.ShouldBe(2);
            v2.Items.Count.ShouldBe(1);
            v2.Items[0].Quantity.ShouldBe(0.75m);
        });
        await WithUnitOfWorkAsync(async () =>
        {
            var active = await techCards.FindActiveAsync(serviceId);
            active.ShouldNotBeNull();
            active!.Version.ShouldBe(2);
            await Should.ThrowAsync<BusinessException>(() => techCards.CreateVersionAsync(serviceId, [(item, 0m)]));
        });
    }
}
