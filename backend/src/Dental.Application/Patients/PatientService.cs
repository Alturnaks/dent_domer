using Dental.Application.Common;
using Dental.Application.Permissions;
using Dental.Domain.Cash;
using Dental.Domain.Patients;
using Dental.Domain.Scheduling;
using Dental.Domain.Visits;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Dental.Application.Patients;

public sealed record PatientListItem(
    Guid Id, string FullName, string LastName, string FirstName, string? MiddleName, DateOnly? BirthDate, Gender Gender, string? Phone,
    string? Iin, IReadOnlyList<string> Tags, bool IsVip, Guid? SourceId, long Balance, DateTimeOffset? LastVisitAt, DateTimeOffset? NextAppointmentAt,
    DateTimeOffset CreatedAt);

public sealed record PatientDto(
    Guid Id, string LastName, string FirstName, string? MiddleName, string FullName, DateOnly? BirthDate, Gender Gender, string? Iin, string? Phone,
    string? PhoneExtra, string? Email, string? Address, Guid? SourceId, string? SourceName, string? Notes, IReadOnlyList<string> Tags, bool IsVip,
    Guid? MergedIntoId, long Balance, DateTimeOffset CreatedAt, int VisitsCount, long TotalPaid, DateTimeOffset? LastVisitAt, DateTimeOffset? NextAppointmentAt);

public sealed record PatientRequest(
    string LastName, string FirstName, string? MiddleName, DateOnly? BirthDate, Gender? Gender, string? Iin, string? Phone, string? PhoneExtra,
    string? Email, string? Address, Guid? SourceId, string? Notes, IReadOnlyList<string>? Tags, bool? IsVip, bool? IgnoreDuplicates);

public sealed record DuplicateCandidate(Guid Id, string FullName, string? Phone, string? Iin, DateOnly? BirthDate, string Reason);
public sealed record CreatePatientResult(PatientDto? Patient, IReadOnlyList<DuplicateCandidate> Duplicates);
public sealed record DuplicatePair(DuplicateCandidate A, DuplicateCandidate B, string Reason);
public sealed record MergePatientsRequest(Guid DuplicateId);

public sealed record PatientVisitItem(Guid Id, DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt, VisitStatus Status, Guid DoctorId, string DoctorName, Guid BranchId, long Total, long PaidTotal);
public sealed record PatientPaymentItem(Guid Id, DateTimeOffset CreatedAt, PaymentType Type, PaymentMethod Method, long Amount, Guid? VisitId, string? Comment);
public sealed record PatientAppointmentItem(Guid Id, DateTimeOffset StartsAt, DateTimeOffset EndsAt, AppointmentStatus Status, Guid DoctorId, string DoctorName, Guid BranchId, string? Comment);
public sealed record PatientBalanceDto(long Balance, long TotalBilled, long TotalPaid, long Debt, long Advance);

public sealed class PatientRequestValidator : AbstractValidator<PatientRequest>
{
    public PatientRequestValidator()
    {
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MiddleName).MaximumLength(100);
        RuleFor(x => x.Iin).Must(IinValidator.IsValid).When(x => !string.IsNullOrWhiteSpace(x.Iin)).WithMessage("Некорректный ИИН");
        RuleFor(x => x.Phone).Must(p => (Patient.NormalizePhone(p)?.Length ?? 0) == 11).When(x => !string.IsNullOrWhiteSpace(x.Phone)).WithMessage("Телефон в формате +7 XXX XXX-XX-XX");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.BirthDate).Must(d => d is null || (d.Value.Year > 1900 && d.Value <= DateOnly.FromDateTime(DateTime.UtcNow))).WithMessage("Некорректная дата рождения");
    }
}

/// <summary>ИИН РК: 12 цифр, контрольная сумма.</summary>
public static class IinValidator
{
    public static bool IsValid(string? iin)
    {
        if (string.IsNullOrWhiteSpace(iin) || iin.Length != 12 || !iin.All(char.IsDigit)) return false;
        var d = iin.Select(c => c - '0').ToArray();
        int[] w1 = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
        int[] w2 = [3, 4, 5, 6, 7, 8, 9, 10, 11, 1, 2];
        var sum = Enumerable.Range(0, 11).Sum(i => d[i] * w1[i]) % 11;
        if (sum == 10) sum = Enumerable.Range(0, 11).Sum(i => d[i] * w2[i]) % 11;
        return sum != 10 && sum == d[11];
    }
}

public sealed class PatientService(IAppDbContext db, ICurrentUser user, IAuditService audit)
{
    public async Task<CursorPage<PatientListItem>> ListAsync(string? q, string? tag, Guid? sourceId, bool? debtors, int? notVisitedMonths, string? cursor, int limit,
        CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 200);
        var query = db.Patients.AsNoTracking().Where(p => p.DeletedAt == null && p.MergedIntoId == null);
        if (!string.IsNullOrWhiteSpace(q)) query = ApplySearch(query, q);
        if (!string.IsNullOrWhiteSpace(tag)) query = query.Where(p => p.Tags.Contains(tag));
        if (sourceId is { } s) query = query.Where(p => p.SourceId == s);
        if (debtors == true) query = query.Where(p => db.PatientBalances.Any(b => b.PatientId == p.Id && b.Balance < 0));
        if (notVisitedMonths is { } months)
        {
            var threshold = DateTimeOffset.UtcNow.AddMonths(-months);
            query = query.Where(p => !db.Visits.Any(v => v.PatientId == p.Id && v.Status == VisitStatus.Closed && v.ClosedAt > threshold));
        }

        var c = Cursor.Decode(cursor);
        if (c is { } cc) query = query.Where(p => p.CreatedAt < cc.At || (p.CreatedAt == cc.At && p.Id.CompareTo(cc.Id) < 0));
        var rows = await query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).Take(limit + 1).ToListAsync(ct);
        var page = rows.Take(limit).ToList();
        var items = await EnrichAsync(page, ct);
        var next = rows.Count > limit ? Cursor.Encode(page[^1].CreatedAt, page[^1].Id) : null;
        return new CursorPage<PatientListItem>(items, next);
    }

    /// <summary>Поиск по ФИО (trigram/ILIKE), телефону или ИИН.</summary>
    private IQueryable<Patient> ApplySearch(IQueryable<Patient> query, string q)
    {
        var term = q.Trim();
        var digits = new string(term.Where(char.IsDigit).ToArray());
        if (digits.Length >= 4 && digits.Length == term.Replace(" ", "").Replace("+", "").Replace("-", "").Replace("(", "").Replace(")", "").Length)
        {
            var phoneDigits = digits.Length >= 10 ? Patient.NormalizePhone(digits) ?? digits : digits;
            return query.Where(p => (p.Phone != null && p.Phone.Contains(phoneDigits)) || (p.PhoneExtra != null && p.PhoneExtra.Contains(phoneDigits)) || p.Iin == digits);
        }
        var parts = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var like = $"%{part}%";
            query = query.Where(p => EF.Functions.ILike(p.LastName, like) || EF.Functions.ILike(p.FirstName, like) || (p.MiddleName != null && EF.Functions.ILike(p.MiddleName, like)));
        }
        return query;
    }

    private async Task<List<PatientListItem>> EnrichAsync(List<Patient> page, CancellationToken ct)
    {
        var ids = page.Select(p => p.Id).ToList();
        var balances = await db.PatientBalances.AsNoTracking().Where(b => ids.Contains(b.PatientId)).ToDictionaryAsync(b => b.PatientId, b => b.Balance, ct);
        var lastVisits = await db.Visits.AsNoTracking().Where(v => ids.Contains(v.PatientId) && v.Status == VisitStatus.Closed)
            .GroupBy(v => v.PatientId).Select(g => new { g.Key, Last = g.Max(v => v.ClosedAt) }).ToDictionaryAsync(x => x.Key, x => x.Last, ct);
        var now = DateTimeOffset.UtcNow;
        var next = await db.Appointments.AsNoTracking().Where(a => ids.Contains(a.PatientId) && a.StartsAt > now && (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed))
            .GroupBy(a => a.PatientId).Select(g => new { g.Key, Next = g.Min(a => a.StartsAt) }).ToDictionaryAsync(x => x.Key, x => x.Next, ct);
        var canSeeIin = user.Has(Perm.Patients.ViewMedical) || user.Has(Perm.Patients.Edit);
        return page.Select(p => new PatientListItem(p.Id, p.FullName, p.LastName, p.FirstName, p.MiddleName, p.BirthDate, p.Gender, p.Phone,
            canSeeIin ? p.Iin : Mask(p.Iin), p.Tags, p.IsVip, p.SourceId, balances.GetValueOrDefault(p.Id), lastVisits.GetValueOrDefault(p.Id),
            next.TryGetValue(p.Id, out var n) ? n : null, p.CreatedAt)).ToList();
    }

    private static string? Mask(string? s) => s is null ? null : s.Length <= 4 ? "****" : new string('*', s.Length - 4) + s[^4..];

    public async Task<PatientDto> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Patients.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct) ?? throw AppException.NotFound("Пациент");
        var source = p.SourceId is { } sid ? await db.LeadSources.AsNoTracking().Where(s => s.Id == sid).Select(s => s.Name).FirstOrDefaultAsync(ct) : null;
        var balance = await db.PatientBalances.AsNoTracking().Where(b => b.PatientId == id).Select(b => b.Balance).FirstOrDefaultAsync(ct);
        var visits = await db.Visits.AsNoTracking().Where(v => v.PatientId == id && v.Status == VisitStatus.Closed).ToListAsync(ct);
        var paid = await db.Payments.AsNoTracking().Where(x => x.PatientId == id && x.Method != PaymentMethod.Balance).SumAsync(x => x.Type == PaymentType.Refund ? -x.Amount : x.Amount, ct);
        var now = DateTimeOffset.UtcNow;
        var next = await db.Appointments.AsNoTracking().Where(a => a.PatientId == id && a.StartsAt > now && (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed))
            .OrderBy(a => a.StartsAt).Select(a => (DateTimeOffset?)a.StartsAt).FirstOrDefaultAsync(ct);
        var canSeeIin = user.Has(Perm.Patients.ViewMedical) || user.Has(Perm.Patients.Edit);
        return new PatientDto(p.Id, p.LastName, p.FirstName, p.MiddleName, p.FullName, p.BirthDate, p.Gender, canSeeIin ? p.Iin : Mask(p.Iin), p.Phone, p.PhoneExtra,
            p.Email, p.Address, p.SourceId, source, p.Notes, p.Tags, p.IsVip, p.MergedIntoId, balance, p.CreatedAt, visits.Count, paid,
            visits.Count == 0 ? null : visits.Max(v => v.ClosedAt), next);
    }

    public async Task<IReadOnlyList<DuplicateCandidate>> FindDuplicatesAsync(string? phone, string? iin, Guid? excludeId, CancellationToken ct)
    {
        var normalized = Patient.NormalizePhone(phone);
        var iinValue = iin.NullIfEmpty();
        if (normalized is null && iinValue is null) return [];
        var rows = await db.Patients.AsNoTracking()
            .Where(p => p.DeletedAt == null && p.MergedIntoId == null && p.Id != excludeId &&
                        ((normalized != null && (p.Phone == normalized || p.PhoneExtra == normalized)) || (iinValue != null && p.Iin == iinValue)))
            .Take(10).ToListAsync(ct);
        return rows.Select(p => new DuplicateCandidate(p.Id, p.FullName, p.Phone, p.Iin, p.BirthDate,
            iinValue != null && p.Iin == iinValue ? "iin" : "phone")).ToList();
    }

    /// <summary>Создание: при совпадении телефона или ИИН — предупреждение со ссылкой на существующего пациента (409), если не ignoreDuplicates.</summary>
    public async Task<CreatePatientResult> CreateAsync(PatientRequest r, CancellationToken ct)
    {
        var duplicates = await FindDuplicatesAsync(r.Phone, r.Iin, null, ct);
        if (duplicates.Any(d => d.Reason == "iin"))
            throw AppException.Conflict(ErrorCodes.IinTaken, "Пациент с таким ИИН уже есть", new Dictionary<string, object?> { ["duplicates"] = duplicates });
        if (duplicates.Count > 0 && r.IgnoreDuplicates != true)
            throw AppException.Conflict(ErrorCodes.PatientDuplicate, "Похоже, такой пациент уже есть", new Dictionary<string, object?> { ["duplicates"] = duplicates });

        var p = new Patient();
        Apply(p, r);
        db.Patients.Add(p);
        db.PatientBalances.Add(new PatientBalance { PatientId = p.Id, Balance = 0 });
        await db.SaveChangesAsync(ct);
        return new CreatePatientResult(await GetAsync(p.Id, ct), duplicates);
    }

    public async Task<PatientDto> UpdateAsync(Guid id, PatientRequest r, CancellationToken ct)
    {
        var p = await db.Patients.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct) ?? throw AppException.NotFound("Пациент");
        if (!string.IsNullOrWhiteSpace(r.Iin) && r.Iin != p.Iin && await db.Patients.AnyAsync(x => x.Iin == r.Iin && x.Id != id && x.DeletedAt == null && x.MergedIntoId == null, ct))
            throw AppException.Conflict(ErrorCodes.IinTaken, "Пациент с таким ИИН уже есть");
        // ИИН скрыт от ролей без права — не затираем его маской при сохранении.
        var keepIin = r.Iin is not null && r.Iin.Contains('*', StringComparison.Ordinal);
        var oldIin = p.Iin;
        Apply(p, r);
        if (keepIin) p.Iin = oldIin;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private static void Apply(Patient p, PatientRequest r)
    {
        p.LastName = r.LastName.Trim();
        p.FirstName = r.FirstName.Trim();
        p.MiddleName = r.MiddleName.NullIfEmpty();
        p.BirthDate = r.BirthDate;
        p.Gender = r.Gender ?? Gender.Unknown;
        p.Iin = r.Iin.NullIfEmpty();
        p.Phone = Patient.NormalizePhone(r.Phone);
        p.PhoneExtra = Patient.NormalizePhone(r.PhoneExtra);
        p.Email = r.Email.NullIfEmpty();
        p.Address = r.Address.NullIfEmpty();
        p.SourceId = r.SourceId;
        p.Notes = r.Notes.NullIfEmpty();
        p.Tags = (r.Tags ?? []).Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().ToList();
        p.IsVip = r.IsVip ?? false;
    }

    private sealed record DupRow(Guid Id, string LastName, string FirstName, string? MiddleName, string? Phone, string? Iin, DateOnly? BirthDate)
    {
        public DuplicateCandidate ToCandidate(string reason) =>
            new(Id, string.Join(' ', new[] { LastName, FirstName, MiddleName }.Where(x => !string.IsNullOrWhiteSpace(x))), Phone, Iin, BirthDate, reason);
    }

    /// <summary>Кандидаты на слияние: одинаковый телефон или ИИН, либо одинаковые ФИО и дата рождения.</summary>
    public async Task<IReadOnlyList<DuplicatePair>> DuplicatesAsync(CancellationToken ct)
    {
        var patients = await db.Patients.AsNoTracking().Where(p => p.DeletedAt == null && p.MergedIntoId == null)
            .Select(p => new DupRow(p.Id, p.LastName, p.FirstName, p.MiddleName, p.Phone, p.Iin, p.BirthDate)).ToListAsync(ct);
        var result = new List<DuplicatePair>();
        var seen = new HashSet<(Guid, Guid)>();
        void Add(IEnumerable<IGrouping<string, DupRow>> groups, string reason)
        {
            foreach (var g in groups)
            {
                var list = g.ToList();
                for (var i = 1; i < list.Count; i++)
                {
                    var (a, b) = (list[0].Id, list[i].Id);
                    if (!seen.Add(a.CompareTo(b) < 0 ? (a, b) : (b, a))) continue;
                    result.Add(new DuplicatePair(list[0].ToCandidate(reason), list[i].ToCandidate(reason), reason));
                }
            }
        }
        Add(patients.Where(p => p.Phone != null).GroupBy(p => p.Phone!).Where(g => g.Count() > 1), "phone");
        Add(patients.Where(p => p.Iin != null).GroupBy(p => p.Iin!).Where(g => g.Count() > 1), "iin");
        Add(patients.Where(p => p.BirthDate != null)
            .GroupBy(p => $"{p.LastName.ToUpperInvariant()}|{p.FirstName.ToUpperInvariant()}|{p.BirthDate}").Where(g => g.Count() > 1), "name_birthdate");
        return result.Take(200).ToList();
    }

    /// <summary>Слияние: записи, визиты, платежи, баланс переносятся на основную карточку; дубль получает merged_into_id.</summary>
    public async Task<PatientDto> MergeAsync(Guid mainId, Guid duplicateId, CancellationToken ct)
    {
        if (mainId == duplicateId) throw AppException.BadRequest(ErrorCodes.PatientMergeInvalid, "Нельзя объединить карточку саму с собой");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var main = await db.Patients.FirstOrDefaultAsync(p => p.Id == mainId && p.DeletedAt == null && p.MergedIntoId == null, ct) ?? throw AppException.NotFound("Пациент");
        var dup = await db.Patients.FirstOrDefaultAsync(p => p.Id == duplicateId && p.DeletedAt == null && p.MergedIntoId == null, ct) ?? throw AppException.NotFound("Дубль");

        var moved = new Dictionary<string, int>
        {
            ["appointments"] = await db.Appointments.Where(x => x.PatientId == dup.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, main.Id), ct),
            ["visits"] = await db.Visits.Where(x => x.PatientId == dup.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, main.Id), ct),
            ["payments"] = await db.Payments.Where(x => x.PatientId == dup.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, main.Id), ct),
            ["waitlist"] = await db.Waitlist.Where(x => x.PatientId == dup.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, main.Id), ct),
            ["consents"] = await db.PatientConsents.Where(x => x.PatientId == dup.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, main.Id), ct),
            ["messages"] = await db.OutgoingMessages.Where(x => x.PatientId == dup.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, main.Id), ct),
            ["movements"] = await db.StockMovements.Where(x => x.PatientId == dup.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, main.Id), ct),
        };

        var mainBalance = await db.PatientBalances.FirstOrDefaultAsync(b => b.PatientId == main.Id, ct);
        var dupBalance = await db.PatientBalances.FirstOrDefaultAsync(b => b.PatientId == dup.Id, ct);
        if (mainBalance is null) { mainBalance = new PatientBalance { PatientId = main.Id }; db.PatientBalances.Add(mainBalance); }
        if (dupBalance is not null)
        {
            mainBalance.Balance += dupBalance.Balance;
            dupBalance.Balance = 0;
        }

        // Дополняем пустые поля основной карточки данными дубля.
        main.Phone ??= dup.Phone;
        if (main.PhoneExtra is null && dup.Phone != main.Phone) main.PhoneExtra = dup.Phone;
        main.Iin ??= dup.Iin;
        main.BirthDate ??= dup.BirthDate;
        main.Email ??= dup.Email;
        main.Address ??= dup.Address;
        main.SourceId ??= dup.SourceId;
        main.Tags = main.Tags.Union(dup.Tags).ToList();
        if (!string.IsNullOrWhiteSpace(dup.Notes)) main.Notes = string.Join("\n", new[] { main.Notes, dup.Notes }.Where(s => !string.IsNullOrWhiteSpace(s)));
        dup.MergedIntoId = main.Id;
        var dupIin = dup.Iin;
        dup.Iin = null;
        if (main.Iin is null) main.Iin = dupIin;

        audit.Log(nameof(Patient), main.Id, "merge", new { duplicateId = dup.Id, duplicateName = dup.FullName, moved });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(main.Id, ct);
    }

    public async Task<IReadOnlyList<PatientVisitItem>> VisitsAsync(Guid id, CancellationToken ct) =>
        await (from v in db.Visits.AsNoTracking()
               join m in db.Memberships.AsNoTracking() on v.DoctorId equals m.Id
               join u in db.Users.AsNoTracking() on m.UserId equals u.Id
               where v.PatientId == id
               orderby v.OpenedAt descending
               select new PatientVisitItem(v.Id, v.OpenedAt, v.ClosedAt, v.Status, v.DoctorId, u.FullName, v.BranchId, v.Total, v.PaidTotal)).ToListAsync(ct);

    public async Task<IReadOnlyList<PatientPaymentItem>> PaymentsAsync(Guid id, CancellationToken ct) =>
        await db.Payments.AsNoTracking().Where(p => p.PatientId == id).OrderByDescending(p => p.CreatedAt)
            .Select(p => new PatientPaymentItem(p.Id, p.CreatedAt, p.Type, p.Method, p.Amount, p.VisitId, p.Comment)).ToListAsync(ct);

    public async Task<IReadOnlyList<PatientAppointmentItem>> AppointmentsAsync(Guid id, CancellationToken ct) =>
        await (from a in db.Appointments.AsNoTracking()
               join m in db.Memberships.AsNoTracking() on a.DoctorId equals m.Id
               join u in db.Users.AsNoTracking() on m.UserId equals u.Id
               where a.PatientId == id
               orderby a.StartsAt descending
               select new PatientAppointmentItem(a.Id, a.StartsAt, a.EndsAt, a.Status, a.DoctorId, u.FullName, a.BranchId, a.Comment)).ToListAsync(ct);

    public async Task<PatientBalanceDto> BalanceAsync(Guid id, CancellationToken ct)
    {
        var balance = await db.PatientBalances.AsNoTracking().Where(b => b.PatientId == id).Select(b => b.Balance).FirstOrDefaultAsync(ct);
        var billed = await db.Visits.AsNoTracking().Where(v => v.PatientId == id && v.Status == VisitStatus.Closed).SumAsync(v => v.Total, ct);
        var paid = await db.Payments.AsNoTracking().Where(p => p.PatientId == id && p.Method != PaymentMethod.Balance).SumAsync(p => p.Type == PaymentType.Refund ? -p.Amount : p.Amount, ct);
        return new PatientBalanceDto(balance, billed, paid, Math.Max(0, -balance), Math.Max(0, balance));
    }
}
