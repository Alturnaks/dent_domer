using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace Dental.Branches;

public class BranchDto : FullAuditedEntityDto<Guid>
{
    public string Name { get; set; } = null!;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
    public List<WorkingDay> WorkingHours { get; set; } = [];
}

public class CreateUpdateBranchDto
{
    [Required]
    [StringLength(BranchConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    [StringLength(BranchConsts.MaxAddressLength)]
    public string? Address { get; set; }

    [StringLength(BranchConsts.MaxPhoneLength)]
    public string? Phone { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>7 дней (0 = вс … 6 = сб). null при создании — часы по умолчанию, при изменении — без изменений.</summary>
    public List<WorkingDay>? WorkingHours { get; set; }
}

public class GetBranchListInput : PagedAndSortedResultRequestDto
{
    public string? Filter { get; set; }
    public bool? IsActive { get; set; }
}

public class BranchLookupDto : EntityDto<Guid>
{
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; }
}

public class RoomDto : EntityDto<Guid>
{
    public Guid BranchId { get; set; }
    public string Name { get; set; } = null!;
}

public class CreateUpdateRoomDto
{
    [Required]
    [StringLength(BranchConsts.MaxRoomNameLength)]
    public string Name { get; set; } = null!;
}

public class ChairDto : EntityDto<Guid>
{
    public Guid BranchId { get; set; }
    public Guid? RoomId { get; set; }
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; }
}

public class CreateUpdateChairDto
{
    [Required]
    [StringLength(BranchConsts.MaxChairNameLength)]
    public string Name { get; set; } = null!;

    public Guid? RoomId { get; set; }
    public bool IsActive { get; set; } = true;
}
