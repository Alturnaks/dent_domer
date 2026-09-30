using System.Threading.Tasks;
using Dental.Branches;
using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Web.Pages.Branches;

[Authorize(DentalPermissions.Org.BranchesManage)]
public class CreateModalModel : DentalPageModel
{
    private readonly IBranchAppService _branchAppService;

    [BindProperty]
    public BranchFormViewModel Branch { get; set; } = new();

    public CreateModalModel(IBranchAppService branchAppService)
    {
        _branchAppService = branchAppService;
    }

    public void OnGet()
    {
        Branch = BranchFormViewModel.From(null);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await _branchAppService.CreateAsync(Branch.ToDto());
        return NoContent();
    }
}
