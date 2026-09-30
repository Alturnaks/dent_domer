using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Branches;
using Dental.Staff;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Web.Pages;

/// <summary>Дашборд (заглушка): приветствие, филиалы и лимиты текущего пользователя.</summary>
public class IndexModel : DentalPageModel
{
    private readonly IEmployeeAppService _employeeAppService;
    private readonly IBranchAppService _branchAppService;

    public CurrentEmployeeDto? Me { get; private set; }
    public List<string> BranchNames { get; private set; } = [];

    public IndexModel(IEmployeeAppService employeeAppService, IBranchAppService branchAppService)
    {
        _employeeAppService = employeeAppService;
        _branchAppService = branchAppService;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!CurrentUser.IsAuthenticated)
        {
            return Redirect("~/Account/Login");
        }
        if (CurrentTenant.IsAvailable)
        {
            Me = await _employeeAppService.GetCurrentAsync();
            BranchNames = (await _branchAppService.GetLookupAsync()).Items.Select(b => b.Name).ToList();
        }
        return Page();
    }
}
