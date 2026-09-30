using Dental.Samples;
using Xunit;

namespace Dental.EntityFrameworkCore.Domains;

[Collection(DentalTestConsts.CollectionDefinitionName)]
public class EfCoreSampleDomainTests : SampleDomainTests<DentalEntityFrameworkCoreTestModule>
{

}
