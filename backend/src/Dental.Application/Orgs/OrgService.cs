using Dental.Application.Auth;
using Dental.Application.Common;
using Dental.Application.Permissions;
using Dental.Domain.Cash;
using Dental.Domain.Inventory;
using Dental.Domain.Organizations;
using Dental.Domain.Patients;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Dental.Application.Orgs;

// ---------- DTO ----------
public sealed record OrgDto(Guid Id, string Name, string Slug, string Timezone, string Currency, string? LogoUrl, OrganizationSettings Settings);
public sealed record UpdateOrgRequest(string? Name, string? Timezone, string? LogoUrl, OrganizationSettings? Settings);

public sealed record BranchDto(Guid Id, string Name, string? Address, string? Phone, bool IsActive, WorkingHours WorkingHours);
public sealed record BranchRequest(string Name, string? Address, string? Phone, bool? IsActive, WorkingHours? WorkingHours);

public sealed record RoomDto(Guid Id, Guid BranchId, string Name);
public sealed record RoomRequest(string Name);
public sealed record ChairDto(Guid Id, Guid BranchId, Guid? RoomId, string Name, bool IsActive);
public sealed record ChairRequest(string Name, Guid? RoomId, bool? IsActive);

public sealed record StaffDto(
    Guid MembershipId, Guid UserId, string FullName, string? Email, string? Phone, Guid RoleId, string RoleName, string RoleCode,
    StaffPosition Position, string? Specialty, string? Color, bool AllBranches, IReadOnlyList<Guid> BranchIds, bool IsActive, DateTimeOffset? FiredAt);
public sealed record CreateStaffRequest(
    string FullName, string? Email, string? Phone, string Password, Guid RoleId, StaffPosition Position, string? Specialty, string? Color,
    bool AllBranches, IReadOnlyList<Guid>? BranchIds);
public sealed record UpdateStaffRequest(
    string? FullName, string? Email, string? Phone, string? Password, Guid? RoleId, StaffPosition? Position, string? Specialty, string? Color,
    bool? AllBranches, IReadOnlyList<Guid>? BranchIds);

public sealed record RoleDto(Guid Id, string Name, string Code, bool IsPreset, IReadOnlyList<string> Permissions, RoleLimits Limits, int MembersCount);
public sealed record RoleRequest(string Name, IReadOnlyList<string> Permissions, RoleLimits Limits);
public sealed record PermissionGroupDto(string Group, IReadOnlyList<string> Codes);

public sealed record NamedRefDto(Guid Id, string Name, string? Type);
public sealed record NamedRefRequest(string Name, string? Type);

public sealed record CashRegisterDto(Guid Id, Guid BranchId, string Name);
public sealed record CashRegisterRequest(Guid BranchId, string Name);

// ---------- Validators ----------
public sealed class BranchRequestValidator : AbstractValidator<BranchRequest>
{
    public BranchRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).MaximumLength(40);
    }
}

public sealed class CreateStaffRequestValidator : AbstractValidator<CreateStaffRequest>
{
    public CreateStaffRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Email) || !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Укажите email или телефон").WithName("email");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.BranchIds).Must(b => b is { Count: > 0 }).When(x => !x.AllBranches).WithMessage("Выберите хотя бы один филиал");
    }
}

public sealed class RoleRequestValidator : AbstractValidator<RoleRequest>
{
    public RoleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleForEach(x => x.Permissions).Must(p => Perm.AllCodes.Contains(p)).WithErrorCode(ErrorCodes.UnknownPermission).WithMessage("Неизвестное право {PropertyValue}");
        RuleFor(x => x.Limits.MaxDiscountPct).InclusiveBetween(0, 100).When(x => x.Limits?.MaxDiscountPct is not null);
        RuleFor(x => x.Limits.MaxWriteoffAmount).GreaterThanOrEqualTo(0).When(x => x.Limits?.MaxWriteoffAmount is not null);
        RuleFor(x => x.Limits.MaxRefundAmount).GreaterThanOrEqualTo(0).When(x => x.Limits?.MaxRefundAmount is not null);
    }
}

public sealed class NamedRefRequestValidator : AbstractValidator<NamedRefRequest>
{
    public NamedRefRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
}

// ---------- Service ----------
public sealed class OrgService(
    IAppDbContext db,
    ICurrentUser user,
    IPasswordHashing hashing,
    IMembershipCache membershipCache,
    IAuditService audit,
    TimeProvider clock)
{
    // Организация
    public async Task<OrgDto> GetOrgAsync(CancellationToken ct)
    {
        var o = await db.Organizations.AsNoTracking().FirstAsync(x => x.Id == user.OrganizationId, ct);
        return new OrgDto(o.Id, o.Name, o.Slug, o.Timezone, o.Currency, o.LogoUrl, o.Settings);
    }

    public async Task<OrgDto> UpdateOrgAsync(UpdateOrgRequest r, CancellationToken ct)
    {
        var o = await db.Organizations.FirstAsync(x => x.Id == user.OrganizationId, ct);
        if (r.Name is not null) o.Name = r.Name.Trim();
        if (r.Timezone is not null)
        {
            try { _ = TimeZoneInfo.FindSystemTimeZoneById(r.Timezone); }
            catch (TimeZoneNotFoundException) { throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Неизвестный часовой пояс"); }
            o.Timezone = r.Timezone;
        }
        if (r.LogoUrl is not null) o.LogoUrl = r.LogoUrl.NullIfEmpty();
        if (r.Settings is not null)
        {
            audit.Log(nameof(Organization), o.Id, "settings_update", new { old = o.Settings, @new = r.Settings });
            o.Settings = r.Settings;
        }
        await db.SaveChangesAsync(ct);
        return await GetOrgAsync(ct);
    }

    // Филиалы
    public async Task<IReadOnlyList<BranchDto>> ListBranchesAsync(bool includeInactive, CancellationToken ct)
    {
        var q = db.Branches.AsNoTracking().NotDeleted();
        if (!includeInactive) q = q.Where(b => b.IsActive);
        if (!user.AllBranches) q = q.Where(b => user.BranchIds.Contains(b.Id));
        return (await q.OrderBy(b => b.Name).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<BranchDto> GetBranchAsync(Guid id, CancellationToken ct)
    {
        user.EnsureBranchAccess(id);
        return ToDto(await db.Branches.AsNoTracking().GetOrThrowAsync(id, "Филиал", ct));
    }

    public async Task<BranchDto> CreateBranchAsync(BranchRequest r, CancellationToken ct)
    {
        var b = new Branch { Name = r.Name.Trim(), Address = r.Address.NullIfEmpty(), Phone = r.Phone.NullIfEmpty(), IsActive = r.IsActive ?? true, WorkingHours = r.WorkingHours ?? WorkingHours.Default() };
        db.Branches.Add(b);
        // Каждому филиалу — склад и касса по умолчанию.
        db.Warehouses.Add(new Warehouse { BranchId = b.Id, Type = WarehouseType.Branch, Name = "Склад " + b.Name });
        db.CashRegisters.Add(new CashRegister { BranchId = b.Id, Name = "Касса регистратуры" });
        await db.SaveChangesAsync(ct);
        return ToDto(b);
    }

    public async Task<BranchDto> UpdateBranchAsync(Guid id, BranchRequest r, CancellationToken ct)
    {
        user.EnsureBranchAccess(id);
        var b = await db.Branches.GetOrThrowAsync(id, "Филиал", ct);
        b.Name = r.Name.Trim();
        b.Address = r.Address.NullIfEmpty();
        b.Phone = r.Phone.NullIfEmpty();
        if (r.IsActive is not null) b.IsActive = r.IsActive.Value;
        if (r.WorkingHours is not null) b.WorkingHours = r.WorkingHours;
        await db.SaveChangesAsync(ct);
        return ToDto(b);
    }

    public async Task<IReadOnlyList<RoomDto>> ListRoomsAsync(Guid branchId, CancellationToken ct)
    {
        user.EnsureBranchAccess(branchId);
        return await db.Rooms.AsNoTracking().NotDeleted().Where(r => r.BranchId == branchId).OrderBy(r => r.Name)
            .Select(r => new RoomDto(r.Id, r.BranchId, r.Name)).ToListAsync(ct);
    }

    public async Task<RoomDto> CreateRoomAsync(Guid branchId, RoomRequest r, CancellationToken ct)
    {
        user.EnsureBranchAccess(branchId);
        _ = await db.Branches.GetOrThrowAsync(branchId, "Филиал", ct);
        var room = new Room { BranchId = branchId, Name = r.Name.Trim() };
        db.Rooms.Add(room);
        await db.SaveChangesAsync(ct);
        return new RoomDto(room.Id, room.BranchId, room.Name);
    }

    public async Task<IReadOnlyList<ChairDto>> ListChairsAsync(Guid? branchId, CancellationToken ct)
    {
        var q = db.Chairs.AsNoTracking().NotDeleted();
        if (branchId is { } b) { user.EnsureBranchAccess(b); q = q.Where(c => c.BranchId == b); }
        else if (!user.AllBranches) q = q.Where(c => user.BranchIds.Contains(c.BranchId));
        return await q.OrderBy(c => c.Name).Select(c => new ChairDto(c.Id, c.BranchId, c.RoomId, c.Name, c.IsActive)).ToListAsync(ct);
    }

    public async Task<ChairDto> CreateChairAsync(Guid branchId, ChairRequest r, CancellationToken ct)
    {
        user.EnsureBranchAccess(branchId);
        _ = await db.Branches.GetOrThrowAsync(branchId, "Филиал", ct);
        var c = new Chair { BranchId = branchId, RoomId = r.RoomId, Name = r.Name.Trim(), IsActive = r.IsActive ?? true };
        db.Chairs.Add(c);
        await db.SaveChangesAsync(ct);
        return new ChairDto(c.Id, c.BranchId, c.RoomId, c.Name, c.IsActive);
    }

    public async Task<ChairDto> UpdateChairAsync(Guid id, ChairRequest r, CancellationToken ct)
    {
        var c = await db.Chairs.GetOrThrowAsync(id, "Кресло", ct);
        user.EnsureBranchAccess(c.BranchId);
        c.Name = r.Name.Trim();
        c.RoomId = r.RoomId;
        if (r.IsActive is not null) c.IsActive = r.IsActive.Value;
        await db.SaveChangesAsync(ct);
        return new ChairDto(c.Id, c.BranchId, c.RoomId, c.Name, c.IsActive);
    }

    // Сотрудники
    public async Task<IReadOnlyList<StaffDto>> ListStaffAsync(bool includeFired, StaffPosition? position, Guid? branchId, CancellationToken ct)
    {
        var q = from m in db.Memberships.AsNoTracking()
                join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                join r in db.Roles.AsNoTracking() on m.RoleId equals r.Id
                select new { m, u, r };
        if (!includeFired) q = q.Where(x => x.m.IsActive);
        if (position is { } p) q = q.Where(x => x.m.Position == p);
        if (branchId is { } b) q = q.Where(x => x.m.AllBranches || x.m.BranchIds.Contains(b));
        var rows = await q.OrderBy(x => x.u.FullName).ToListAsync(ct);
        return rows.Select(x => ToDto(x.m, x.u, x.r)).ToList();
    }

    /// <summary>Врачи филиала (для календаря и форм) — доступно всем, кто видит расписание.</summary>
    public Task<IReadOnlyList<StaffDto>> ListDoctorsAsync(Guid? branchId, CancellationToken ct) =>
        ListStaffAsync(false, StaffPosition.Doctor, branchId, ct);

    public async Task<StaffDto> GetStaffAsync(Guid membershipId, CancellationToken ct)
    {
        var x = await (from m in db.Memberships.AsNoTracking()
                       join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                       join r in db.Roles.AsNoTracking() on m.RoleId equals r.Id
                       where m.Id == membershipId
                       select new { m, u, r }).FirstOrDefaultAsync(ct) ?? throw AppException.NotFound("Сотрудник");
        return ToDto(x.m, x.u, x.r);
    }

    public async Task<StaffDto> CreateStaffAsync(CreateStaffRequest r, CancellationToken ct)
    {
        var role = await db.Roles.NotDeleted().GetOrThrowAsync(r.RoleId, "Роль", ct);
        EnsureCanAssignRole(role);
        var email = r.Email.NullIfEmpty()?.ToLowerInvariant();
        var phone = Patient.NormalizePhone(r.Phone);

        // Пользователь глобальный: если email уже есть — добавляем членство существующему (сеть + другая организация).
        var existing = email is not null ? await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct)
            : await db.Users.FirstOrDefaultAsync(u => u.Phone == phone, ct);
        if (existing is not null && await db.Memberships.AnyAsync(m => m.UserId == existing.Id, ct))
            throw AppException.Conflict(email is not null ? ErrorCodes.EmailTaken : ErrorCodes.PhoneTaken, "Сотрудник с таким логином уже есть в организации");
        if (existing is null && phone is not null && await db.Users.AnyAsync(u => u.Phone == phone, ct))
            throw AppException.Conflict(ErrorCodes.PhoneTaken, "Этот телефон уже используется");

        var u = existing ?? new User { Email = email, Phone = phone, FullName = r.FullName.Trim(), IsActive = true };
        if (existing is null)
        {
            u.PasswordHash = hashing.Hash(u, r.Password);
            db.Users.Add(u);
        }
        var m = new Membership
        {
            UserId = u.Id,
            RoleId = role.Id,
            AllBranches = r.AllBranches,
            BranchIds = r.AllBranches ? [] : (r.BranchIds ?? []).Distinct().ToList(),
            Position = r.Position,
            Specialty = r.Specialty.NullIfEmpty(),
            Color = r.Color.NullIfEmpty(),
        };
        db.Memberships.Add(m);
        await db.SaveChangesAsync(ct);
        return ToDto(m, u, role);
    }

    public async Task<StaffDto> UpdateStaffAsync(Guid membershipId, UpdateStaffRequest r, CancellationToken ct)
    {
        var m = await db.Memberships.GetOrThrowAsync(membershipId, "Сотрудник", ct);
        var u = await db.Users.FirstAsync(x => x.Id == m.UserId, ct);
        if (r.FullName is not null) u.FullName = r.FullName.Trim();
        if (r.Email is not null)
        {
            var email = r.Email.NullIfEmpty()?.ToLowerInvariant();
            if (email is not null && await db.Users.AnyAsync(x => x.Email == email && x.Id != u.Id, ct)) throw AppException.Conflict(ErrorCodes.EmailTaken, "Этот email уже используется");
            u.Email = email;
        }
        if (r.Phone is not null)
        {
            var phone = Patient.NormalizePhone(r.Phone);
            if (phone is not null && await db.Users.AnyAsync(x => x.Phone == phone && x.Id != u.Id, ct)) throw AppException.Conflict(ErrorCodes.PhoneTaken, "Этот телефон уже используется");
            u.Phone = phone;
        }
        if (!string.IsNullOrEmpty(r.Password))
        {
            if (r.Password.Length < 8) throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Пароль — минимум 8 символов");
            u.PasswordHash = hashing.Hash(u, r.Password);
            await AuthService.RevokeAllAsync(db, u.Id, m.OrganizationId, clock.GetUtcNow(), ct);
        }
        if (r.RoleId is { } roleId && roleId != m.RoleId)
        {
            var role = await db.Roles.NotDeleted().GetOrThrowAsync(roleId, "Роль", ct);
            EnsureCanAssignRole(role);
            m.RoleId = roleId;
        }
        if (r.Position is not null) m.Position = r.Position.Value;
        if (r.Specialty is not null) m.Specialty = r.Specialty.NullIfEmpty();
        if (r.Color is not null) m.Color = r.Color.NullIfEmpty();
        if (r.AllBranches is not null) m.AllBranches = r.AllBranches.Value;
        if (r.BranchIds is not null) m.BranchIds = r.BranchIds.Distinct().ToList();
        await db.SaveChangesAsync(ct);
        await membershipCache.InvalidateAsync(u.Id, m.OrganizationId, ct);
        return await GetStaffAsync(m.Id, ct);
    }

    /// <summary>Увольнение: доступ пропадает немедленно (кэш прав сброшен, refresh-токены отозваны).</summary>
    public async Task<StaffDto> FireStaffAsync(Guid membershipId, CancellationToken ct)
    {
        var m = await db.Memberships.GetOrThrowAsync(membershipId, "Сотрудник", ct);
        if (m.UserId == user.UserId) throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Нельзя уволить самого себя");
        m.IsActive = false;
        m.FiredAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await AuthService.RevokeAllAsync(db, m.UserId, m.OrganizationId, clock.GetUtcNow(), ct);
        await membershipCache.InvalidateAsync(m.UserId, m.OrganizationId, ct);
        return await GetStaffAsync(m.Id, ct);
    }

    private void EnsureCanAssignRole(Role role)
    {
        // Назначить владельца может только владелец.
        if (role.Code == RolePresets.Owner && user.RoleCode != RolePresets.Owner)
            throw AppException.Forbidden("Назначить роль владельца может только владелец");
    }

    // Роли
    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().NotDeleted().OrderByDescending(r => r.IsPreset).ThenBy(r => r.Name).ToListAsync(ct);
        var counts = await db.Memberships.AsNoTracking().Where(m => m.IsActive).GroupBy(m => m.RoleId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return roles.Select(r => ToDto(r, counts.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<RoleDto> GetRoleAsync(Guid id, CancellationToken ct)
    {
        var r = await db.Roles.AsNoTracking().NotDeleted().GetOrThrowAsync(id, "Роль", ct);
        var count = await db.Memberships.CountAsync(m => m.RoleId == id && m.IsActive, ct);
        return ToDto(r, count);
    }

    public async Task<RoleDto> CreateRoleAsync(RoleRequest req, CancellationToken ct)
    {
        var r = new Role { Name = req.Name.Trim(), Code = "custom", Permissions = req.Permissions.Distinct().ToList(), Limits = req.Limits };
        db.Roles.Add(r);
        await db.SaveChangesAsync(ct);
        return ToDto(r, 0);
    }

    public async Task<RoleDto> UpdateRoleAsync(Guid id, RoleRequest req, CancellationToken ct)
    {
        var r = await db.Roles.NotDeleted().GetOrThrowAsync(id, "Роль", ct);
        if (r.Code == RolePresets.Owner) throw AppException.Conflict(ErrorCodes.PresetRoleLocked, "Роль владельца изменить нельзя");
        r.Name = req.Name.Trim();
        r.Permissions = req.Permissions.Distinct().ToList();
        r.Limits = new RoleLimits
        {
            MaxDiscountPct = req.Limits.MaxDiscountPct,
            MaxWriteoffAmount = req.Limits.MaxWriteoffAmount,
            MaxRefundAmount = req.Limits.MaxRefundAmount,
            CanEditClosedShiftVisits = req.Limits.CanEditClosedShiftVisits,
        };
        await db.SaveChangesAsync(ct);
        await membershipCache.InvalidateRoleAsync(r.OrganizationId, r.Id, ct);
        // Кэш по индексу может быть неполным — сбрасываем для всех сотрудников с этой ролью.
        foreach (var uid in await db.Memberships.Where(m => m.RoleId == r.Id).Select(m => m.UserId).ToListAsync(ct))
            await membershipCache.InvalidateAsync(uid, r.OrganizationId, ct);
        return await GetRoleAsync(r.Id, ct);
    }

    public async Task DeleteRoleAsync(Guid id, CancellationToken ct)
    {
        var r = await db.Roles.NotDeleted().GetOrThrowAsync(id, "Роль", ct);
        if (r.IsPreset) throw AppException.Conflict(ErrorCodes.PresetRoleLocked, "Системную роль удалить нельзя");
        if (await db.Memberships.AnyAsync(m => m.RoleId == id && m.IsActive, ct)) throw AppException.Conflict(ErrorCodes.RoleInUse, "Роль назначена сотрудникам");
        r.DeletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public static IReadOnlyList<PermissionGroupDto> PermissionCatalog() =>
        Perm.All.GroupBy(p => p.Group).Select(g => new PermissionGroupDto(g.Key, g.Select(x => x.Code).ToList())).ToList();

    // Кассы
    public async Task<IReadOnlyList<CashRegisterDto>> ListCashRegistersAsync(Guid? branchId, CancellationToken ct)
    {
        var q = db.CashRegisters.AsNoTracking().NotDeleted();
        if (branchId is { } b) q = q.Where(c => c.BranchId == b);
        else if (!user.AllBranches) q = q.Where(c => user.BranchIds.Contains(c.BranchId));
        return await q.OrderBy(c => c.Name).Select(c => new CashRegisterDto(c.Id, c.BranchId, c.Name)).ToListAsync(ct);
    }

    public async Task<CashRegisterDto> CreateCashRegisterAsync(CashRegisterRequest r, CancellationToken ct)
    {
        user.EnsureBranchAccess(r.BranchId);
        var c = new CashRegister { BranchId = r.BranchId, Name = r.Name.Trim() };
        db.CashRegisters.Add(c);
        await db.SaveChangesAsync(ct);
        return new CashRegisterDto(c.Id, c.BranchId, c.Name);
    }

    // Мапперы
    public static BranchDto ToDto(Branch b) => new(b.Id, b.Name, b.Address, b.Phone, b.IsActive, b.WorkingHours);

    public static StaffDto ToDto(Membership m, User u, Role r) =>
        new(m.Id, u.Id, u.FullName, u.Email, u.Phone, r.Id, r.Name, r.Code, m.Position, m.Specialty, m.Color, m.AllBranches, m.BranchIds, m.IsActive, m.FiredAt);

    public static RoleDto ToDto(Role r, int members) =>
        new(r.Id, r.Name, r.Code, r.IsPreset, r.Code == RolePresets.Owner ? Perm.AllCodes.Order(StringComparer.Ordinal).ToList() : r.Permissions, r.Limits, members);
}
