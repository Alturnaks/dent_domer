using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Dental.Permissions;
using Dental.Roles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Web.Pages.RoleLimits;

/// <summary>Редактирование лимитов роли. Суммы на форме — в тенге, в API — в тиынах.</summary>
[Authorize(DentalPermissions.Org.RolesManage)]
public class EditModalModel : DentalPageModel
{
    private readonly IRoleLimitAppService _roleLimitAppService;

    [HiddenInput]
    [BindProperty(SupportsGet = true)]
    public Guid RoleId { get; set; }

    public string RoleName { get; private set; } = "";

    [BindProperty]
    public LimitsViewModel Limits { get; set; } = new();

    public EditModalModel(IRoleLimitAppService roleLimitAppService)
    {
        _roleLimitAppService = roleLimitAppService;
    }

    public async Task OnGetAsync()
    {
        var dto = await _roleLimitAppService.GetAsync(RoleId);
        RoleName = dto.RoleName;
        Limits = new LimitsViewModel
        {
            MaxDiscountPct = dto.MaxDiscountPct,
            MaxWriteoffTenge = dto.MaxWriteoffAmount / 100m,
            MaxRefundTenge = dto.MaxRefundAmount / 100m,
            CanEditClosedShiftVisits = dto.CanEditClosedShiftVisits,
        };
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await _roleLimitAppService.UpdateAsync(RoleId, new UpdateRoleLimitDto
        {
            MaxDiscountPct = Limits.MaxDiscountPct,
            MaxWriteoffAmount = Limits.MaxWriteoffTenge is { } w ? (long)Math.Round(w * 100m) : null,
            MaxRefundAmount = Limits.MaxRefundTenge is { } r ? (long)Math.Round(r * 100m) : null,
            CanEditClosedShiftVisits = Limits.CanEditClosedShiftVisits,
        });
        return NoContent();
    }

    public class LimitsViewModel
    {
        [Range(0, 100)]
        public decimal? MaxDiscountPct { get; set; }

        [Range(0, 1_000_000_000)]
        public decimal? MaxWriteoffTenge { get; set; }

        [Range(0, 1_000_000_000)]
        public decimal? MaxRefundTenge { get; set; }

        public bool CanEditClosedShiftVisits { get; set; }
    }
}
