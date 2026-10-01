using System.Threading.Tasks;
using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Authorization;
namespace Dental.Reports;
[Authorize(DentalPermissions.Org.SettingsManage)]
public class OperationsAppService(IOperationsRunner runner) : DentalAppService
{
    public async Task RunAsync()
    {if(CurrentTenant.Id==null)throw new AbpAuthorizationException();await runner.RunTenantAsync(CurrentTenant.Id.Value);}
}
