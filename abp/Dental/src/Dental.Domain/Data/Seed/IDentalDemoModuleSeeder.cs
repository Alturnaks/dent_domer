using System;
using System.Threading.Tasks;

namespace Dental.Data.Seed;

/// <summary>
/// Демо-данные модуля для арендатора dental-plus. Вызывается DentalDemoDataSeedContributor после создания
/// филиалов и сотрудников (в контексте хоста; переключение арендатора и UoW — внутри реализации). Должен быть идемпотентным.
/// Регистрация: [ExposeServices(typeof(IDentalDemoModuleSeeder))] + ITransientDependency.
/// </summary>
public interface IDentalDemoModuleSeeder
{
    /// <summary>Порядок выполнения (склад — 100; модули, зависящие от склада, — больше).</summary>
    int Order { get; }

    Task SeedDemoAsync(Guid tenantId);
}
