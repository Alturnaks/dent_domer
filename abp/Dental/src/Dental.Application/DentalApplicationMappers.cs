using Dental.Branches;
using Riok.Mapperly.Abstractions;
using Volo.Abp.Mapperly;

namespace Dental;

// Маппинги сущность → DTO (Mapperly, compile-time). Вызываются через ObjectMapper.Map<TSource, TDest>.
// Модули кладут свои мапперы рядом со своими app-сервисами (Application/{Module}/{Module}Mappers.cs).

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class BranchToBranchDtoMapper : MapperBase<Branch, BranchDto>
{
    public override partial BranchDto Map(Branch source);
    public override partial void Map(Branch source, BranchDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class BranchToBranchLookupDtoMapper : MapperBase<Branch, BranchLookupDto>
{
    public override partial BranchLookupDto Map(Branch source);
    public override partial void Map(Branch source, BranchLookupDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class RoomToRoomDtoMapper : MapperBase<Room, RoomDto>
{
    public override partial RoomDto Map(Room source);
    public override partial void Map(Room source, RoomDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ChairToChairDtoMapper : MapperBase<Chair, ChairDto>
{
    public override partial ChairDto Map(Chair source);
    public override partial void Map(Chair source, ChairDto destination);
}
