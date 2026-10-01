using System;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Dental.Schedule;

public interface IAppointmentStore : IRepository<Appointment, Guid>
{
    Task SaveAsync(Appointment appointment, bool isNew);
    Task<bool> TryLockTasksAsync(Guid tenantId);
}
