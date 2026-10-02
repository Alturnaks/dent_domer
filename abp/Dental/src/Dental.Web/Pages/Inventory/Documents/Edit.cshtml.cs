using System;
using System.Threading.Tasks;
using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dental.Inventory;
using System.IO;
using Microsoft.AspNetCore.Http;
using Volo.Abp;
using Volo.Abp.Content;

namespace Dental.Web.Pages.Inventory.Documents;

/// <summary>Редактор складского документа (новый — ?type=, существующий — ?id=). Логика — Edit.js + IStockAppService.</summary>
[Authorize(DentalPermissions.Inventory.View)]
[RequestSizeLimit(21*1024*1024)]
[RequestFormLimits(MultipartBodyLengthLimit=21*1024*1024)]
public class EditModel(IStockFileAppService files) : DentalPageModel
{
    public async Task<IActionResult> OnGetPdfAsync(Guid id)
    {
        using var content=await files.ExportPdfAsync(id);
        using var memory=new MemoryStream();await content.GetStream().CopyToAsync(memory);
        return File(memory.ToArray(),content.ContentType,content.FileName);
    }
    public async Task<IActionResult> OnGetTemplateAsync()
    {
        using var content=await files.GetReceiptTemplateAsync();
        using var memory=new MemoryStream();await content.GetStream().CopyToAsync(memory);
        return File(memory.ToArray(),content.ContentType,content.FileName);
    }
    public async Task<IActionResult> OnPostImportAsync(IFormFile file)
    {
        if(file==null || file.Length==0)throw new UserFriendlyException("Выберите непустой XLSX-файл.");
        await using var stream=file.OpenReadStream();
        using var content=new RemoteStreamContent(stream,file.FileName,file.ContentType,file.Length);
        return new JsonResult(await files.PreviewReceiptImportAsync(content));
    }
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
