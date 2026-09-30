using Dental.Foundation;
using Xunit;

namespace Dental.EntityFrameworkCore.Applications;

[Collection(DentalTestConsts.CollectionDefinitionName)]
public class EfCoreFoundationSeedTests : FoundationSeedTests<DentalEntityFrameworkCoreTestModule>
{
}
