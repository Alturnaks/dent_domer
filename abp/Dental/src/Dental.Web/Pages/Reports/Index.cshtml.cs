using Dental.Permissions;
using Dental.Web.Authorization;
using Dental.Reports;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Web.Pages.Reports;

/// <summary>Раздел приложения с проверкой прав текущего пользователя.</summary>
[AnyPermission(DentalPermissions.Reports.Branch, DentalPermissions.Reports.Network, DentalPermissions.Reports.Finance, DentalPermissions.Reports.Payroll, DentalPermissions.Inventory.View)]
public class IndexModel(IReportsAppService reports) : DentalPageModel
{
    public void OnGet()
    {
    }
    public async Task<IActionResult> OnGetExportAsync(ReportInput input)
    {
        using var content = await reports.ExportAsync(input);
        using var stream = new MemoryStream(); await content.GetStream().CopyToAsync(stream);
        return File(stream.ToArray(),content.ContentType,content.FileName);
    }
}
