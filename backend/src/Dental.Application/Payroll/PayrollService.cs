using System.Text.Json;
using Dental.Application.Common;
using Dental.Application.Permissions;
using Dental.Application.Schedule;
using Dental.Domain.Common;
using Dental.Domain.Organizations;
using Dental.Domain.Payroll;
using Dental.Domain.Visits;
using Microsoft.EntityFrameworkCore;

namespace Dental.Application.Payroll;

/// <summary>
/// Зарплата врачей (SPEC §6.8, §8.7): схемы с историей, ведомости по филиалу за период, расчёт по закрытым визитам,
/// бонусы/штрафы, утверждение и выплата, «моя зарплата» для врача.
/// </summary>
public sealed class PayrollService(IAppDbContext db, ICurrentUser user, IAuditService audit, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------- Схемы ----------
    public async Task<IReadOnlyList<PayrollSchemeDto>> ListSchemesAsync(Guid? membershipId, CancellationToken ct)
    {
        EnsureCanViewAll();
        var q = db.PayrollSchemes.AsNoTracking();
        if (membershipId is { } mid) q = q.Where(s => s.MembershipId == mid);
        var rows = await q.OrderBy(s => s.MembershipId).ThenByDescending(s => s.ValidFrom).ThenByDescending(s => s.CreatedAt).ToListAsync(ct);
        var names = await DoctorNamesAsync(rows.Select(r => r.MembershipId), ct);
        var today = await TodayAsync(ct);
        var current = rows.Where(r => r.ValidFrom <= today).GroupBy(r => r.MembershipId).Select(g => g.First().Id).ToHashSet();
        return rows.Select(r => ToDto(r, names, current.Contains(r.Id))).ToList();
    }

    /// <summary>
    /// Новая версия схемы. Старые версии не меняются (история); если версия с той же датой начала уже есть — она уточняется.
    /// </summary>
    public async Task<PayrollSchemeDto> SaveSchemeAsync(PayrollSchemeRequest r, CancellationToken ct)
    {
        user.EnsurePermission(Perm.Payroll.Manage);
        var m = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.MembershipId, ct) ?? throw AppException.NotFound("Сотрудник");
        var s = await db.PayrollSchemes.FirstOrDefaultAsync(x => x.MembershipId == m.Id && x.ValidFrom == r.ValidFrom, ct);
        if (s is null)
        {
            s = new PayrollScheme { MembershipId = m.Id, ValidFrom = r.ValidFrom };
            db.PayrollSchemes.Add(s);
        }
        s.Type = r.Type;
        s.Percent = r.Type == PayrollSchemeType.PerShift ? 0 : r.Percent ?? 0;
        s.FixedAmount = r.Type == PayrollSchemeType.FixedPlusPercent ? r.FixedAmount ?? 0 : 0;
        s.ShiftRate = r.Type == PayrollSchemeType.PerShift ? r.ShiftRate ?? 0 : 0;
        await db.SaveChangesAsync(ct);
        var names = await DoctorNamesAsync([m.Id], ct);
        var today = await TodayAsync(ct);
        var currentId = await db.PayrollSchemes.AsNoTracking().Where(x => x.MembershipId == m.Id && x.ValidFrom <= today)
            .OrderByDescending(x => x.ValidFrom).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        return ToDto(s, names, currentId == s.Id);
    }

    // ---------- Ведомости ----------
    public async Task<IReadOnlyList<PayrollPeriodDto>> ListPeriodsAsync(Guid? branchId, PayrollPeriodStatus? status, CancellationToken ct)
    {
        EnsureCanViewAll();
        var q = db.PayrollPeriods.AsNoTracking();
        if (branchId is { } bid) { user.EnsureBranchAccess(bid); q = q.Where(p => p.BranchId == bid); }
        else if (!user.AllBranches) { var ids = user.BranchIds.ToList(); q = q.Where(p => ids.Contains(p.BranchId)); }
        if (status is { } st) q = q.Where(p => p.Status == st);
        var periods = await q.OrderByDescending(p => p.PeriodStart).ThenByDescending(p => p.CreatedAt).Take(200).ToListAsync(ct);
        return await MapPeriodsAsync(periods, ct);
    }

    public async Task<PayrollPeriodDetailDto> GetPeriodAsync(Guid id, CancellationToken ct)
    {
        EnsureCanViewAll();
        var p = await db.PayrollPeriods.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Ведомость");
        user.EnsureBranchAccess(p.BranchId);
        var entries = await db.PayrollEntries.AsNoTracking().Where(e => e.PeriodId == id).ToListAsync(ct);
        var names = await DoctorNamesAsync(entries.Select(e => e.MembershipId), ct);
        var org = await db.Organizations.AsNoTracking().FirstAsync(ct);
        return new PayrollPeriodDetailDto((await MapPeriodsAsync([p], ct))[0],
            entries.Select(e => ToEntryDto(e, names)).OrderBy(e => e.DoctorName, StringComparer.CurrentCulture).ToList(), org.Settings.PayrollOnlyPaidVisits);
    }

    public async Task<PayrollEntryDetailDto> GetEntryAsync(Guid entryId, CancellationToken ct)
    {
        var e = await db.PayrollEntries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == entryId, ct) ?? throw AppException.NotFound("Начисление");
        var p = await db.PayrollPeriods.AsNoTracking().FirstAsync(x => x.Id == e.PeriodId, ct);
        var own = e.MembershipId == user.MembershipId && user.Has(Perm.Payroll.ViewOwn) && p.Status != PayrollPeriodStatus.Draft;
        if (!own)
        {
            EnsureCanViewAll();
            user.EnsureBranchAccess(p.BranchId);
        }
        var names = await DoctorNamesAsync([e.MembershipId], ct);
        var d = ParseDetails(e.Details);
        return new PayrollEntryDetailDto(ToEntryDto(e, names), d.WorkedDays, d.Visits);
    }

    /// <summary>Создание ведомости (draft) и сразу расчёт. Пересекающиеся ведомости одного филиала запрещены.</summary>
    public async Task<PayrollPeriodDetailDto> CreatePeriodAsync(CreatePayrollPeriodRequest r, CancellationToken ct)
    {
        user.EnsurePermission(Perm.Payroll.Manage);
        user.EnsureBranchAccess(r.BranchId);
        if (r.PeriodEnd < r.PeriodStart) throw AppException.BadRequest(ErrorCodes.InvalidTimeRange, "Конец периода раньше начала");
        _ = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == r.BranchId && b.DeletedAt == null, ct) ?? throw AppException.NotFound("Филиал");
        var overlap = await db.PayrollPeriods.AsNoTracking()
            .FirstOrDefaultAsync(p => p.BranchId == r.BranchId && p.PeriodStart <= r.PeriodEnd && p.PeriodEnd >= r.PeriodStart, ct);
        if (overlap is not null)
        {
            throw AppException.Conflict(ErrorCodes.PayrollPeriodOverlap,
                $"Период пересекается с ведомостью {overlap.PeriodStart:dd.MM.yyyy}–{overlap.PeriodEnd:dd.MM.yyyy}",
                new Dictionary<string, object?> { ["periodId"] = overlap.Id });
        }
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var p = new PayrollPeriod { BranchId = r.BranchId, PeriodStart = r.PeriodStart, PeriodEnd = r.PeriodEnd };
        db.PayrollPeriods.Add(p);
        await CalculateEntriesAsync(p, ct);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await GetPeriodAsync(p.Id, ct);
    }

    /// <summary>Пересчёт черновика: начисления обновляются по текущим данным, бонусы/штрафы сохраняются.</summary>
    public async Task<PayrollPeriodDetailDto> RecalculateAsync(Guid id, CancellationToken ct)
    {
        user.EnsurePermission(Perm.Payroll.Manage);
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var p = await LoadDraftAsync(id, ct);
        await CalculateEntriesAsync(p, ct);
        p.UpdatedAt = clock.GetUtcNow(); // отметка пересчёта
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await GetPeriodAsync(p.Id, ct);
    }

    public async Task<PayrollPeriodDetailDto> ApproveAsync(Guid id, CancellationToken ct)
    {
        user.EnsurePermission(Perm.Payroll.Manage);
        var p = await LoadDraftAsync(id, ct);
        p.Status = PayrollPeriodStatus.Approved;
        p.ApprovedAt = clock.GetUtcNow();
        p.ApprovedBy = user.UserId;
        await db.SaveChangesAsync(ct);
        return await GetPeriodAsync(p.Id, ct);
    }

    public async Task<PayrollPeriodDetailDto> MarkPaidAsync(Guid id, CancellationToken ct)
    {
        user.EnsurePermission(Perm.Payroll.Manage);
        var p = await db.PayrollPeriods.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Ведомость");
        user.EnsureBranchAccess(p.BranchId);
        if (p.Status != PayrollPeriodStatus.Approved)
            throw AppException.Conflict(ErrorCodes.InvalidStatusTransition, "Отметить выплату можно только для утверждённой ведомости");
        p.Status = PayrollPeriodStatus.Paid;
        await db.SaveChangesAsync(ct);
        return await GetPeriodAsync(p.Id, ct);
    }

    /// <summary>Бонус / штраф с комментарием (только черновик). Пишется в аудит с причиной.</summary>
    public async Task<PayrollEntryDto> UpdateEntryAsync(Guid entryId, UpdatePayrollEntryRequest r, CancellationToken ct)
    {
        user.EnsurePermission(Perm.Payroll.Manage);
        var e = await db.PayrollEntries.FirstOrDefaultAsync(x => x.Id == entryId, ct) ?? throw AppException.NotFound("Начисление");
        var p = await LoadDraftAsync(e.PeriodId, ct);
        var bonus = r.Bonus ?? e.Bonus;
        var penalty = r.Penalty ?? e.Penalty;
        var comment = r.Comment.NullIfEmpty() ?? e.Comment;
        if ((bonus != e.Bonus || penalty != e.Penalty) && (bonus != 0 || penalty != 0) && string.IsNullOrWhiteSpace(r.Comment))
            throw AppException.BadRequest(ErrorCodes.CommentRequired, "Укажите комментарий к бонусу или штрафу");
        var before = new { e.Bonus, e.Penalty, e.Total };
        e.Bonus = bonus;
        e.Penalty = penalty;
        e.Comment = comment;
        e.RecalculateTotal();
        audit.Log(nameof(PayrollEntry), e.Id, "payroll_adjust", new { before, after = new { e.Bonus, e.Penalty, e.Total }, e.MembershipId, periodId = p.Id },
            comment, branchId: p.BranchId);
        await db.SaveChangesAsync(ct);
        var names = await DoctorNamesAsync([e.MembershipId], ct);
        return ToEntryDto(e, names);
    }

    // ---------- Моя зарплата ----------
    public async Task<MyPayrollDto> MyAsync(CancellationToken ct)
    {
        user.EnsurePermission(Perm.Payroll.ViewOwn);
        var mid = user.MembershipId;
        var (tz, org) = await OrgTimeAsync(ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), tz).DateTime);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var calc = (await ComputeAsync(null, monthStart, monthEnd, [mid], ct)).GetValueOrDefault(mid) ?? Empty(null);
        var current = new PayrollAccrualDto(monthStart, monthEnd, calc.Scheme, calc.Visits.Count, calc.Shifts, calc.Revenue, calc.Materials, calc.Accrued, calc.Visits);

        var entries = await (from e in db.PayrollEntries.AsNoTracking()
                             join p in db.PayrollPeriods.AsNoTracking() on e.PeriodId equals p.Id
                             where e.MembershipId == mid && p.Status != PayrollPeriodStatus.Draft
                             orderby p.PeriodStart descending
                             select new { e, p }).Take(36).ToListAsync(ct);
        var branchIds = entries.Select(x => x.p.BranchId).Distinct().ToList();
        var branches = await db.Branches.AsNoTracking().Where(b => branchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var periods = entries.Select(x =>
        {
            var d = ParseDetails(x.e.Details);
            return new MyPayrollEntryDto(x.e.Id, x.p.Id, branches.GetValueOrDefault(x.p.BranchId) ?? "?", x.p.PeriodStart, x.p.PeriodEnd, x.p.Status, d.Scheme,
                d.Visits.Count, d.Shifts, x.e.BaseRevenue, x.e.MaterialsCost, x.e.Accrued, x.e.Bonus, x.e.Penalty, x.e.Total, x.e.Comment, d.Visits);
        }).ToList();
        var names = await DoctorNamesAsync([mid], ct);
        return new MyPayrollDto(mid, names.GetValueOrDefault(mid) ?? "?", org.Settings.PayrollOnlyPaidVisits, current, periods);
    }

    // ---------- Расчёт ----------
    private async Task CalculateEntriesAsync(PayrollPeriod p, CancellationToken ct)
    {
        // Врачи филиала (активные на момент расчёта) + все, у кого есть услуги в визитах периода.
        var doctors = await db.Memberships.AsNoTracking().Where(m => m.Position == StaffPosition.Doctor).ToListAsync(ct);
        var periodStartUtc = DayStartUtc(p.PeriodStart, await TzAsync(ct));
        var ids = doctors.Where(m => m.HasBranch(p.BranchId) && (m.FiredAt == null || m.FiredAt >= periodStartUtc))
            .Select(m => m.Id).ToHashSet();
        var computed = await ComputeAsync(p.BranchId, p.PeriodStart, p.PeriodEnd, null, ct);
        ids.UnionWith(computed.Keys);

        var existing = await db.PayrollEntries.Where(e => e.PeriodId == p.Id).ToListAsync(ct);
        foreach (var mid in ids)
        {
            var c = computed.GetValueOrDefault(mid) ?? Empty(await SchemeAtAsync(mid, p.PeriodEnd, ct));
            var e = existing.FirstOrDefault(x => x.MembershipId == mid);
            if (e is null)
            {
                e = new PayrollEntry { PeriodId = p.Id, MembershipId = mid };
                db.PayrollEntries.Add(e);
            }
            e.BaseRevenue = c.Revenue;
            e.MaterialsCost = c.Materials;
            e.LabCost = 0;
            e.Accrued = c.Accrued;
            e.Details = JsonSerializer.Serialize(new PayrollEntryDetails(c.Scheme, c.Shifts, c.WorkedDays, c.Visits), Json);
            e.RecalculateTotal();
        }
        // Врачи, выпавшие из расчёта (например, визиты отменены), остаются с нулями — строки не удаляются.
        foreach (var e in existing.Where(x => !ids.Contains(x.MembershipId)))
        {
            e.BaseRevenue = 0; e.MaterialsCost = 0; e.Accrued = 0; e.Details = JsonSerializer.Serialize(new PayrollEntryDetails(null, 0, [], []), Json);
            e.RecalculateTotal();
        }
    }

    private sealed record Computed(PayrollSchemeSnapshot? Scheme, long Revenue, long Materials, int Shifts, IReadOnlyList<DateOnly> WorkedDays, long Accrued,
        IReadOnlyList<PayrollVisitLine> Visits);

    private static Computed Empty(PayrollSchemeSnapshot? scheme) =>
        new(scheme, 0, 0, 0, [], scheme?.Type == PayrollSchemeType.FixedPlusPercent ? scheme.FixedAmount : 0, []);

    /// <summary>
    /// Расчёт по закрытым визитам: визит попадает в период по дате закрытия (часовой пояс организации).
    /// Выручка врача — сумма visit_items.total его позиций; материалы — себестоимость visit_material_usage его позиций
    /// (материалы без привязки к позиции — врачу визита). Смены — дни с закрытыми визитами врача.
    /// Схема — действующая на конец периода.
    /// </summary>
    private async Task<Dictionary<Guid, Computed>> ComputeAsync(Guid? branchId, DateOnly start, DateOnly end, IReadOnlyCollection<Guid>? onlyMembers, CancellationToken ct)
    {
        var (tz, org) = await OrgTimeAsync(ct);
        var fromUtc = DayStartUtc(start, tz);
        var toUtc = DayStartUtc(end.AddDays(1), tz);
        var onlyPaid = org.Settings.PayrollOnlyPaidVisits;

        var q = db.Visits.AsNoTracking().Include(v => v.Items).Include(v => v.Materials)
            .Where(v => v.Status == VisitStatus.Closed && v.ClosedAt >= fromUtc && v.ClosedAt < toUtc);
        if (branchId is { } bid) q = q.Where(v => v.BranchId == bid);
        if (onlyMembers is not null)
        {
            var members = onlyMembers.ToList();
            q = q.Where(v => members.Contains(v.DoctorId) || v.Items.Any(i => members.Contains(i.DoctorId)));
        }
        var visits = await q.AsSplitQuery().ToListAsync(ct);
        if (onlyPaid) visits = visits.Where(v => v.PaidTotal >= v.Total).ToList();

        var patientIds = visits.Select(v => v.PatientId).Distinct().ToList();
        var patients = (await db.Patients.AsNoTracking().Where(x => patientIds.Contains(x.Id)).ToListAsync(ct)).ToDictionary(x => x.Id, x => x.FullName);
        var serviceIds = visits.SelectMany(v => v.Items).Select(i => i.ServiceId).Distinct().ToList();
        var services = await db.Services.AsNoTracking().Where(s => serviceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var perDoctor = new Dictionary<Guid, List<PayrollVisitLine>>();
        foreach (var v in visits.OrderBy(v => v.ClosedAt))
        {
            var itemDoctor = v.Items.ToDictionary(i => i.Id, i => i.DoctorId);
            var doctorsInVisit = v.Items.Select(i => i.DoctorId).Append(v.DoctorId).Distinct();
            foreach (var d in doctorsInVisit)
            {
                if (onlyMembers is not null && !onlyMembers.Contains(d)) continue;
                var items = v.Items.Where(i => i.DoctorId == d).ToList();
                var revenue = items.Sum(i => i.Total);
                var materials = v.Materials.Where(m => m.VisitItemId is { } iid && itemDoctor.TryGetValue(iid, out var md) ? md == d : d == v.DoctorId).Sum(m => m.Cost);
                if (items.Count == 0 && materials == 0) continue;
                var local = TimeZoneInfo.ConvertTime(v.ClosedAt!.Value, tz);
                var line = new PayrollVisitLine(v.Id, v.ClosedAt.Value, DateOnly.FromDateTime(local.DateTime), v.PatientId, patients.GetValueOrDefault(v.PatientId) ?? "?",
                    string.Join(", ", items.Select(i => (services.GetValueOrDefault(i.ServiceId) ?? "?") + (i.Qty > 1 ? $" ×{i.Qty}" : ""))),
                    revenue, materials, v.PaidTotal >= v.Total);
                if (!perDoctor.TryGetValue(d, out var list)) perDoctor[d] = list = [];
                list.Add(line);
            }
        }

        var result = new Dictionary<Guid, Computed>();
        foreach (var (mid, lines) in perDoctor)
        {
            var scheme = await SchemeAtAsync(mid, end, ct);
            var revenue = lines.Sum(l => l.Revenue);
            var materials = lines.Sum(l => l.MaterialsCost);
            var days = lines.Select(l => l.Date).Distinct().Order().ToList();
            var accrued = scheme is null ? 0 : PayrollCalculator.Accrue(
                new PayrollScheme { Type = scheme.Type, Percent = scheme.Percent, FixedAmount = scheme.FixedAmount, ShiftRate = scheme.ShiftRate },
                new PayrollInput(revenue, materials, days.Count));
            result[mid] = new Computed(scheme, revenue, materials, days.Count, days, accrued, lines);
        }
        if (onlyMembers is not null)
        {
            foreach (var mid in onlyMembers.Where(m => !result.ContainsKey(m)))
                result[mid] = Empty(await SchemeAtAsync(mid, end, ct));
        }
        return result;
    }

    private async Task<PayrollSchemeSnapshot?> SchemeAtAsync(Guid membershipId, DateOnly date, CancellationToken ct)
    {
        var s = await db.PayrollSchemes.AsNoTracking().Where(x => x.MembershipId == membershipId && x.ValidFrom <= date)
            .OrderByDescending(x => x.ValidFrom).ThenByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        return s is null ? null : new PayrollSchemeSnapshot(s.Type, s.Percent, s.FixedAmount, s.ShiftRate, s.ValidFrom);
    }

    // ---------- Общие ----------
    private void EnsureCanViewAll()
    {
        if (!user.Has(Perm.Payroll.ViewAll) && !user.Has(Perm.Payroll.Manage)) throw AppException.Forbidden();
    }

    private async Task<PayrollPeriod> LoadDraftAsync(Guid id, CancellationToken ct)
    {
        var p = await db.PayrollPeriods.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Ведомость");
        user.EnsureBranchAccess(p.BranchId);
        if (p.Status != PayrollPeriodStatus.Draft)
            throw AppException.Conflict(ErrorCodes.PayrollPeriodLocked, "Ведомость утверждена — изменения невозможны");
        return p;
    }

    private async Task<(TimeZoneInfo Tz, Organization Org)> OrgTimeAsync(CancellationToken ct)
    {
        var org = await db.Organizations.AsNoTracking().FirstAsync(ct);
        return (ScheduleService.FindTz(org.Timezone), org);
    }

    private async Task<TimeZoneInfo> TzAsync(CancellationToken ct) => (await OrgTimeAsync(ct)).Tz;

    private async Task<DateOnly> TodayAsync(CancellationToken ct) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), await TzAsync(ct)).DateTime);

    /// <summary>Начало локального дня организации в UTC.</summary>
    public static DateTimeOffset DayStartUtc(DateOnly date, TimeZoneInfo tz)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, tz.GetUtcOffset(local)).ToUniversalTime();
    }

    private async Task<Dictionary<Guid, string>> DoctorNamesAsync(IEnumerable<Guid> membershipIds, CancellationToken ct)
    {
        var ids = membershipIds.Distinct().ToList();
        return await (from m in db.Memberships.AsNoTracking()
                      join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                      where ids.Contains(m.Id)
                      select new { m.Id, u.FullName }).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
    }

    private async Task<IReadOnlyList<PayrollPeriodDto>> MapPeriodsAsync(IReadOnlyList<PayrollPeriod> periods, CancellationToken ct)
    {
        var ids = periods.Select(p => p.Id).ToList();
        var sums = await db.PayrollEntries.AsNoTracking().Where(e => ids.Contains(e.PeriodId)).GroupBy(e => e.PeriodId)
            .Select(g => new { g.Key, Count = g.Count(), Accrued = g.Sum(e => e.Accrued), Bonus = g.Sum(e => e.Bonus), Penalty = g.Sum(e => e.Penalty), Total = g.Sum(e => e.Total) })
            .ToDictionaryAsync(x => x.Key, ct);
        var branchIds = periods.Select(p => p.BranchId).Distinct().ToList();
        var branches = await db.Branches.AsNoTracking().Where(b => branchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var approverIds = periods.Where(p => p.ApprovedBy != null).Select(p => p.ApprovedBy!.Value).Distinct().ToList();
        var approvers = await db.Users.AsNoTracking().Where(u => approverIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return periods.Select(p =>
        {
            var s = sums.GetValueOrDefault(p.Id);
            return new PayrollPeriodDto(p.Id, p.BranchId, branches.GetValueOrDefault(p.BranchId) ?? "?", p.PeriodStart, p.PeriodEnd, p.Status, p.ApprovedAt,
                p.ApprovedBy is { } a ? approvers.GetValueOrDefault(a) : null, s?.Count ?? 0, s?.Accrued ?? 0, s?.Bonus ?? 0, s?.Penalty ?? 0, s?.Total ?? 0,
                p.CreatedAt, p.UpdatedAt);
        }).ToList();
    }

    private static PayrollSchemeDto ToDto(PayrollScheme s, Dictionary<Guid, string> names, bool current) =>
        new(s.Id, s.MembershipId, names.GetValueOrDefault(s.MembershipId) ?? "?", s.Type, s.Percent, s.FixedAmount, s.ShiftRate, s.ValidFrom, current, s.CreatedAt);

    private static PayrollEntryDto ToEntryDto(PayrollEntry e, Dictionary<Guid, string> names)
    {
        var d = ParseDetails(e.Details);
        return new PayrollEntryDto(e.Id, e.PeriodId, e.MembershipId, names.GetValueOrDefault(e.MembershipId) ?? "?", d.Scheme, d.Visits.Count, d.Shifts,
            e.BaseRevenue, e.MaterialsCost, e.LabCost, e.Accrued, e.Bonus, e.Penalty, e.Total, e.Comment);
    }

    private static PayrollEntryDetails ParseDetails(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.TrimStart().StartsWith('[')) return new PayrollEntryDetails(null, 0, [], []);
        try
        {
            return JsonSerializer.Deserialize<PayrollEntryDetails>(json, Json) ?? new PayrollEntryDetails(null, 0, [], []);
        }
        catch (JsonException)
        {
            return new PayrollEntryDetails(null, 0, [], []);
        }
    }
}
