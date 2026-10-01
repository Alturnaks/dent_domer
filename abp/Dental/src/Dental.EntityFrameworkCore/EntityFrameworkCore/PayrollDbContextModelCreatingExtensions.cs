using System;
using Dental.Payroll;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;
namespace Dental.EntityFrameworkCore;
public static class PayrollDbContextModelCreatingExtensions
{
    public static void ConfigurePayroll(this ModelBuilder b)
    {
        b.Entity<PayrollScheme>(e => { e.HasBaseType((Type?)null); e.ToTable("AppPayrollSchemes"); e.ConfigureByConvention(); e.Property(x => x.Percent).HasPrecision(5,2); e.HasIndex(x => new {x.TenantId,x.EmployeeId,x.ValidFrom}).IsUnique().HasFilter("\"IsDeleted\" = FALSE"); });
        b.Entity<PayrollPeriod>(e => { e.HasBaseType((Type?)null); e.ToTable("AppPayrollPeriods"); e.ConfigureByConvention(); e.HasIndex(x => new {x.TenantId,x.BranchId,x.PeriodStart}); });
        b.Entity<PayrollEntry>(e => { e.HasBaseType((Type?)null); e.ToTable("AppPayrollEntries"); e.ConfigureByConvention(); e.Property(x => x.Comment).HasMaxLength(2000); e.HasIndex(x => new {x.TenantId,x.PeriodId,x.EmployeeId}).IsUnique().HasFilter("\"IsDeleted\" = FALSE"); e.HasOne<PayrollPeriod>().WithMany().HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Restrict); e.HasOne<Dental.Staff.Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict); });
    }
}
