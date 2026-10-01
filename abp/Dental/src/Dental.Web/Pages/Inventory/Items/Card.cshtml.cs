using System;
using System.Threading.Tasks;
using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Web.Pages.Inventory.Items;

/// <summary>Карточка товара: остатки по складам/партиям, нормы остатков, движения.</summary>
[Authorize(DentalPermissions.Inventory.View)]
public class CardModel : DentalPageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public bool CanManage { get; private set; }

    public async Task OnGetAsync()
    {
        CanManage = await AuthorizationService.IsGrantedAsync(DentalPermissions.Inventory.ItemsManage);
    }
}
