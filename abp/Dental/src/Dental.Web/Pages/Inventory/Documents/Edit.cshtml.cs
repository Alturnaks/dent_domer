using System;
using System.Threading.Tasks;
using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Web.Pages.Inventory.Documents;

/// <summary>Редактор складского документа (новый — ?type=, существующий — ?id=). Логика — Edit.js + IStockAppService.</summary>
[Authorize(DentalPermissions.Inventory.View)]
public class EditModel : DentalPageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Type { get; set; }

    public PermissionFlags Perms { get; } = new();

    public async Task OnGetAsync()
    {
        Perms.Receive = await AuthorizationService.IsGrantedAsync(DentalPermissions.Inventory.Receive);
        Perms.TransferCreate = await AuthorizationService.IsGrantedAsync(DentalPermissions.Inventory.TransferCreate);
        Perms.TransferReceive = await AuthorizationService.IsGrantedAsync(DentalPermissions.Inventory.TransferReceive);
        Perms.Writeoff = await AuthorizationService.IsGrantedAsync(DentalPermissions.Inventory.Writeoff);
        Perms.Count = await AuthorizationService.IsGrantedAsync(DentalPermissions.Inventory.Count);
        Perms.CountApprove = await AuthorizationService.IsGrantedAsync(DentalPermissions.Inventory.CountApprove);
    }

    public class PermissionFlags
    {
        public bool Receive { get; set; }
        public bool TransferCreate { get; set; }
        public bool TransferReceive { get; set; }
        public bool Writeoff { get; set; }
        public bool Count { get; set; }
        public bool CountApprove { get; set; }
    }
}
