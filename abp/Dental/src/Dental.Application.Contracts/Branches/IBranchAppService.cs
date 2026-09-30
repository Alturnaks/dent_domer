using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Dental.Branches;

/// <summary>Филиалы, кабинеты и кресла. Изменения — право Dental.Org.BranchesManage.</summary>
public interface IBranchAppService : IApplicationService
{
    Task<PagedResultDto<BranchDto>> GetListAsync(GetBranchListInput input);
    Task<BranchDto> GetAsync(Guid id);
    Task<BranchDto> CreateAsync(CreateUpdateBranchDto input);
    Task<BranchDto> UpdateAsync(Guid id, CreateUpdateBranchDto input);
    Task DeleteAsync(Guid id);

    /// <summary>Филиалы, доступные текущему пользователю (для выпадающих списков любых модулей).</summary>
    Task<ListResultDto<BranchLookupDto>> GetLookupAsync();

    Task<ListResultDto<RoomDto>> GetRoomsAsync(Guid branchId);
    Task<RoomDto> CreateRoomAsync(Guid branchId, CreateUpdateRoomDto input);
    Task<RoomDto> UpdateRoomAsync(Guid id, CreateUpdateRoomDto input);
    Task DeleteRoomAsync(Guid id);

    Task<ListResultDto<ChairDto>> GetChairsAsync(Guid branchId);
    Task<ChairDto> CreateChairAsync(Guid branchId, CreateUpdateChairDto input);
    Task<ChairDto> UpdateChairAsync(Guid id, CreateUpdateChairDto input);
    Task DeleteChairAsync(Guid id);
}
