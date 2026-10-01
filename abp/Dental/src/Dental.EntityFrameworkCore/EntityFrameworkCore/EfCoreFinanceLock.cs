using System.Threading.Tasks;
using Dental.Finance;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;

namespace Dental.EntityFrameworkCore;

public class EfCoreFinanceLock(IDbContextProvider<DentalDbContext> provider, ICurrentTenant tenant) : IFinanceLock, ITransientDependency
{
    public async Task AcquireAsync()
    {
        var db = await provider.GetDbContextAsync();
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"finance:" + tenant.Id}, 0))");
    }
}
