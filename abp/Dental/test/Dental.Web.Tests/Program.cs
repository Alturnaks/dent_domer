using Microsoft.AspNetCore.Builder;
using Dental;
using Volo.Abp.AspNetCore.TestBase;

var builder = WebApplication.CreateBuilder();
builder.Environment.ContentRootPath = GetWebProjectContentRootPathHelper.Get("Dental.Web.csproj"); 
await builder.RunAbpModuleAsync<DentalWebTestModule>(applicationName: "Dental.Web");

public partial class Program
{
}
