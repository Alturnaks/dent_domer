using System;
using System.Linq;
using System.Threading.Tasks;
using Dental.Permissions;
using Dental.References;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Dental.Patients;

/// <summary>Справочник источников привлечения. Чтение — любой сотрудник, изменение — настройки организации.</summary>
[Authorize]
public class LeadSourceAppService : DentalAppService, ILeadSourceAppService
{
    private readonly IRepository<LeadSource, Guid> _repository;

    public LeadSourceAppService(IRepository<LeadSource, Guid> repository) => _repository = repository;

    public async Task<ListResultDto<LeadSourceDto>> GetListAsync() =>
        new((await _repository.GetListAsync()).OrderBy(x => x.Name).Select(x => new LeadSourceDto { Id = x.Id, Name = x.Name }).ToList());

    [Authorize(DentalPermissions.Org.SettingsManage)]
    public async Task<LeadSourceDto> CreateAsync(CreateUpdateLeadSourceDto input)
    {
        var x = await _repository.InsertAsync(new LeadSource(GuidGenerator.Create(), CurrentTenant.Id, input.Name), autoSave: true);
        return new LeadSourceDto { Id = x.Id, Name = x.Name };
    }

    [Authorize(DentalPermissions.Org.SettingsManage)]
    public async Task<LeadSourceDto> UpdateAsync(Guid id, CreateUpdateLeadSourceDto input)
    {
        var x = await _repository.GetAsync(id);
        x.SetName(input.Name);
        await _repository.UpdateAsync(x, autoSave: true);
        return new LeadSourceDto { Id = x.Id, Name = x.Name };
    }

    [Authorize(DentalPermissions.Org.SettingsManage)]
    public Task DeleteAsync(Guid id) => _repository.DeleteAsync(id);
}
