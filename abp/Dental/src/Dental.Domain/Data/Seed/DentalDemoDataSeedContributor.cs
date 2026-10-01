using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Branches;
using Dental.Roles;
using Dental.Staff;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
/// Демо-арендатор «Дентал Плюс» (Name = dental-plus): 3 филиала, кабинеты/кресла, 12 сотрудников.
/// Выполняется в контексте хоста; идемпотентно. Отключается настройкой "Dental:SeedDemo": false.
/// </summary>
public class DentalDemoDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    public const string DemoTenantName = "dental-plus";
    public const string DemoTenantDisplayName = "Дентал Плюс";
    public const string DemoPassword = "demo12345";

    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantManager _tenantManager;
    private readonly IDataSeeder _dataSeeder;
    private readonly ICurrentTenant _currentTenant;
    private readonly IRepository<Branch, Guid> _branchRepository;
    private readonly IRepository<Room, Guid> _roomRepository;
    private readonly IRepository<Chair, Guid> _chairRepository;
    private readonly IRepository<Employee, Guid> _employeeRepository;
    private readonly IdentityUserManager _userManager;
    private readonly EmployeeManager _employeeManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;

    public ILogger<DentalDemoDataSeedContributor> Logger { get; set; } = NullLogger<DentalDemoDataSeedContributor>.Instance;

    public DentalDemoDataSeedContributor(
        ITenantRepository tenantRepository,
        ITenantManager tenantManager,
        IDataSeeder dataSeeder,
        ICurrentTenant currentTenant,
        IRepository<Branch, Guid> branchRepository,
        IRepository<Room, Guid> roomRepository,
        IRepository<Chair, Guid> chairRepository,
        IRepository<Employee, Guid> employeeRepository,
        IdentityUserManager userManager,
        EmployeeManager employeeManager,
        IGuidGenerator guidGenerator,
        IUnitOfWorkManager unitOfWorkManager,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        _tenantRepository = tenantRepository;
        _tenantManager = tenantManager;
        _dataSeeder = dataSeeder;
        _currentTenant = currentTenant;
        _branchRepository = branchRepository;
        _roomRepository = roomRepository;
        _chairRepository = chairRepository;
        _employeeRepository = employeeRepository;
        _userManager = userManager;
        _employeeManager = employeeManager;
        _guidGenerator = guidGenerator;
        _unitOfWorkManager = unitOfWorkManager;
        _configuration = configuration;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        if (context.TenantId != null || !_configuration.GetValue("Dental:SeedDemo", true))
        {
            return;
        }

        Guid tenantId;
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var tenant = await _tenantRepository.FindByNameAsync(DemoTenantName.ToUpperInvariant());
            if (tenant == null)
            {
                tenant = await _tenantManager.CreateAsync(DemoTenantName);
                tenant.SetProperty("DisplayName", DemoTenantDisplayName);
                await _tenantRepository.InsertAsync(tenant, autoSave: true);
                Logger.LogInformation("Demo tenant {Name} created", DemoTenantName);
            }
            tenantId = tenant.Id;
            await uow.CompleteAsync();
        }

        // Стандартный seed арендатора (admin арендатора, права admin, пресеты ролей).
        await _dataSeeder.SeedAsync(new DataSeedContext(tenantId)
            .WithProperty(IdentityDataSeedContributor.AdminEmailPropertyName, DentalConsts.AdminEmailDefaultValue)
            .WithProperty(IdentityDataSeedContributor.AdminPasswordPropertyName, DentalConsts.AdminPasswordDefaultValue));

        // Без requiresNew: роли из вложенного сидинга могут быть ещё не закоммичены во внешней UoW.
        using (_currentTenant.Change(tenantId))
        using (var uow = _unitOfWorkManager.Begin())
        {
            var branches = await SeedBranchesAsync(tenantId);
            await SeedStaffAsync(branches);
            await uow.CompleteAsync();
        }

        // Module seeders use independent transactions. They must run after the enclosing
        // seed transaction commits, otherwise PostgreSQL cannot see the new branches.
        async Task SeedModulesAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            foreach (var seeder in scope.ServiceProvider.GetServices<IDentalDemoModuleSeeder>().OrderBy(s => s.Order))
                await seeder.SeedDemoAsync(tenantId);
        }
        if (_unitOfWorkManager.Current is { } outerUnitOfWork)
            outerUnitOfWork.OnCompleted(SeedModulesAsync);
        else
            await SeedModulesAsync();
    }

    private async Task<List<Branch>> SeedBranchesAsync(Guid tenantId)
    {
        var data = new[]
        {
            ("Дентал Плюс — Абая", "г. Алматы, пр. Абая, 150", "+7 727 355-10-10"),
            ("Дентал Плюс — Сатпаева", "г. Алматы, ул. Сатпаева, 30А", "+7 727 355-20-20"),
            ("Дентал Плюс — Есиль", "г. Астана, пр. Мангилик Ел, 55/20", "+7 717 255-30-30"),
        };
        var result = new List<Branch>();
        foreach (var (name, address, phone) in data)
        {
            var branch = await _branchRepository.FirstOrDefaultAsync(b => b.Name == name);
            if (branch == null)
            {
                branch = await _branchRepository.InsertAsync(
                    new Branch(_guidGenerator.Create(), tenantId, name) { Address = address, Phone = phone },
                    autoSave: true);

                for (var i = 1; i <= 2; i++)
                {
                    var room = await _roomRepository.InsertAsync(
                        new Room(_guidGenerator.Create(), tenantId, branch.Id, $"Кабинет {i}"), autoSave: true);
                    await _chairRepository.InsertAsync(
                        new Chair(_guidGenerator.Create(), tenantId, branch.Id, $"Кресло {i}", room.Id), autoSave: true);
                }
            }
            result.Add(branch);
        }
        return result;
    }

    private async Task SeedStaffAsync(List<Branch> b)
    {
        var staff = new (string Email, string Name, string Phone, string Role, StaffPosition Pos, bool All, Guid[] BranchIds, string? Specialty, string? Color)[]
        {
            ("owner@demo.kz", "Жаксылыков Нурлан Серикович", "77010000001", DentalRoles.Owner, StaffPosition.Owner, true, [], null, null),
            ("senior1@demo.kz", "Ахметова Динара Болатовна", "77010000002", DentalRoles.SeniorAdmin, StaffPosition.SeniorAdmin, false, [b[0].Id, b[1].Id], null, null),
            ("senior2@demo.kz", "Ким Елена Викторовна", "77010000003", DentalRoles.SeniorAdmin, StaffPosition.SeniorAdmin, false, [b[2].Id], null, null),
            ("admin1@demo.kz", "Смагулова Айгерим Маратовна", "77010000004", DentalRoles.Admin, StaffPosition.Admin, false, [b[0].Id], null, null),
            ("admin2@demo.kz", "Петрова Ольга Сергеевна", "77010000005", DentalRoles.Admin, StaffPosition.Admin, false, [b[1].Id], null, null),
            ("admin3@demo.kz", "Нурланова Асель Ерлановна", "77010000006", DentalRoles.Admin, StaffPosition.Admin, false, [b[2].Id], null, null),
            ("doctor1@demo.kz", "Иманбаев Тимур Русланович", "77010000011", DentalRoles.Doctor, StaffPosition.Doctor, false, [b[0].Id], "Терапевт", "#2563eb"),
            ("doctor2@demo.kz", "Сейткали Мадина Нурлановна", "77010000012", DentalRoles.Doctor, StaffPosition.Doctor, false, [b[0].Id], "Хирург-имплантолог", "#dc2626"),
            ("doctor3@demo.kz", "Волков Андрей Петрович", "77010000013", DentalRoles.Doctor, StaffPosition.Doctor, false, [b[1].Id], "Ортопед", "#16a34a"),
            ("doctor4@demo.kz", "Абдрахманова Жанна Ерболовна", "77010000014", DentalRoles.Doctor, StaffPosition.Doctor, false, [b[1].Id], "Детский стоматолог", "#9333ea"),
            ("doctor5@demo.kz", "Тулегенов Арман Бахытович", "77010000015", DentalRoles.Doctor, StaffPosition.Doctor, false, [b[2].Id], "Ортодонт", "#ea580c"),
            ("doctor6@demo.kz", "Лебедева Ирина Александровна", "77010000016", DentalRoles.Doctor, StaffPosition.Doctor, false, [b[2].Id], "Гигиенист", "#0891b2"),
        };

        foreach (var s in staff)
        {
            var user = await _userManager.FindByEmailAsync(s.Email);
            if (user != null && await _employeeRepository.AnyAsync(e => e.UserId == user.Id))
            {
                continue;
            }
            await _employeeManager.CreateAsync(s.Email, DemoPassword, s.Name, s.Phone, s.Pos, [s.Role], s.All, s.BranchIds, s.Specialty, s.Color);
        }
    }
}
