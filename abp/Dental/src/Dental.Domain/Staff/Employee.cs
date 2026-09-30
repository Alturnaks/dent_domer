using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dental.Staff;

/// <summary>
/// Профиль сотрудника арендатора (бывший Membership). Связан 1:1 с IdentityUser (UserId).
/// Роли/права — через ABP Identity (роли пользователя), здесь — кадровые данные и доступ к филиалам.
/// </summary>
public class Employee : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string FullName { get; private set; } = null!;
    public string? Phone { get; set; }
    public StaffPosition Position { get; set; }
    public string? Specialty { get; set; }
    public string? Color { get; set; }
    public bool AllBranches { get; private set; }
    public List<Guid> BranchIds { get; private set; } = [];
    public bool IsActive { get; private set; } = true;
    public DateTime? FiredAt { get; private set; }

    protected Employee() { }

    public Employee(Guid id, Guid? tenantId, Guid userId, string fullName, StaffPosition position) : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        SetFullName(fullName);
        Position = position;
    }

    public void SetFullName(string fullName) =>
        FullName = Check.NotNullOrWhiteSpace(fullName, nameof(fullName), EmployeeConsts.MaxFullNameLength).Trim();

    public void SetBranches(bool allBranches, IEnumerable<Guid>? branchIds)
    {
        var ids = (branchIds ?? []).Distinct().ToList();
        if (!allBranches && ids.Count == 0)
        {
            throw new BusinessException(DentalDomainErrorCodes.EmployeeBranchesRequired);
        }
        AllBranches = allBranches;
        BranchIds = allBranches ? [] : ids;
    }

    public bool HasBranch(Guid branchId) => AllBranches || BranchIds.Contains(branchId);

    public void Fire(DateTime firedAt)
    {
        if (!IsActive)
        {
            throw new BusinessException(DentalDomainErrorCodes.EmployeeAlreadyFired);
        }
        IsActive = false;
        FiredAt = firedAt;
    }

    public void Rehire()
    {
        IsActive = true;
        FiredAt = null;
    }
}
