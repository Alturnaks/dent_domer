using System;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Dental.Branches;

[Authorize]
public class BranchAppService : DentalAppService, IBranchAppService
{
    private readonly IRepository<Branch, Guid> _branchRepository;
    private readonly IRepository<Room, Guid> _roomRepository;
    private readonly IRepository<Chair, Guid> _chairRepository;

    public BranchAppService(
        IRepository<Branch, Guid> branchRepository,
        IRepository<Room, Guid> roomRepository,
        IRepository<Chair, Guid> chairRepository)
    {
        _branchRepository = branchRepository;
        _roomRepository = roomRepository;
        _chairRepository = chairRepository;
    }

    [Authorize(DentalPermissions.Org.BranchesManage)]
    public async Task<PagedResultDto<BranchDto>> GetListAsync(GetBranchListInput input)
    {
        var query = await _branchRepository.GetQueryableAsync();
        query = await BranchScope.ApplyAsync(query, b => b.Id);
        query = query
            .WhereIf(!input.Filter.IsNullOrWhiteSpace(), b => b.Name.ToLower().Contains(input.Filter!.ToLower()) || (b.Address != null && b.Address.ToLower().Contains(input.Filter!.ToLower())))
            .WhereIf(input.IsActive.HasValue, b => b.IsActive == input.IsActive);

        var total = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query
            .OrderBy(input.Sorting.IsNullOrWhiteSpace() ? nameof(Branch.Name) : input.Sorting)
            .PageBy(input));
        return new PagedResultDto<BranchDto>(total, ObjectMapper.Map<System.Collections.Generic.List<Branch>, System.Collections.Generic.List<BranchDto>>(items));
    }

    [Authorize(DentalPermissions.Org.BranchesManage)]
    public async Task<BranchDto> GetAsync(Guid id)
    {
        await BranchScope.EnsureCanAccessAsync(id);
        return ObjectMapper.Map<Branch, BranchDto>(await _branchRepository.GetAsync(id));
    }

    [Authorize(DentalPermissions.Org.BranchesManage)]
    public async Task<BranchDto> CreateAsync(CreateUpdateBranchDto input)
    {
        if (!(await BranchScope.GetAsync()).AllBranches)
        {
            throw new BusinessException(DentalDomainErrorCodes.BranchAccessDenied);
        }
        await EnsureUniqueNameAsync(input.Name, null);
        var branch = new Branch(GuidGenerator.Create(), CurrentTenant.Id, input.Name)
        {
            Address = input.Address,
            Phone = input.Phone,
            IsActive = input.IsActive,
        };
        if (input.WorkingHours != null)
        {
            branch.SetWorkingHours(input.WorkingHours);
        }
        await _branchRepository.InsertAsync(branch, autoSave: true);
        return ObjectMapper.Map<Branch, BranchDto>(branch);
    }

    [Authorize(DentalPermissions.Org.BranchesManage)]
    public async Task<BranchDto> UpdateAsync(Guid id, CreateUpdateBranchDto input)
    {
        await BranchScope.EnsureCanAccessAsync(id);
        var branch = await _branchRepository.GetAsync(id);
        await EnsureUniqueNameAsync(input.Name, id);
        branch.SetName(input.Name);
        branch.Address = input.Address;
        branch.Phone = input.Phone;
        branch.IsActive = input.IsActive;
        if (input.WorkingHours != null)
        {
            branch.SetWorkingHours(input.WorkingHours);
        }
        await _branchRepository.UpdateAsync(branch, autoSave: true);
        return ObjectMapper.Map<Branch, BranchDto>(branch);
    }

    /// <summary>Мягкое удаление (IsDeleted). Кабинеты и кресла остаются привязанными.</summary>
    [Authorize(DentalPermissions.Org.BranchesManage)]
    public async Task DeleteAsync(Guid id)
    {
        if (!(await BranchScope.GetAsync()).AllBranches)
        {
            throw new BusinessException(DentalDomainErrorCodes.BranchAccessDenied);
        }
        await _branchRepository.DeleteAsync(id);
    }

    public async Task<ListResultDto<BranchLookupDto>> GetLookupAsync()
    {
        var query = await BranchScope.ApplyAsync(await _branchRepository.GetQueryableAsync(), b => b.Id);
        var items = await AsyncExecuter.ToListAsync(query.OrderBy(b => b.Name));
        return new ListResultDto<BranchLookupDto>(ObjectMapper.Map<System.Collections.Generic.List<Branch>, System.Collections.Generic.List<BranchLookupDto>>(items));
    }

    public async Task<ListResultDto<RoomDto>> GetRoomsAsync(Guid branchId)
    {
        await BranchScope.EnsureCanAccessAsync(branchId);
        var items = (await _roomRepository.GetListAsync(r => r.BranchId == branchId)).OrderBy(r => r.Name).ToList();
        return new ListResultDto<RoomDto>(ObjectMapper.Map<System.Collections.Generic.List<Room>, System.Collections.Generic.List<RoomDto>>(items));
    }

    [Authorize(DentalPermissions.Org.BranchesManage)]
    public async Task<RoomDto> CreateRoomAsync(Guid branchId, CreateUpdateRoomDto input)
    {
        await BranchScope.EnsureCanAccessAsync(branchId);
        await _branchRepository.GetAsync(branchId);
        var room = await _roomRepository.InsertAsync(new Room(GuidGenerator.Create(), CurrentTenant.Id, branchId, input.Name), autoSave: true);
        return ObjectMapper.Map<Room, RoomDto>(room);
    }

    [Authorize(DentalPermissions.Org.BranchesManage)]
    public async Task<RoomDto> UpdateRoomAsync(Guid id, CreateUpdateRoomDto input)
    {
        var room = await _roomRepository.GetAsync(id);
        await BranchScope.EnsureCanAccessAsync(room.BranchId);
        room.SetName(input.Name);
        await _roomRepository.UpdateAsync(room, autoSave: true);
        return ObjectMapper.Map<Room, RoomDto>(room);
    }

    [Authorize(DentalPermissions.Org.BranchesManage)]
    public async Task DeleteRoomAsync(Guid id)
    {
        var room = await _roomRepository.GetAsync(id);
        await BranchScope.EnsureCanAccessAsync(room.BranchId);
        foreach (var chair in await _chairRepository.GetListAsync(c => c.RoomId == id))
        {
            chair.RoomId = null;
            await _chairRepository.UpdateAsync(chair);
        }
        await _roomRepository.DeleteAsync(room);
    }

    public async Task<ListResultDto<ChairDto>> GetChairsAsync(Guid branchId)
    {
        await BranchScope.EnsureCanAccessAsync(branchId);
        var items = (await _chairRepository.GetListAsync(c => c.BranchId == branchId)).OrderBy(c => c.Name).ToList();
        return new ListResultDto<ChairDto>(ObjectMapper.Map<System.Collections.Generic.List<Chair>, System.Collections.Generic.List<ChairDto>>(items));
    }

    [Authorize(DentalPermissions.Org.BranchesManage)]
    public async Task<ChairDto> CreateChairAsync(Guid branchId, CreateUpdateChairDto input)
    {
        await BranchScope.EnsureCanAccessAsync(branchId);
        await _branchRepository.GetAsync(branchId);
        await EnsureRoomInBranchAsync(input.RoomId, branchId);
        var chair = new Chair(GuidGenerator.Create(), CurrentTenant.Id, branchId, input.Name, input.RoomId) { IsActive = input.IsActive };
        await _chairRepository.InsertAsync(chair, autoSave: true);
        return ObjectMapper.Map<Chair, ChairDto>(chair);
    }

    [Authorize(DentalPermissions.Org.BranchesManage)]
    public async Task<ChairDto> UpdateChairAsync(Guid id, CreateUpdateChairDto input)
    {
        var chair = await _chairRepository.GetAsync(id);
        await BranchScope.EnsureCanAccessAsync(chair.BranchId);
        await EnsureRoomInBranchAsync(input.RoomId, chair.BranchId);
        chair.SetName(input.Name);
        chair.RoomId = input.RoomId;
        chair.IsActive = input.IsActive;
        await _chairRepository.UpdateAsync(chair, autoSave: true);
        return ObjectMapper.Map<Chair, ChairDto>(chair);
    }

    [Authorize(DentalPermissions.Org.BranchesManage)]
    public async Task DeleteChairAsync(Guid id)
    {
        var chair = await _chairRepository.GetAsync(id);
        await BranchScope.EnsureCanAccessAsync(chair.BranchId);
        await _chairRepository.DeleteAsync(chair);
    }

    private async Task EnsureUniqueNameAsync(string name, Guid? exceptId)
    {
        var trimmed = name.Trim();
        if (await _branchRepository.AnyAsync(b => b.Name == trimmed && b.Id != exceptId))
        {
            throw new BusinessException(DentalDomainErrorCodes.BranchNameAlreadyExists).WithData("name", trimmed);
        }
    }

    private async Task EnsureRoomInBranchAsync(Guid? roomId, Guid branchId)
    {
        if (roomId is { } rid && !await _roomRepository.AnyAsync(r => r.Id == rid && r.BranchId == branchId))
        {
            throw new Volo.Abp.Domain.Entities.EntityNotFoundException(typeof(Room), rid);
        }
    }
}
