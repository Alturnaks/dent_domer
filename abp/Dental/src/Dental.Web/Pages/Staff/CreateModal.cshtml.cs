using System.Threading.Tasks;
using Dental.Branches;
using Dental.Permissions;
using Dental.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Web.Pages.Staff;

[Authorize(DentalPermissions.Org.StaffManage)]
public class CreateModalModel : EmployeeModalModelBase
{
    public CreateModalModel(IEmployeeAppService employeeAppService, IBranchAppService branchAppService)
        : base(employeeAppService, branchAppService)
    {
    }

    public async Task OnGetAsync()
    {
        await LoadListsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var input = new CreateEmployeeDto { Email = Employee.Email ?? "", Password = Employee.Password ?? "" };
        Employee.Fill(input);
        await EmployeeAppService.CreateAsync(input);
        return NoContent();
    }
}
