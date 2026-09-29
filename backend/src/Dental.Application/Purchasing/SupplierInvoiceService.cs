using Dental.Application.Common;
using Dental.Application.Schedule;
using Dental.Domain.Common;
using Dental.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace Dental.Application.Purchasing;

/// <summary>
/// Счета поставщиков и долги перед ними. Счёт создаётся при проведении прихода (или вручную);
/// оплата увеличивает paid_amount и пишется в аудит (история оплат — журнал действий по счёту).
/// </summary>
public sealed class SupplierInvoiceService(IAppDbContext db, IAuditService audit, TimeProvider clock)
{
    public async Task<PagedResult<SupplierInvoiceDto>> ListAsync(Guid? supplierId, SupplierInvoiceStatus? status, bool? overdue, Guid? purchaseOrderId,
        PageQuery page, CancellationToken ct)
    {
        var today = await TodayAsync(ct);
        var q = db.SupplierInvoices.AsNoTracking();
        if (supplierId is { } s) q = q.Where(i => i.SupplierId == s);
        if (status is { } st) q = q.Where(i => i.Status == st);
        if (purchaseOrderId is { } po) q = q.Where(i => i.PurchaseOrderId == po);
        if (overdue == true) q = q.Where(i => i.Status != SupplierInvoiceStatus.Paid && i.DueDate != null && i.DueDate < today);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(i => i.Status == SupplierInvoiceStatus.Paid ? 1 : 0).ThenBy(i => i.DueDate ?? i.Date).ThenByDescending(i => i.Date)
            .Skip((page.SafePage - 1) * page.SafeSize).Take(page.SafeSize).ToListAsync(ct);
        return new PagedResult<SupplierInvoiceDto>(await MapAsync(rows, today, ct), total, page.SafePage, page.SafeSize);
    }

    public async Task<SupplierInvoiceDto> GetAsync(Guid id, CancellationToken ct)
    {
        var i = await db.SupplierInvoices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Счёт");
        return (await MapAsync([i], await TodayAsync(ct), ct))[0];
    }

    public async Task<SupplierInvoiceDto> CreateAsync(CreateSupplierInvoiceRequest r, CancellationToken ct)
    {
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == r.SupplierId && s.DeletedAt == null, ct) ?? throw AppException.NotFound("Поставщик");
        if (r.PurchaseOrderId is { } po && !await db.PurchaseOrders.AnyAsync(o => o.Id == po && o.SupplierId == supplier.Id, ct))
            throw AppException.NotFound("Заказ");
        var invoice = new SupplierInvoice
        {
            SupplierId = supplier.Id, PurchaseOrderId = r.PurchaseOrderId, Number = r.Number.Trim(), Date = r.Date, Amount = r.Amount,
            DueDate = r.DueDate ?? (supplier.PaymentTermsDays > 0 ? r.Date.AddDays(supplier.PaymentTermsDays) : null),
        };
        invoice.RecalculateStatus();
        db.SupplierInvoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        return await GetAsync(invoice.Id, ct);
    }

    public async Task<SupplierInvoiceDto> UpdateAsync(Guid id, UpdateSupplierInvoiceRequest r, CancellationToken ct)
    {
        var invoice = await db.SupplierInvoices.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Счёт");
        if (r.Number.NullIfEmpty() is { } n) invoice.Number = n;
        if (r.Date is { } d) invoice.Date = d;
        if (r.DueDate is { } due) invoice.DueDate = due;
        if (r.Amount is { } a)
        {
            if (a <= 0 || a < invoice.PaidAmount + invoice.ReturnedAmount)
                throw AppException.BadRequest(ErrorCodes.AmountInvalid, "Сумма счёта не может быть меньше уже оплаченной");
            invoice.Amount = a;
        }
        invoice.RecalculateStatus();
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Оплата счёта поставщику (частичная или полная). Переплата запрещена.</summary>
    public async Task<SupplierInvoiceDto> PayAsync(Guid id, SupplierPaymentRequest r, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM supplier_invoices WHERE id = {id} FOR UPDATE").ToListAsync(ct);
        var invoice = await db.SupplierInvoices.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Счёт");
        var remaining = invoice.Amount - invoice.PaidAmount - invoice.ReturnedAmount;
        if (remaining <= 0) throw AppException.Conflict(ErrorCodes.DocumentInvalidState, "Счёт уже оплачен");
        if (r.Amount <= 0 || r.Amount > remaining)
            throw AppException.BadRequest(ErrorCodes.AmountInvalid, $"Сумма оплаты должна быть от 0,01 до {new Money(remaining)}",
                new Dictionary<string, object?> { ["remaining"] = remaining });
        var date = r.Date ?? await TodayAsync(ct);
        invoice.PaidAmount += r.Amount;
        invoice.RecalculateStatus();
        audit.Log(nameof(SupplierInvoice), invoice.Id, "payment",
            new { amount = r.Amount, date, invoice.Number, paidAmount = invoice.PaidAmount, status = invoice.Status.ToString() },
            r.Comment.NullIfEmpty() ?? $"Оплата {new Money(r.Amount)} по счёту {invoice.Number}");
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Долги по поставщикам: неоплаченные остатки счетов, в т.ч. просроченные.</summary>
    public async Task<SupplierDebtsDto> DebtsAsync(CancellationToken ct)
    {
        var today = await TodayAsync(ct);
        var open = await db.SupplierInvoices.AsNoTracking().Where(i => i.Status != SupplierInvoiceStatus.Paid).ToListAsync(ct);
        var supplierIds = open.Select(i => i.SupplierId).Distinct().ToList();
        var names = await db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var rows = open.GroupBy(i => i.SupplierId).Select(g =>
        {
            var debt = g.Sum(Remaining);
            var overdue = g.Where(i => i.DueDate is { } d && d < today).Sum(Remaining);
            var next = g.Where(i => i.DueDate is { } d && d >= today).Select(i => i.DueDate).Min();
            return new SupplierDebtRow(g.Key, names.GetValueOrDefault(g.Key, "?"), g.Count(), g.Sum(i => i.Amount), g.Sum(i => i.PaidAmount),
                g.Sum(i => i.ReturnedAmount), debt, overdue, next);
        }).Where(r => r.Debt > 0).OrderByDescending(r => r.OverdueDebt).ThenByDescending(r => r.Debt).ToList();
        return new SupplierDebtsDto(rows, rows.Sum(r => r.Debt), rows.Sum(r => r.OverdueDebt));
    }

    private static long Remaining(SupplierInvoice i) => Math.Max(0, i.Amount - i.PaidAmount - i.ReturnedAmount);

    private async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        var tzId = await db.Organizations.AsNoTracking().Select(o => o.Timezone).FirstAsync(ct);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), ScheduleService.FindTz(tzId)).DateTime);
    }

    private async Task<List<SupplierInvoiceDto>> MapAsync(List<SupplierInvoice> rows, DateOnly today, CancellationToken ct)
    {
        var sIds = rows.Select(r => r.SupplierId).Distinct().ToList();
        var poIds = rows.Where(r => r.PurchaseOrderId != null).Select(r => r.PurchaseOrderId!.Value).Distinct().ToList();
        var docIds = rows.Where(r => r.StockDocumentId != null).Select(r => r.StockDocumentId!.Value).Distinct().ToList();
        var suppliers = await db.Suppliers.AsNoTracking().Where(s => sIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var orders = await db.PurchaseOrders.AsNoTracking().Where(o => poIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Number, ct);
        var docs = await db.StockDocuments.AsNoTracking().Where(d => docIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Number, ct);
        return rows.Select(i =>
        {
            var overdue = i.Status != SupplierInvoiceStatus.Paid && i.DueDate is { } d && d < today;
            return new SupplierInvoiceDto(i.Id, i.SupplierId, suppliers.GetValueOrDefault(i.SupplierId, "?"), i.PurchaseOrderId,
                i.PurchaseOrderId is { } po ? orders.GetValueOrDefault(po) : null, i.StockDocumentId, i.StockDocumentId is { } sd ? docs.GetValueOrDefault(sd) : null,
                i.Number, i.Date, i.Amount, i.DueDate, i.PaidAmount, i.ReturnedAmount, Remaining(i), i.Status, overdue,
                overdue ? today.DayNumber - i.DueDate!.Value.DayNumber : null);
        }).ToList();
    }
}
