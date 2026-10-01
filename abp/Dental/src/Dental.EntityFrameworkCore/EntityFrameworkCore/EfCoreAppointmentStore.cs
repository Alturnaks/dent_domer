using System;
using System.Linq;
using System.Threading.Tasks;
using Dental.Schedule;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;

namespace Dental.EntityFrameworkCore;

public class EfCoreAppointmentStore(IDbContextProvider<DentalDbContext> provider)
    : EfCoreRepository<DentalDbContext, Appointment, Guid>(provider), IAppointmentStore
{
    public override async Task<IQueryable<Appointment>> WithDetailsAsync() => (await GetQueryableAsync()).Include(a => a.Services);
    public async Task<bool> TryLockTasksAsync(Guid tenantId)
    {
        var db = await GetDbContextAsync();
        if (!db.Database.IsNpgsql()) return true;
        return await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtextextended({tenantId.ToString()}, 0)) AS \"Value\"").SingleAsync();
    }
    public async Task SaveAsync(Appointment appointment, bool isNew)
    {
        try
        {
            if (isNew) await InsertAsync(appointment, autoSave: true);
            else await UpdateAsync(appointment, autoSave: true);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23P01" })
        {
            throw new BusinessException(DentalDomainErrorCodes.AppointmentSlotConflict);
        }
    }
}
