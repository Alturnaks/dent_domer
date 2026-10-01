using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Patients;
using Dental.Schedule;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Timing;

namespace Dental.Finance;

[Dependency(ReplaceServices = true)]
public class FinancePatientActivityProvider(IRepository<Visit, Guid> visits, IRepository<Payment, Guid> payments, IAppointmentStore appointments, IAsyncQueryableExecuter executer, IClock clock) : IPatientActivityProvider, ITransientDependency
{
    public async Task<IQueryable<Guid>?> GetVisitedSinceQueryAsync(DateTime sinceUtc) => (await visits.GetQueryableAsync()).Where(v => v.Status == VisitStatus.Closed && v.ClosedAt >= sinceUtc).Select(v => v.PatientId);
    public async Task<Dictionary<Guid, PatientActivity>> GetActivityAsync(IReadOnlyCollection<Guid> patientIds)
    {
        var result = patientIds.ToDictionary(id => id, id => new PatientActivity());
        var vs = await visits.GetListAsync(v => patientIds.Contains(v.PatientId) && v.Status == VisitStatus.Closed);
        foreach (var group in vs.GroupBy(v => v.PatientId)) { var a = result[group.Key]; a.LastVisitAt = group.Max(v => v.ClosedAt); a.VisitsCount = group.Count(); a.TotalBilled = group.Sum(v => v.Total); }
        var ps = await payments.GetListAsync(p => patientIds.Contains(p.PatientId) && p.State == 0 && p.Method != PaymentMethod.Balance);
        foreach (var group in ps.GroupBy(p => p.PatientId)) result[group.Key].TotalPaid = group.Sum(p => p.Type == PaymentType.Refund ? -p.Amount : p.Amount);
        var future = await executer.ToListAsync((await appointments.GetQueryableAsync()).Where(a => patientIds.Contains(a.PatientId) && a.StartsAt >= clock.Now && (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed)));
        foreach (var group in future.GroupBy(a => a.PatientId)) result[group.Key].NextAppointmentAt = group.Min(a => a.StartsAt);
        return result;
    }
}
