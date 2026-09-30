using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Branches;
using Dental.Staff;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Dental.Web.Pages.Staff;

public abstract class EmployeeModalModelBase : DentalPageModel
{
    protected readonly IEmployeeAppService EmployeeAppService;
    protected readonly IBranchAppService BranchAppService;

    [BindProperty]
    public EmployeeFormViewModel Employee { get; set; } = new();

    public List<SelectListItem> Roles { get; private set; } = [];
    public List<SelectListItem> Branches { get; private set; } = [];
    public List<SelectListItem> Positions { get; private set; } = [];

    protected EmployeeModalModelBase(IEmployeeAppService employeeAppService, IBranchAppService branchAppService)
    {
        EmployeeAppService = employeeAppService;
        BranchAppService = branchAppService;
    }

    protected async Task LoadListsAsync()
    {
        Roles = (await EmployeeAppService.GetRoleLookupAsync()).Items
            .Select(r => new SelectListItem(r.Name, r.Name)).ToList();
        Branches = (await BranchAppService.GetLookupAsync()).Items
            .Select(b => new SelectListItem(b.Name, b.Id.ToString(), Employee.BranchIds.Contains(b.Id))).ToList();
        Positions = Enum.GetValues<StaffPosition>()
            .Select(p => new SelectListItem(L["Enum:StaffPosition." + p].Value, p.ToString(), p == Employee.Position)).ToList();
    }
}
