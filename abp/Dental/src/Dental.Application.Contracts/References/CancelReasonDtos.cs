using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Dental.References;

public class CancelReasonDto : EntityDto<Guid>
{
    public string Name { get; set; } = "";
    public CancelReasonType Type { get; set; }
}

public class CreateUpdateCancelReasonDto
{
    [Required, StringLength(ReferenceConsts.MaxNameLength)]
    public string Name { get; set; } = "";

    public CancelReasonType Type { get; set; }
}

/// <summary>Причины отмены/переноса записей. Чтение — любой сотрудник, изменение — Dental.Org.SettingsManage.</summary>
public interface ICancelReasonAppService : IApplicationService
{
    Task<ListResultDto<CancelReasonDto>> GetListAsync(CancelReasonType? type);
    Task<CancelReasonDto> CreateAsync(CreateUpdateCancelReasonDto input);
    Task<CancelReasonDto> UpdateAsync(Guid id, CreateUpdateCancelReasonDto input);
    Task DeleteAsync(Guid id);
}
