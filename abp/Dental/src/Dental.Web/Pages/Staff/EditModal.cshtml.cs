using System;
using System.Threading.Tasks;
using Dental.Branches;
using Dental.Permissions;
using Dental.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Web.Pages.Staff;

[Authorize(DentalPermissions.Org.StaffManage)]
public class EditModalModel : EmployeeModalModelBase
{
    [HiddenInput]
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public EditModalModel(IEmployeeAppService employeeAppService, IBranchAppService branchAppService)
        : base(employeeAppService, branchAppService)
    {
    }

    public async Task OnGetAsync()
    {
        Employee = EmployeeFormViewModel.From(await EmployeeAppService.GetAsync(Id));
        await LoadListsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var input = new UpdateEmployeeDto();
        Employee.Fill(input);
        await EmployeeAppService.UpdateAsync(Id, input);
        return NoContent();
    }
}
