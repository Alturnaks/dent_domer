using System;
using System.Threading.Tasks;
using Dental.Branches;
using Dental.Finance;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Dental.Data.Seed;

[ExposeServices(typeof(IDentalDemoModuleSeeder))]
public class FinanceDemoDataSeedContributor(IRepository<Branch, Guid> branches, IRepository<CashRegister, Guid> registers, IRepository<ExpenseCategory, Guid> categories, ICurrentTenant tenant, IUnitOfWorkManager uow, IGuidGenerator guids) : IDentalDemoModuleSeeder, ITransientDependency
{
    public int Order => 200;
    public async Task SeedDemoAsync(Guid tenantId)
    {
        using (tenant.Change(tenantId))
        using (var work = uow.Begin(requiresNew: true, isTransactional: true))
        {
            foreach (var branch in await branches.GetListAsync())
                if (!await registers.AnyAsync(r => r.BranchId == branch.Id)) await registers.InsertAsync(new CashRegister(guids.Create(), tenantId, branch.Id, "Основная касса"));
            var names = new[] { "Аренда", "Коммунальные услуги", "Зарплата", "Материалы", "Лаборатория", "Маркетинг", "Прочее" };
            for (var i = 0; i < names.Length; i++) if (!await categories.AnyAsync(c => c.Name == names[i])) await categories.InsertAsync(new ExpenseCategory(guids.Create(), tenantId, names[i], (ExpenseCategoryType)i));
            await work.CompleteAsync();
        }
    }
}
