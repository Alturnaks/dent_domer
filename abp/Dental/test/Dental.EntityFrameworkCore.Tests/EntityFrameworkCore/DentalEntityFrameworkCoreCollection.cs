using Xunit;

namespace Dental.EntityFrameworkCore;

[CollectionDefinition(DentalTestConsts.CollectionDefinitionName)]
public class DentalEntityFrameworkCoreCollection : ICollectionFixture<DentalEntityFrameworkCoreFixture>
{

}
