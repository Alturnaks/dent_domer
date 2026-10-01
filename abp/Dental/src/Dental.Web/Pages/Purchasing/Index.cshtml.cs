using Dental.Permissions;
using Dental.Web.Authorization;
using Dental.Purchasing;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Web.Pages.Purchasing;

/// <summary>Раздел приложения с проверкой прав текущего пользователя.</summary>
[AnyPermission(DentalPermissions.Purchase.RequestCreate, DentalPermissions.Purchase.OrderCreate, DentalPermissions.Purchase.OrderApprove)]
public class IndexModel(IPurchasingAppService purchasing) : DentalPageModel
{
    public void OnGet()
    {
    }
    public async Task<IActionResult> OnGetExportAsync(Guid id,string format="xlsx")
    {using var content=await purchasing.ExportOrderAsync(id,new PurchaseExportInput{Format=format});using var stream=new MemoryStream();await content.GetStream().CopyToAsync(stream);return File(stream.ToArray(),content.ContentType,content.FileName);}
}
