using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace Dental.Pages;

[Collection(DentalTestConsts.CollectionDefinitionName)]
public class Index_Tests : DentalWebTestBase
{
    [Fact]
    public async Task Welcome_Page()
    {
        var response = await GetResponseAsStringAsync("/");
        response.ShouldNotBeNull();
    }
}
