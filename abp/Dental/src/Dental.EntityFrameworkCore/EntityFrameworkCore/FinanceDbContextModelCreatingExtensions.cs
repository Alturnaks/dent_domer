using System;
using System.Collections.Generic;
using System.Linq;
using Dental.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Dental.EntityFrameworkCore;

public static class FinanceDbContextModelCreatingExtensions
{
    private static void Configure<T>(EntityTypeBuilder<T> b, string name) where T : FinanceEntity
    { b.HasBaseType((Type?)null); b.ToTable(DentalConsts.DbTablePrefix + name, DentalConsts.DbSchema); b.ConfigureByConvention(); b.HasIndex(x => new { x.TenantId, x.Id }); }
    public static void ConfigureFinance(this ModelBuilder builder)
    {
        builder.Entity<Visit>(b => { Configure(b, "Visits"); b.Ignore(v => v.Debt); b.Ignore(v => v.ActiveItems); b.Ignore(v => v.ActiveMaterials); b.Property(v => v.CancelReason).HasMaxLength(2000); b.HasIndex(v => new { v.TenantId, v.BranchId, v.OpenedAt }); b.HasIndex(v => new { v.TenantId, v.AppointmentId }).IsUnique().HasFilter("\"IsDeleted\" = FALSE AND \"Status\" <> 2 AND \"AppointmentId\" IS NOT NULL"); b.HasMany(v => v.Items).WithOne().HasForeignKey(i => i.VisitId).OnDelete(DeleteBehavior.Restrict); b.HasMany(v => v.Materials).WithOne().HasForeignKey(i => i.VisitId).OnDelete(DeleteBehavior.Restrict); });
        builder.Entity<VisitItem>(b => { Configure(b, "VisitItems"); b.Property(i => i.DiscountPct).HasPrecision(5, 2); b.Property(i => i.ToothNumbers).HasConversion(v => string.Join(",", v), v => ParseTeeth(v)).Metadata.SetValueComparer(new ValueComparer<List<int>>((a, c) => a!.SequenceEqual(c!), v => v.Aggregate(0, (hash, tooth) => HashCode.Combine(hash, tooth)), v => v.ToList())); });
        builder.Entity<VisitMaterial>(b => { Configure(b, "VisitMaterials"); b.Property(m => m.Quantity).HasPrecision(18, 6); b.Property(m => m.NormQuantity).HasPrecision(18, 6); });
        builder.Entity<CashRegister>(b => { Configure(b, "CashRegisters"); b.Property(r => r.Name).IsRequired().HasMaxLength(200); });
        builder.Entity<CashShift>(b => { Configure(b, "CashShifts"); b.Property(s => s.CloseComment).HasMaxLength(2000); b.HasIndex(s => new { s.TenantId, s.CashRegisterId }).IsUnique().HasFilter("\"IsDeleted\" = FALSE AND \"Status\" = 0"); });
        builder.Entity<Payment>(b => { Configure(b, "Payments"); b.Property(p => p.Comment).HasMaxLength(2000); b.Property(p => p.IdempotencyKey).HasMaxLength(120); b.Property(p => p.RequestHash).HasMaxLength(64); b.HasIndex(p => new { p.TenantId, p.IdempotencyKey }).IsUnique().HasFilter("\"IsDeleted\" = FALSE AND \"IdempotencyKey\" IS NOT NULL"); b.HasIndex(p => new { p.TenantId, p.BranchId, p.CreationTime }); b.HasIndex(p => p.PatientId); });
        builder.Entity<ExpenseCategory>(b => { Configure(b, "ExpenseCategories"); b.Property(c => c.Name).IsRequired().HasMaxLength(200); });
        builder.Entity<Expense>(b => { Configure(b, "Expenses"); b.Property(e => e.Comment).HasMaxLength(2000); b.Property(e => e.Counterparty).HasMaxLength(200); });
        builder.Entity<CashOperation>(b => { Configure(b, "CashOperations"); b.Property(o => o.Comment).HasMaxLength(2000); });
    }
    private static System.Collections.Generic.List<int> ParseTeeth(string value) => string.IsNullOrEmpty(value) ? [] : System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(value.Split(','), int.Parse));
}
