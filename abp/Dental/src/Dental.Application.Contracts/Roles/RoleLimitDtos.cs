using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Dental.Roles;

/// <summary>Лимиты роли. Суммы — в тиынах (1 ₸ = 100), null = без ограничения.</summary>
public class RoleLimitDto
{
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = null!;
    public decimal? MaxDiscountPct { get; set; }
    public long? MaxWriteoffAmount { get; set; }
    public long? MaxRefundAmount { get; set; }
    public bool CanEditClosedShiftVisits { get; set; }
}

public class UpdateRoleLimitDto
{
    [Range(0, 100)]
    public decimal? MaxDiscountPct { get; set; }

    [Range(0, long.MaxValue)]
    public long? MaxWriteoffAmount { get; set; }

    [Range(0, long.MaxValue)]
    public long? MaxRefundAmount { get; set; }

    public bool CanEditClosedShiftVisits { get; set; }
}

/// <summary>Редактор лимитов ролей (право Dental.Org.RolesManage). Права ролей — стандартный UI ABP.</summary>
public interface IRoleLimitAppService : IApplicationService
{
    Task<ListResultDto<RoleLimitDto>> GetListAsync();
    Task<RoleLimitDto> GetAsync(Guid id);
    Task<RoleLimitDto> UpdateAsync(Guid id, UpdateRoleLimitDto input);

    /// <summary>Итоговые лимиты текущего пользователя.</summary>
    Task<RoleLimitsData> GetCurrentAsync();
}
