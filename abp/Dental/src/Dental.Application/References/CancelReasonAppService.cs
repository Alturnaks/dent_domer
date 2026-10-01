using System;
using System.Linq;
using System.Threading.Tasks;
using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Dental.References;

[Authorize]
public class CancelReasonAppService : DentalAppService, ICancelReasonAppService
{
    private readonly IRepository<CancelReason, Guid> _repository;

    public CancelReasonAppService(IRepository<CancelReason, Guid> repository) => _repository = repository;

    public async Task<ListResultDto<CancelReasonDto>> GetListAsync(CancelReasonType? type) =>
        new((await _repository.GetListAsync(x => type == null || x.Type == type)).OrderBy(x => x.Type).ThenBy(x => x.Name).Select(ToDto).ToList());

    [Authorize(DentalPermissions.Org.SettingsManage)]
    public async Task<CancelReasonDto> CreateAsync(CreateUpdateCancelReasonDto input) =>
        ToDto(await _repository.InsertAsync(new CancelReason(GuidGenerator.Create(), CurrentTenant.Id, input.Name, input.Type), autoSave: true));

    [Authorize(DentalPermissions.Org.SettingsManage)]
    public async Task<CancelReasonDto> UpdateAsync(Guid id, CreateUpdateCancelReasonDto input)
    {
        var x = await _repository.GetAsync(id);
        x.SetName(input.Name);
        x.Type = input.Type;
        await _repository.UpdateAsync(x, autoSave: true);
        return ToDto(x);
    }

    [Authorize(DentalPermissions.Org.SettingsManage)]
    public Task DeleteAsync(Guid id) => _repository.DeleteAsync(id);

    private static CancelReasonDto ToDto(CancelReason x) => new() { Id = x.Id, Name = x.Name, Type = x.Type };
}
