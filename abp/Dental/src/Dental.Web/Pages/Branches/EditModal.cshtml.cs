using System;
using System.Threading.Tasks;
using Dental.Branches;
using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Web.Pages.Branches;

[Authorize(DentalPermissions.Org.BranchesManage)]
public class EditModalModel : DentalPageModel
{
    private readonly IBranchAppService _branchAppService;

    [HiddenInput]
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public BranchFormViewModel Branch { get; set; } = new();

    public EditModalModel(IBranchAppService branchAppService)
    {
        _branchAppService = branchAppService;
    }

    public async Task OnGetAsync()
    {
        Branch = BranchFormViewModel.From(await _branchAppService.GetAsync(Id));
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await _branchAppService.UpdateAsync(Id, Branch.ToDto());
        return NoContent();
    }
}
