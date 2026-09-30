using System;
using System.Collections.Generic;
using System.Linq;

namespace Dental.Staff;

/// <summary>Доступные текущему пользователю филиалы. AllBranches = без ограничения.</summary>
public sealed record BranchScopeInfo(bool AllBranches, IReadOnlyList<Guid> BranchIds)
{
    public static BranchScopeInfo All { get; } = new(true, []);
    public static BranchScopeInfo Nothing { get; } = new(false, []);

    public bool Contains(Guid branchId) => AllBranches || BranchIds.Contains(branchId);
}
