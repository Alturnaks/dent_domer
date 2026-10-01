using System.Collections.Generic;
using System.Threading.Tasks;
using Dental.Inventory;
using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;

namespace Dental.Web.Pages.Inventory.Documents;

[Authorize(DentalPermissions.Inventory.View)]
public class IndexModel : DentalPageModel
{
    public List<StockDocumentType> CreatableTypes { get; } = [];

    public async Task OnGetAsync()
    {
        CreatableTypes.AddRange(await CreatableTypesAsync());
    }

    /// <summary>Типы документов, которые текущий пользователь может создавать.</summary>
    private async Task<List<StockDocumentType>> CreatableTypesAsync()
    {
        var result = new List<StockDocumentType>();
        var auth = AuthorizationService;
        if (await auth.IsGrantedAsync(DentalPermissions.Inventory.Receive)) result.Add(StockDocumentType.Receipt);
        if (await auth.IsGrantedAsync(DentalPermissions.Inventory.TransferCreate)) result.Add(StockDocumentType.Transfer);
        if (await auth.IsGrantedAsync(DentalPermissions.Inventory.Writeoff))
        {
            result.Add(StockDocumentType.Writeoff);
            result.Add(StockDocumentType.ReturnToSupplier);
        }
        if (await auth.IsGrantedAsync(DentalPermissions.Inventory.Count)) result.Add(StockDocumentType.Inventory);
        return result;
    }
}
