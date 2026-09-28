using Dental.Application.Catalog;
using Dental.Application.Common;
using Dental.Application.Inventory;
using Dental.Application.Orgs;
using Dental.Application.Permissions;
using Dental.Application.Schedule;
using Dental.Domain.Cash;
using Dental.Domain.Common;
using Dental.Domain.Organizations;
using Dental.Domain.Patients;
using Dental.Domain.Payroll;
using Dental.Domain.Scheduling;
using Dental.Domain.Visits;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Dental.Application.Visits;

public sealed record VisitItemDto(Guid Id, Guid ServiceId, string ServiceName, string ServiceCode, Guid DoctorId, int Qty, long UnitPrice, decimal DiscountPct,
    long DiscountAmount, long Total, IReadOnlyList<int> ToothNumbers, bool DiscountPendingApproval);
public sealed record VisitMaterialDto(Guid Id, Guid? VisitItemId, Guid ItemId, string ItemName, string BaseUnit, decimal Quantity, decimal NormQuantity, Guid? BatchId, long Cost);
public sealed record VisitPaymentDto(Guid Id, DateTimeOffset CreatedAt, PaymentType Type, PaymentMethod Method, long Amount, string? Comment);
public sealed record VisitDto(
    Guid Id, Guid? AppointmentId, Guid BranchId, Guid PatientId, string PatientName, string? PatientPhone, long PatientBalance, Guid DoctorId, string DoctorName,
    Guid? AssistantId, VisitStatus Status, VisitApprovalState ApprovalState, DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt, Guid? CashShiftId, bool ShiftClosed,
    long Subtotal, long DiscountTotal, long Total, long PaidTotal, long Debt, int Version, IReadOnlyList<VisitItemDto> Items,
    IReadOnlyList<VisitMaterialDto> Materials, IReadOnlyList<VisitPaymentDto> Payments, bool CanEdit, bool CanCorrect);

public sealed record VisitItemRequest(Guid ServiceId, int? Qty, decimal? DiscountPct, Guid? DoctorId, IReadOnlyList<int>? ToothNumbers, long? UnitPrice);
public sealed record UpdateVisitRequest(Guid? AssistantId, decimal? DiscountPct, int? Version);
public sealed record VisitMaterialInput(Guid? VisitItemId, Guid ItemId, decimal Quantity);
public sealed record CloseVisitRequest(int? Version);
public sealed record VisitCorrectionItem(Guid ServiceId, int Qty, long UnitPrice, decimal DiscountPct, Guid? DoctorId, IReadOnlyList<int>? ToothNumbers);
public sealed record VisitCorrectionRequest(IReadOnlyList<VisitCorrectionItem> Items, IReadOnlyList<VisitMaterialInput>? Materials, string Reason, int? Version);
public sealed record CancelVisitRequest(string Reason);

public sealed class VisitItemRequestValidator : AbstractValidator<VisitItemRequest>
{
    public VisitItemRequestValidator()
    {
        RuleFor(x => x.ServiceId).NotEmpty();
        RuleFor(x => x.Qty).InclusiveBetween(1, 100).When(x => x.Qty is not null);
        RuleFor(x => x.DiscountPct).InclusiveBetween(0, 100).When(x => x.DiscountPct is not null);
        RuleForEach(x => x.ToothNumbers).Must(t => t is >= 11 and <= 85).WithMessage("Номер зуба по ISO 3950");
    }
}

public sealed class VisitCorrectionRequestValidator : AbstractValidator<VisitCorrectionRequest>
{
    public VisitCorrectionRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithErrorCode(ErrorCodes.CommentRequired).MaximumLength(2000);
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.Qty).InclusiveBetween(1, 100);
            i.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
            i.RuleFor(x => x.DiscountPct).InclusiveBetween(0, 100);
        });
    }
}

public sealed class CancelVisitRequestValidator : AbstractValidator<CancelVisitRequest>
{
    public CancelVisitRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
}

public sealed class VisitService(
    IAppDbContext db,
    ICurrentUser user,
    IAuditService audit,
    ApprovalService approvals,
    CatalogService catalog,
    IVisitStockConsumer stock,
    TimeProvider clock) : IAppointmentArrivalHandler
{
    // ---------- Открытие ----------
    /// <summary>«Пациент пришёл»: запись → arrived, открывается визит, услуги копируются из записи с ценами действующего прайса.</summary>
    public async Task<VisitDto> ArriveAsync(Guid appointmentId, CancellationToken ct)
    {
        var a = await db.Appointments.Include(x => x.Services).FirstOrDefaultAsync(x => x.Id == appointmentId, ct) ?? throw AppException.NotFound("Запись");
        user.EnsureBranchAccess(a.BranchId);
        if (await db.Visits.AnyAsync(v => v.AppointmentId == a.Id && v.Status != VisitStatus.Cancelled, ct))
        {
            var existing = await db.Visits.FirstAsync(v => v.AppointmentId == a.Id && v.Status != VisitStatus.Cancelled, ct);
            return await GetAsync(existing.Id, ct);
        }
        if (!Appointment.CanTransition(a.Status, AppointmentStatus.Arrived))
            throw AppException.Conflict(ErrorCodes.InvalidStatusTransition, "Нельзя отметить приход для записи в этом статусе");
        a.Status = AppointmentStatus.Arrived;
        var id = await OnArrivedAsync(a, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<Guid> OnArrivedAsync(Appointment a, CancellationToken ct)
    {
        var existing = await db.Visits.FirstOrDefaultAsync(v => v.AppointmentId == a.Id && v.Status != VisitStatus.Cancelled, ct);
        if (existing is not null) return existing.Id;
        var services = a.Services.Count > 0 ? a.Services : await db.AppointmentServices.Where(s => s.AppointmentId == a.Id).ToListAsync(ct);
        var prices = await catalog.ResolvePricesAsync(a.BranchId, services.Select(s => s.ServiceId).Distinct().ToList(), ct);
        var org = await db.Organizations.AsNoTracking().FirstAsync(ct);
        var v = new Visit
        {
            AppointmentId = a.Id, BranchId = a.BranchId, PatientId = a.PatientId, DoctorId = a.DoctorId, OpenedAt = clock.GetUtcNow(), Currency = org.Currency,
        };
        foreach (var s in services)
        {
            v.Items.Add(new VisitItem { VisitId = v.Id, ServiceId = s.ServiceId, DoctorId = a.DoctorId, Qty = s.Qty, UnitPrice = prices.GetValueOrDefault(s.ServiceId, s.PlannedPrice) });
        }
        v.Recalculate();
        db.Visits.Add(v);
        await FillMaterialsFromTechCardsAsync(v, ct);
        return v.Id;
    }

    // ---------- Чтение ----------
    public async Task<VisitDto> GetAsync(Guid id, CancellationToken ct)
    {
        var v = await db.Visits.AsNoTracking().Include(x => x.Items).Include(x => x.Materials).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Визит");
        user.EnsureBranchAccess(v.BranchId);
        if (!user.Has(Perm.Schedule.ViewAll) && !user.Has(Perm.Schedule.Manage) && !user.Has(Perm.Cash.PaymentCreate) && v.DoctorId != user.MembershipId)
            throw AppException.Forbidden("Можно открывать только свои визиты");
        var patient = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == v.PatientId, ct);
        var balance = await db.PatientBalances.AsNoTracking().Where(b => b.PatientId == v.PatientId).Select(b => b.Balance).FirstOrDefaultAsync(ct);
        var doctor = await (from m in db.Memberships join u in db.Users on m.UserId equals u.Id where m.Id == v.DoctorId select u.FullName).FirstOrDefaultAsync(ct);
        var serviceIds = v.Items.Select(i => i.ServiceId).Distinct().ToList();
        var services = await db.Services.AsNoTracking().Where(s => serviceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var itemIds = v.Materials.Select(m => m.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var payments = await db.Payments.AsNoTracking().Where(p => p.VisitId == id && !p.PendingApproval).OrderBy(p => p.CreatedAt)
            .Select(p => new VisitPaymentDto(p.Id, p.CreatedAt, p.Type, p.Method, p.Amount, p.Comment)).ToListAsync(ct);
        var shiftClosed = v.CashShiftId is { } sid && await db.CashShifts.AnyAsync(s => s.Id == sid && s.Status == CashShiftStatus.Closed, ct);
        return new VisitDto(v.Id, v.AppointmentId, v.BranchId, v.PatientId, patient.FullName, patient.Phone, balance, v.DoctorId, doctor ?? "?", v.AssistantId,
            v.Status, v.ApprovalState, v.OpenedAt, v.ClosedAt, v.CashShiftId, shiftClosed, v.Subtotal, v.DiscountTotal, v.Total, v.PaidTotal, v.Debt, v.Version,
            v.Items.OrderBy(i => i.CreatedAt).Select(i => new VisitItemDto(i.Id, i.ServiceId, services.GetValueOrDefault(i.ServiceId)?.Name ?? "?",
                services.GetValueOrDefault(i.ServiceId)?.Code ?? "", i.DoctorId, i.Qty, i.UnitPrice, i.DiscountPct, i.DiscountAmount, i.Total, i.ToothNumbers,
                i.DiscountPendingApproval)).ToList(),
            v.Materials.OrderBy(m => items.GetValueOrDefault(m.ItemId)?.Name).Select(m => new VisitMaterialDto(m.Id, m.VisitItemId, m.ItemId,
                items.GetValueOrDefault(m.ItemId)?.Name ?? "?", items.GetValueOrDefault(m.ItemId)?.BaseUnit.ToString() ?? "", m.Quantity, m.NormQuantity, m.BatchId, m.Cost)).ToList(),
            payments,
            v.Status == VisitStatus.Open && (user.Has(Perm.Visits.EditOpen) || user.Has(Perm.Visits.Complete)),
            v.Status == VisitStatus.Closed && user.Has(Perm.Visits.EditClosed));
    }

    // ---------- Позиции ----------
    public async Task<VisitDto> AddItemAsync(Guid visitId, VisitItemRequest r, CancellationToken ct)
    {
        var v = await LoadOpenAsync(visitId, ct);
        var service = await db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == r.ServiceId && s.DeletedAt == null, ct) ?? throw AppException.NotFound("Услуга");
        var price = r.UnitPrice ?? (await catalog.ResolvePricesAsync(v.BranchId, [service.Id], ct)).GetValueOrDefault(service.Id, -1);
        if (price < 0) throw AppException.Conflict(ErrorCodes.PriceNotFound, $"Для услуги «{service.Name}» нет цены в действующем прайсе");
        if (r.UnitPrice is not null && !user.Has(Perm.Catalog.PricesManage)) price = (await catalog.ResolvePricesAsync(v.BranchId, [service.Id], ct)).GetValueOrDefault(service.Id);
        var item = new VisitItem
        {
            VisitId = v.Id, ServiceId = service.Id, DoctorId = r.DoctorId ?? v.DoctorId, Qty = r.Qty ?? 1, UnitPrice = price, ToothNumbers = (r.ToothNumbers ?? []).ToList(),
        };
        v.Items.Add(item);
        await ApplyDiscountAsync(v, item, r.DiscountPct ?? 0, ct);
        v.Recalculate();
        await FillMaterialsForItemAsync(v, item, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(v.Id, ct);
    }

    public async Task<VisitDto> UpdateItemAsync(Guid visitId, Guid itemId, VisitItemRequest r, CancellationToken ct)
    {
        var v = await LoadOpenAsync(visitId, ct);
        var item = v.Items.FirstOrDefault(i => i.Id == itemId) ?? throw AppException.NotFound("Позиция");
        var qtyChanged = r.Qty is { } q && q != item.Qty;
        if (r.Qty is not null) item.Qty = r.Qty.Value;
        if (r.DoctorId is not null) item.DoctorId = r.DoctorId.Value;
        if (r.ToothNumbers is not null) item.ToothNumbers = r.ToothNumbers.ToList();
        if (r.UnitPrice is not null && user.Has(Perm.Catalog.PricesManage)) item.UnitPrice = r.UnitPrice.Value;
        if (r.DiscountPct is not null) await ApplyDiscountAsync(v, item, r.DiscountPct.Value, ct);
        v.Recalculate();
        if (qtyChanged)
        {
            foreach (var m in v.Materials.Where(m => m.VisitItemId == item.Id).ToList()) { v.Materials.Remove(m); db.VisitMaterialUsages.Remove(m); }
            await FillMaterialsForItemAsync(v, item, ct);
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(v.Id, ct);
    }

    /// <summary>Удаление позиции открытого визита (визит ещё не закрыт — это черновик, физическое удаление строки допустимо и пишется в аудит).</summary>
    public async Task<VisitDto> RemoveItemAsync(Guid visitId, Guid itemId, CancellationToken ct)
    {
        var v = await LoadOpenAsync(visitId, ct);
        var item = v.Items.FirstOrDefault(i => i.Id == itemId) ?? throw AppException.NotFound("Позиция");
        foreach (var m in v.Materials.Where(m => m.VisitItemId == item.Id).ToList()) { v.Materials.Remove(m); db.VisitMaterialUsages.Remove(m); }
        v.Items.Remove(item);
        db.VisitItems.Remove(item);
        audit.Log(nameof(Visit), v.Id, "item_remove", new { item.ServiceId, item.Qty, item.UnitPrice, item.DiscountPct }, branchId: v.BranchId);
        v.Recalculate();
        await RecalcApprovalStateAsync(v, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(v.Id, ct);
    }

    public async Task<VisitDto> UpdateAsync(Guid visitId, UpdateVisitRequest r, CancellationToken ct)
    {
        var v = await LoadOpenAsync(visitId, ct);
        db.SetExpectedVersion(v, r.Version);
        if (r.AssistantId is not null) v.AssistantId = r.AssistantId == Guid.Empty ? null : r.AssistantId;
        if (r.DiscountPct is { } pct)
        {
            // Скидка на весь визит — применяется ко всем позициям.
            foreach (var i in v.Items) await ApplyDiscountAsync(v, i, pct, ct);
        }
        v.Recalculate();
        await db.SaveChangesAsync(ct);
        return await GetAsync(v.Id, ct);
    }

    /// <summary>Скидка выше лимита роли → approval_request, визит нельзя закрыть до подтверждения.</summary>
    private async Task ApplyDiscountAsync(Visit v, VisitItem item, decimal pct, CancellationToken ct)
    {
        if (pct < 0 || pct > 100) throw AppException.BadRequest(ErrorCodes.DiscountInvalid, "Скидка должна быть от 0 до 100%");
        if (pct > 0) user.EnsurePermission(Perm.Catalog.DiscountsApply);
        item.DiscountPct = pct;
        item.Recalculate();
        if (DiscountCalculator.RequiresApproval(pct, user.Limits.MaxDiscountPct))
        {
            item.DiscountPendingApproval = true;
            v.ApprovalState = VisitApprovalState.PendingDiscount;
            await approvals.RequestAsync(ApprovalType.Discount, nameof(Visit), v.Id, v.Items.Max(i => i.DiscountPct),
                $"Скидка {pct}% (лимит роли {user.Limits.MaxDiscountPct}%) в визите", new { visitId = v.Id, itemId = item.Id, pct, limit = user.Limits.MaxDiscountPct }, v.BranchId, ct);
            audit.Log(nameof(Visit), v.Id, "discount_over_limit", new { item.ServiceId, pct, limit = user.Limits.MaxDiscountPct }, suspicious: true, branchId: v.BranchId);
        }
        else
        {
            item.DiscountPendingApproval = false;
            await RecalcApprovalStateAsync(v, ct);
        }
    }

    private async Task RecalcApprovalStateAsync(Visit v, CancellationToken ct)
    {
        if (v.ApprovalState == VisitApprovalState.PendingDiscount && !v.Items.Any(i => i.DiscountPendingApproval))
        {
            v.ApprovalState = VisitApprovalState.None;
            var pending = await db.ApprovalRequests.Where(a => a.EntityId == v.Id && a.Type == ApprovalType.Discount && a.Status == ApprovalStatus.Pending).ToListAsync(ct);
            foreach (var p in pending) { p.Status = ApprovalStatus.Rejected; p.Comment = "Скидка изменена в пределах лимита"; p.DecidedAt = clock.GetUtcNow(); }
        }
    }

    // ---------- Материалы ----------
    public async Task<IReadOnlyList<VisitMaterialDto>> GetMaterialsAsync(Guid visitId, CancellationToken ct) => (await GetAsync(visitId, ct)).Materials;

    public async Task<VisitDto> SetMaterialsAsync(Guid visitId, IReadOnlyList<VisitMaterialInput> materials, CancellationToken ct)
    {
        var v = await LoadOpenAsync(visitId, ct);
        var norms = v.Materials.GroupBy(m => (m.VisitItemId, m.ItemId)).ToDictionary(g => g.Key, g => g.Sum(x => x.NormQuantity));
        foreach (var m in v.Materials.ToList()) { v.Materials.Remove(m); db.VisitMaterialUsages.Remove(m); }
        foreach (var m in materials.Where(m => m.Quantity > 0))
        {
            v.Materials.Add(new VisitMaterialUsage
            {
                VisitId = v.Id, VisitItemId = m.VisitItemId, ItemId = m.ItemId, Quantity = m.Quantity, NormQuantity = norms.GetValueOrDefault((m.VisitItemId, m.ItemId)),
            });
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(v.Id, ct);
    }

    private async Task FillMaterialsFromTechCardsAsync(Visit v, CancellationToken ct)
    {
        foreach (var item in v.Items) await FillMaterialsForItemAsync(v, item, ct);
    }

    private async Task FillMaterialsForItemAsync(Visit v, VisitItem item, CancellationToken ct)
    {
        var card = await db.TechCards.AsNoTracking().Include(t => t.Items).Where(t => t.ServiceId == item.ServiceId && t.IsActive)
            .OrderByDescending(t => t.Version).FirstOrDefaultAsync(ct);
        if (card is null) return;
        foreach (var ti in card.Items)
        {
            var qty = ti.Quantity * item.Qty;
            v.Materials.Add(new VisitMaterialUsage { VisitId = v.Id, VisitItemId = item.Id, ItemId = ti.ItemId, Quantity = qty, NormQuantity = qty });
        }
    }

    // ---------- Закрытие ----------
    /// <summary>
    /// Закрытие: фиксирует цены, списывает материалы (FEFO; при нехватке — в минус с уведомлением), выставляет долг total − paid,
    /// привязывает визит к открытой смене кассы филиала.
    /// </summary>
    public async Task<VisitDto> CloseAsync(Guid visitId, CloseVisitRequest r, CancellationToken ct)
    {
        user.EnsurePermission(Perm.Visits.Complete);
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var v = await LoadOpenAsync(visitId, ct);
        db.SetExpectedVersion(v, r.Version);
        if (v.ApprovalState != VisitApprovalState.None) throw AppException.Conflict(ErrorCodes.VisitPendingApproval, "Визит ожидает подтверждения скидки");
        if (v.Items.Count == 0) throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Добавьте хотя бы одну услугу");
        v.Recalculate();
        v.Status = VisitStatus.Closed;
        v.ClosedAt = clock.GetUtcNow();
        v.ClosedBy = user.UserId;
        v.CashShiftId = await db.CashShifts.Where(s => s.BranchId == v.BranchId && s.Status == CashShiftStatus.Open).OrderByDescending(s => s.OpenedAt)
            .Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);

        if (v.Materials.Count > 0)
        {
            var (docId, costs) = await stock.ConsumeAsync(v.Id, v.BranchId, v.PatientId, v.Materials.Select(m => (m.Id, m.ItemId, m.Quantity)).ToList(), ct);
            v.ConsumptionDocumentId = docId;
            foreach (var m in v.Materials)
            {
                if (!costs.TryGetValue(m.Id, out var c)) continue;
                m.Cost = c.Cost;
                m.BatchId = c.BatchId;
            }
        }

        await AdjustBalanceAsync(v.PatientId, -v.Total, ct);
        if (v.AppointmentId is { } aid)
        {
            var a = await db.Appointments.FirstOrDefaultAsync(x => x.Id == aid, ct);
            if (a is not null && a.Status != AppointmentStatus.Completed) a.Status = AppointmentStatus.Completed;
        }
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await GetAsync(v.Id, ct);
    }

    // ---------- Корректировка закрытого визита ----------
    /// <summary>
    /// Изменение закрытого визита: смена открыта — нужно право visits.edit_closed и комментарий; смена закрыта — ещё лимит
    /// can_edit_closed_shift_visits, иначе approval_request. Исходные записи не переписываются: сторно расхода + новый расход,
    /// корректировка баланса на разницу. Аудит с is_suspicious.
    /// </summary>
    public async Task<VisitDto> CorrectAsync(Guid visitId, VisitCorrectionRequest r, CancellationToken ct)
    {
        user.EnsurePermission(Perm.Visits.EditClosed);
        var v = await db.Visits.Include(x => x.Items).Include(x => x.Materials).FirstOrDefaultAsync(x => x.Id == visitId, ct) ?? throw AppException.NotFound("Визит");
        user.EnsureBranchAccess(v.BranchId);
        db.SetExpectedVersion(v, r.Version);
        if (v.Status != VisitStatus.Closed) throw AppException.Conflict(ErrorCodes.VisitNotClosed, "Корректировка доступна только для закрытого визита");
        if (string.IsNullOrWhiteSpace(r.Reason)) throw AppException.BadRequest(ErrorCodes.CommentRequired, "Укажите причину корректировки");

        var shiftClosed = v.CashShiftId is null || await db.CashShifts.AnyAsync(s => s.Id == v.CashShiftId && s.Status == CashShiftStatus.Closed, ct);
        var payrollLocked = await IsPayrollLockedAsync(v, ct);
        if ((shiftClosed && !user.Limits.CanEditClosedShiftVisits) || payrollLocked)
        {
            v.ApprovalState = VisitApprovalState.PendingCorrection;
            var type = payrollLocked ? ApprovalType.PayrollPeriodChange : ApprovalType.ClosedVisitEdit;
            var newTotal = r.Items.Sum(i => Money.Zero().Add(new Money(i.UnitPrice * i.Qty)).Subtract(new Money(i.UnitPrice * i.Qty).Percent(i.DiscountPct)).Minor);
            await approvals.RequestAsync(type, nameof(Visit), v.Id, Math.Abs(newTotal - v.Total),
                payrollLocked ? "Изменение визита в утверждённом периоде зарплаты" : "Изменение визита после закрытия смены",
                new VisitCorrectionPayload(r.Items, r.Materials, r.Reason, v.Total, newTotal, user.UserId), v.BranchId, ct);
            audit.Log(nameof(Visit), v.Id, "closed_visit_edit_requested", new { oldTotal = v.Total, newTotal }, r.Reason, suspicious: true, branchId: v.BranchId);
            await db.SaveChangesAsync(ct);
            return await GetAsync(v.Id, ct);
        }

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await ApplyCorrectionAsync(v, r.Items, r.Materials, r.Reason, ct);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await GetAsync(v.Id, ct);
    }

    private async Task<bool> IsPayrollLockedAsync(Visit v, CancellationToken ct)
    {
        if (v.ClosedAt is null) return false;
        var date = DateOnly.FromDateTime(v.ClosedAt.Value.UtcDateTime);
        return await db.PayrollPeriods.AnyAsync(p => p.BranchId == v.BranchId && p.Status != PayrollPeriodStatus.Draft && p.PeriodStart <= date && p.PeriodEnd >= date, ct);
    }

    internal async Task ApplyCorrectionAsync(Visit v, IReadOnlyList<VisitCorrectionItem> items, IReadOnlyList<VisitMaterialInput>? materials, string reason, CancellationToken ct)
    {
        var before = new
        {
            v.Total,
            items = v.Items.Select(i => new { i.ServiceId, i.Qty, i.UnitPrice, i.DiscountPct, i.Total }).ToList(),
            materials = v.Materials.Select(m => new { m.ItemId, m.Quantity }).ToList(),
        };
        var oldTotal = v.Total;

        // Позиции: старые помечаются заменёнными (удаляются из визита), новые создаются — история в аудите.
        foreach (var i in v.Items.ToList()) { v.Items.Remove(i); db.VisitItems.Remove(i); }
        foreach (var ci in items)
        {
            var item = new VisitItem
            {
                VisitId = v.Id, ServiceId = ci.ServiceId, DoctorId = ci.DoctorId ?? v.DoctorId, Qty = ci.Qty, UnitPrice = ci.UnitPrice, DiscountPct = ci.DiscountPct,
                ToothNumbers = (ci.ToothNumbers ?? []).ToList(),
            };
            v.Items.Add(item);
        }
        v.Recalculate();

        // Материалы: сторно прежнего расхода и новый расход.
        if (materials is not null)
        {
            if (v.ConsumptionDocumentId is { } docId) await stock.ReverseAsync(docId, "Корректировка визита: " + reason, ct);
            foreach (var m in v.Materials.ToList()) { v.Materials.Remove(m); db.VisitMaterialUsages.Remove(m); }
            foreach (var m in materials.Where(x => x.Quantity > 0))
                v.Materials.Add(new VisitMaterialUsage { VisitId = v.Id, ItemId = m.ItemId, Quantity = m.Quantity, NormQuantity = m.Quantity });
            await db.SaveChangesAsync(ct);
            v.ConsumptionDocumentId = null;
            if (v.Materials.Count > 0)
            {
                var (newDoc, costs) = await stock.ConsumeAsync(v.Id, v.BranchId, v.PatientId, v.Materials.Select(m => (m.Id, m.ItemId, m.Quantity)).ToList(), ct);
                v.ConsumptionDocumentId = newDoc;
                foreach (var m in v.Materials) if (costs.TryGetValue(m.Id, out var c)) { m.Cost = c.Cost; m.BatchId = c.BatchId; }
            }
        }

        await AdjustBalanceAsync(v.PatientId, oldTotal - v.Total, ct);
        v.ApprovalState = VisitApprovalState.None;
        audit.Log(nameof(Visit), v.Id, "closed_visit_edit", new
        {
            before,
            after = new { v.Total, items = v.Items.Select(i => new { i.ServiceId, i.Qty, i.UnitPrice, i.DiscountPct, i.Total }).ToList(), materials = v.Materials.Select(m => new { m.ItemId, m.Quantity }).ToList() },
        }, reason, suspicious: true, branchId: v.BranchId);
    }

    // ---------- Отмена ----------
    /// <summary>Отмена открытого визита (visits.cancel) или закрытого — только владельцем, с возвратом материалов и пересчётом баланса.</summary>
    public async Task<VisitDto> CancelAsync(Guid visitId, CancelVisitRequest r, CancellationToken ct)
    {
        user.EnsurePermission(Perm.Visits.Cancel);
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var v = await db.Visits.Include(x => x.Items).Include(x => x.Materials).FirstOrDefaultAsync(x => x.Id == visitId, ct) ?? throw AppException.NotFound("Визит");
        user.EnsureBranchAccess(v.BranchId);
        if (v.Status == VisitStatus.Cancelled) return await GetAsync(v.Id, ct);
        if (v.Status == VisitStatus.Closed)
        {
            if (user.RoleCode != RolePresets.Owner) throw AppException.Forbidden("Отменить закрытый визит может только владелец");
            if (v.ConsumptionDocumentId is { } docId) await stock.ReverseAsync(docId, "Отмена визита: " + r.Reason, ct);
            await AdjustBalanceAsync(v.PatientId, v.Total, ct);
            audit.Log(nameof(Visit), v.Id, "visit_cancel_closed", new { v.Total, v.PaidTotal }, r.Reason, suspicious: true, branchId: v.BranchId);
        }
        v.Status = VisitStatus.Cancelled;
        v.CancelReason = r.Reason;
        if (v.AppointmentId is { } aid)
        {
            var a = await db.Appointments.FirstOrDefaultAsync(x => x.Id == aid, ct);
            if (a is not null && a.Status is AppointmentStatus.Arrived or AppointmentStatus.InChair) a.Status = AppointmentStatus.Confirmed;
        }
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await GetAsync(v.Id, ct);
    }

    // ---------- Общие ----------
    private async Task<Visit> LoadOpenAsync(Guid id, CancellationToken ct)
    {
        var v = await db.Visits.Include(x => x.Items).Include(x => x.Materials).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Визит");
        user.EnsureBranchAccess(v.BranchId);
        if (!user.Has(Perm.Visits.EditOpen) && !(user.Has(Perm.Visits.Complete) && v.DoctorId == user.MembershipId) && !user.Has(Perm.Visits.EditClosed))
            throw AppException.Forbidden("Нет права редактировать визит");
        v.EnsureOpen();
        return v;
    }

    /// <summary>Баланс пациента: + деньги получены, − услуги оказаны (кэш patient_balances).</summary>
    internal async Task AdjustBalanceAsync(Guid patientId, long delta, CancellationToken ct)
    {
        if (delta == 0) return;
        var b = await db.PatientBalances.FirstOrDefaultAsync(x => x.PatientId == patientId, ct);
        if (b is null) { b = new PatientBalance { PatientId = patientId }; db.PatientBalances.Add(b); }
        b.Balance += delta;
    }
}

public sealed record VisitCorrectionPayload(IReadOnlyList<VisitCorrectionItem> Items, IReadOnlyList<VisitMaterialInput>? Materials, string Reason, long OldTotal, long NewTotal, Guid RequestedBy);

/// <summary>Подтверждение скидки выше лимита: снимает флаги ожидания; отклонение — скидка обнуляется.</summary>
public sealed class DiscountApprovalHandler(IAppDbContext db) : IApprovalHandler
{
    public ApprovalType Type => ApprovalType.Discount;

    public async Task OnApprovedAsync(ApprovalRequest request, CancellationToken ct)
    {
        var v = await db.Visits.Include(x => x.Items).FirstAsync(x => x.Id == request.EntityId, ct);
        foreach (var i in v.Items) i.DiscountPendingApproval = false;
        if (v.ApprovalState == VisitApprovalState.PendingDiscount) v.ApprovalState = VisitApprovalState.None;
    }

    public async Task OnRejectedAsync(ApprovalRequest request, CancellationToken ct)
    {
        var v = await db.Visits.Include(x => x.Items).FirstAsync(x => x.Id == request.EntityId, ct);
        foreach (var i in v.Items.Where(i => i.DiscountPendingApproval))
        {
            i.DiscountPct = 0;
            i.DiscountPendingApproval = false;
        }
        v.Recalculate();
        if (v.ApprovalState == VisitApprovalState.PendingDiscount) v.ApprovalState = VisitApprovalState.None;
    }
}

public sealed class ClosedVisitEditApprovalHandler(IAppDbContext db, VisitService visits) : IApprovalHandler
{
    public ApprovalType Type => ApprovalType.ClosedVisitEdit;

    public async Task OnApprovedAsync(ApprovalRequest request, CancellationToken ct)
    {
        var payload = System.Text.Json.JsonSerializer.Deserialize<VisitCorrectionPayload>(request.Payload, ApprovalService.Json)
                      ?? throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Повреждённые данные запроса");
        var v = await db.Visits.Include(x => x.Items).Include(x => x.Materials).FirstAsync(x => x.Id == request.EntityId, ct);
        await visits.ApplyCorrectionAsync(v, payload.Items, payload.Materials, payload.Reason, ct);
    }

    public async Task OnRejectedAsync(ApprovalRequest request, CancellationToken ct)
    {
        var v = await db.Visits.FirstAsync(x => x.Id == request.EntityId, ct);
        if (v.ApprovalState == VisitApprovalState.PendingCorrection) v.ApprovalState = VisitApprovalState.None;
    }
}

public sealed class PayrollPeriodChangeApprovalHandler(IAppDbContext db, VisitService visits) : IApprovalHandler
{
    public ApprovalType Type => ApprovalType.PayrollPeriodChange;

    public async Task OnApprovedAsync(ApprovalRequest request, CancellationToken ct)
    {
        var payload = System.Text.Json.JsonSerializer.Deserialize<VisitCorrectionPayload>(request.Payload, ApprovalService.Json)
                      ?? throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Повреждённые данные запроса");
        var v = await db.Visits.Include(x => x.Items).Include(x => x.Materials).FirstAsync(x => x.Id == request.EntityId, ct);
        await visits.ApplyCorrectionAsync(v, payload.Items, payload.Materials, payload.Reason, ct);
    }

    public async Task OnRejectedAsync(ApprovalRequest request, CancellationToken ct)
    {
        var v = await db.Visits.FirstAsync(x => x.Id == request.EntityId, ct);
        if (v.ApprovalState == VisitApprovalState.PendingCorrection) v.ApprovalState = VisitApprovalState.None;
    }
}
