using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace Dental.Inventory;

public class ReceiptImportLineDto : StockLineInputDto
{
    public int Row { get; set; }
    public string ItemName { get; set; } = "";
    public string ItemSku { get; set; } = "";
    public string UnitName { get; set; } = "";
}
public class ReceiptImportPreviewDto
{
    public List<ReceiptImportLineDto> Lines { get; set; } = [];
    public List<ReceiptImportError> Errors { get; set; } = [];
}
public interface IStockFileAppService : IApplicationService
{
    Task<ReceiptImportPreviewDto> PreviewReceiptImportAsync(IRemoteStreamContent file);
    Task<IRemoteStreamContent> GetReceiptTemplateAsync();
    Task<IRemoteStreamContent> ExportPdfAsync(Guid id);
}
