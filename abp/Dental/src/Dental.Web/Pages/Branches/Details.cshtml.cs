using System;
using System.Threading.Tasks;
using Dental.Branches;
using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Web.Pages.Branches;

/// <summary>Кабинеты и кресла филиала.</summary>
[Authorize(DentalPermissions.Org.BranchesManage)]
public class DetailsModel : DentalPageModel
{
    private readonly IBranchAppService _branchAppService;

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public BranchDto Branch { get; private set; } = null!;

    public DetailsModel(IBranchAppService branchAppService)
    {
        _branchAppService = branchAppService;
    }

    public async Task OnGetAsync()
    {
        Branch = await _branchAppService.GetAsync(Id);
    }
}
