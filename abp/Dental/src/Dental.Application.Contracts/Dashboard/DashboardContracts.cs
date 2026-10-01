using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Dental.Dashboard;

public class DashboardInput { public Guid? BranchId { get; set; } public DateOnly? Date { get; set; } }
public class DashboardDto
{
    public DateOnly Date { get; set; }
    public string Timezone { get; set; } = "";
    public bool CanSeeFinance { get; set; }
    public int Appointments { get; set; }
    public int Unconfirmed { get; set; }
    public int Arrived { get; set; }
    public int Completed { get; set; }
    public int NoShow { get; set; }
    public decimal? UtilizationPct { get; set; }
    public long ReceivedToday { get; set; }
    public long RevenueWeek { get; set; }
    public long Debt { get; set; }
    public int PendingApprovals { get; set; }
    public int LowStock { get; set; }
    public int ExpiringBatches { get; set; }
    public int OverdueShifts { get; set; }
    public List<BranchDashboardDto> Branches { get; set; } = [];
}
public record BranchDashboardDto(Guid Id, string Name, int Appointments, int Unconfirmed, long? RevenueToday, long? Debt);
public interface IDashboardAppService : IApplicationService { Task<DashboardDto> GetAsync(DashboardInput input); }
