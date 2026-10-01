using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Patients;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Timing;

namespace Dental.Schedule;

public class SchedulePatientMergeContributor(IAppointmentStore appointments, IRepository<WaitlistEntry, Guid> waitlist) : IPatientMergeContributor, ITransientDependency
{
    public string Name => "appointments";
    public async Task<int> MoveAsync(Guid fromPatientId, Guid toPatientId)
    {
        var rows = await appointments.GetListAsync(a => a.PatientId == fromPatientId);
        foreach (var row in rows) { row.MergePatient(toPatientId); await appointments.UpdateAsync(row); }
        var waiting = await waitlist.GetListAsync(w => w.PatientId == fromPatientId);
        foreach (var row in waiting) { row.MergePatient(toPatientId); await waitlist.UpdateAsync(row); }
        return rows.Count + waiting.Count;
    }
}
[Dependency(ReplaceServices = true)]
public class SchedulePatientActivityProvider(IAppointmentStore appointments, IAsyncQueryableExecuter executer, IClock clock) : IPatientActivityProvider, ITransientDependency
{
    public Task<IQueryable<Guid>?> GetVisitedSinceQueryAsync(DateTime sinceUtc) => Task.FromResult<IQueryable<Guid>?>(null);
    public async Task<Dictionary<Guid, PatientActivity>> GetActivityAsync(IReadOnlyCollection<Guid> patientIds)
    {
        var q = (await appointments.GetQueryableAsync()).Where(a => patientIds.Contains(a.PatientId) && a.StartsAt >= clock.Now
            && (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed));
        var rows = await executer.ToListAsync(q);
        return rows.GroupBy(a => a.PatientId).ToDictionary(g => g.Key, g => new PatientActivity { NextAppointmentAt = g.Min(a => a.StartsAt) });
    }
}
