using Dental.Inventory;
using Xunit;

namespace Dental.EntityFrameworkCore.Applications;

[Collection(DentalTestConsts.CollectionDefinitionName)]
public class EfCoreInventoryTests : InventoryTests<DentalEntityFrameworkCoreTestModule>
{
}
