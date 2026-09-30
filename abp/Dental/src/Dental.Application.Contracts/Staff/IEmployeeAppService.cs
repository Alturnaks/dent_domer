using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Dental.Staff;

/// <summary>Персонал. Изменения — право Dental.Org.StaffManage.</summary>
public interface IEmployeeAppService : IApplicationService
{
    Task<PagedResultDto<EmployeeDto>> GetListAsync(GetEmployeeListInput input);
    Task<EmployeeDto> GetAsync(Guid id);
    Task<EmployeeDto> CreateAsync(CreateEmployeeDto input);
    Task<EmployeeDto> UpdateAsync(Guid id, UpdateEmployeeDto input);
    Task FireAsync(Guid id);
    Task RehireAsync(Guid id);

    Task<ListResultDto<RoleLookupDto>> GetRoleLookupAsync();

    /// <summary>Активные сотрудники (по умолчанию врачи) в доступных филиалах — для списков выбора в модулях.</summary>
    Task<ListResultDto<EmployeeLookupDto>> GetLookupAsync(GetEmployeeLookupInput input);

    Task<CurrentEmployeeDto> GetCurrentAsync();
}
