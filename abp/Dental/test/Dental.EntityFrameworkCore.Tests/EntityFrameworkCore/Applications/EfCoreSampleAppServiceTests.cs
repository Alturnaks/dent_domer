using Dental.Samples;
using Xunit;

namespace Dental.EntityFrameworkCore.Applications;

[Collection(DentalTestConsts.CollectionDefinitionName)]
public class EfCoreSampleAppServiceTests : SampleAppServiceTests<DentalEntityFrameworkCoreTestModule>
{

}
