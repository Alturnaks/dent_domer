using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dental.Permissions;
using Dental.Reports;
using Dental.Settings;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Settings;

namespace Dental.Inventory;

[Authorize(DentalPermissions.Inventory.View)]
public class StockFileAppService(IReceiptSpreadsheet spreadsheet, IRepository<Item,Guid> items, IStockAppService stock, IReportDocumentRenderer renderer, ISettingProvider settings) : DentalAppService, IStockFileAppService
{
    [Authorize(DentalPermissions.Inventory.Receive)]
    public async Task<ReceiptImportPreviewDto> PreviewReceiptImportAsync(IRemoteStreamContent file)
    {
        if(!string.Equals(Path.GetExtension(file.FileName),".xlsx",StringComparison.OrdinalIgnoreCase))throw new UserFriendlyException("Выберите файл XLSX по шаблону.");
        if(file.ContentLength > 20*1024*1024)throw new UserFriendlyException("Размер файла не должен превышать 20 МБ.");
        using var memory=new MemoryStream();var buffer=new byte[81920];var stream=file.GetStream();int read;
        while((read=await stream.ReadAsync(buffer))>0){if(memory.Length+read>20*1024*1024)throw new UserFriendlyException("Размер файла не должен превышать 20 МБ.");await memory.WriteAsync(buffer.AsMemory(0,read));}
        var parsed=spreadsheet.Read(memory.ToArray());var result=new ReceiptImportPreviewDto{Errors=parsed.Errors};
        var wanted=parsed.Rows.Select(r=>r.Sku.ToLowerInvariant()).Distinct().ToList();
        var lookup=(await AsyncExecuter.ToListAsync((await items.WithDetailsAsync(i=>i.Units)).Where(i=>i.IsActive&&wanted.Contains(i.Sku.ToLower())))).ToLookup(i=>i.Sku,StringComparer.OrdinalIgnoreCase);
        foreach(var row in parsed.Rows)
        {
            var matches=lookup[row.Sku].ToList();
            if(matches.Count!=1){result.Errors.Add(new(row.Row,"Артикул не найден среди активных товаров или неоднозначен: "+row.Sku));continue;}
            var item=matches[0];var baseName=Unit(item.BaseUnit);Guid? unitId=null;decimal factor=1;
            if(row.Unit.Length>0 && !string.Equals(row.Unit,baseName,StringComparison.OrdinalIgnoreCase) && !string.Equals(row.Unit,item.BaseUnit.ToString(),StringComparison.OrdinalIgnoreCase))
            {
                var units=item.Units.Where(u=>string.Equals(u.UnitName,row.Unit,StringComparison.OrdinalIgnoreCase)).ToList();
                if(units.Count!=1){result.Errors.Add(new(row.Row,"Единица не найдена у товара: "+row.Unit));continue;}
                unitId=units[0].Id;factor=units[0].FactorToBase;
            }
            if(factor>100_000_000/row.Quantity){result.Errors.Add(new(row.Row,"Количество в базовой единице превышает 100 000 000."));continue;}
            if(decimal.Round(row.Quantity*factor,InventoryConsts.QtyScale)!=row.Quantity*factor){result.Errors.Add(new(row.Row,"Количество в базовой единице допускает не больше 4 знаков после запятой."));continue;}
            if(row.Price*100*row.Quantity>long.MaxValue){result.Errors.Add(new(row.Row,"Сумма строки превышает допустимое значение."));continue;}
            if(item.TrackSerials && (row.Quantity*factor!=1 || row.SerialNumber==null)){result.Errors.Add(new(row.Row,"Для серийного товара укажите серийный номер и количество, равное одной штуке."));continue;}
            if(item.TrackBatches && row.BatchNumber==null && row.SerialNumber==null){result.Errors.Add(new(row.Row,"Для товара требуется номер партии."));continue;}
            if(item.TrackExpiry && row.ExpiresAt==null){result.Errors.Add(new(row.Row,"Для товара требуется срок годности."));continue;}
            result.Lines.Add(new(){Row=row.Row,ItemId=item.Id,ItemSku=item.Sku,ItemName=item.Name,Qty=row.Quantity,UnitId=unitId,UnitName=unitId==null?baseName:row.Unit,UnitCost=(long)(row.Price*100),BatchNumber=row.BatchNumber,SerialNumber=row.SerialNumber,ExpiresAt=row.ExpiresAt});
        }
        foreach(var repeated in result.Lines.Where(l=>l.SerialNumber!=null).GroupBy(l=>(l.ItemId,l.SerialNumber)).Where(g=>g.Count()>1))
            foreach(var line in repeated)result.Errors.Add(new(line.Row,"Серийный номер повторяется в файле."));
        if(result.Lines.Sum(l=>l.UnitCost.GetValueOrDefault()*(decimal)l.Qty)>long.MaxValue)
            result.Errors.Add(new(0,"Общая сумма импортируемых строк превышает допустимое значение."));
        return result;
    }

    [Authorize(DentalPermissions.Inventory.Receive)]
    public async Task<IRemoteStreamContent> GetReceiptTemplateAsync()
    {
        var catalog=await AsyncExecuter.ToListAsync((await items.WithDetailsAsync(i=>i.Units)).Where(i=>i.IsActive).OrderBy(i=>i.Name));
        var bytes=spreadsheet.Template(catalog.Select(i=>new ReceiptTemplateItem(i.Sku,i.Name,string.Join("; ",new[]{Unit(i.BaseUnit)}.Concat(i.Units.Select(u=>u.UnitName+" ("+u.FactorToBase+" "+Unit(i.BaseUnit)+")"))))).ToList());
        return new RemoteStreamContent(new MemoryStream(bytes),"receipt-template.xlsx","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    public async Task<IRemoteStreamContent> ExportPdfAsync(Guid id)
    {
        // The same tenant/warehouse scope as the editor applies to printing every document type.
        var doc=await stock.GetAsync(id);var table=new ReportTable{Timezone=await settings.GetOrNullAsync(DentalSettings.Timezone)??"Asia/Almaty"};
        var title=L["Enum:StockDocumentType."+(int)doc.Type].Value+" "+doc.Number+" · "+L["Enum:StockDocumentStatus."+(int)doc.Status].Value;
        var tz=TimeZoneInfo.FindSystemTimeZoneById(table.Timezone);
        string Date(DateTime d)=>TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(d,DateTimeKind.Utc),tz).ToString("dd.MM.yyyy HH:mm");
        var metadata="Создан: "+Date(doc.CreationTime)+(doc.CreatorName==null?"":" · "+doc.CreatorName);
        if(doc.WarehouseFromName!=null)metadata+="\nСклад-отправитель: "+doc.WarehouseFromName;
        if(doc.WarehouseToName!=null)metadata+="\nСклад-получатель: "+doc.WarehouseToName;
        if(doc.SupplierName!=null)metadata+="\nПоставщик: "+doc.SupplierName;
        if(doc.InvoiceNumber!=null)metadata+="\nНакладная: "+doc.InvoiceNumber+" · "+doc.InvoiceDate?.ToString("dd.MM.yyyy");
        if(doc.ReasonName!=null)metadata+="\nПричина: "+doc.ReasonName;
        if(doc.PostedAt!=null)metadata+="\nПроведён: "+Date(doc.PostedAt.Value)+(doc.PostedByName==null?"":" · "+doc.PostedByName);
        if(doc.ReceivedAt!=null)metadata+="\nПолучен: "+Date(doc.ReceivedAt.Value);
        if(doc.Comment!=null)metadata+="\nКомментарий: "+doc.Comment;
        table.Description=metadata;
        var count=doc.Type==StockDocumentType.Inventory;var transfer=doc.Type==StockDocumentType.Transfer;
        table.Columns=[new("№","Int32"),new("Артикул / товар","String"),new("Партия / серия","String"),new("Годен до","DateOnly"),new(count?"По учёту":transfer?"Отправлено":"Количество","Quantity"),new("Единица","String")];
        if(count||transfer){table.Columns.Add(new("Факт","Quantity"));table.Columns.Add(new("Разница","Quantity"));}
        table.Columns.Add(new("Цена за базовую ед., ₸","UnitCost"));table.Columns.Add(new("Сумма, ₸","Decimal"));
        for(var n=0;n<doc.Lines.Count;n++)
        {
            var line=doc.Lines[n];var row=new System.Collections.Generic.List<object?>{n+1,line.ItemSku+" · "+line.ItemName,string.Join(" / ",new[]{line.BatchNumber,line.SerialNumber}.Where(s=>s!=null)),line.ExpiresAt,count?line.ExpectedQty:line.Qty,Unit(line.BaseUnit)};
            if(count||transfer){row.Add(line.ActualQty);row.Add(line.ActualQty.HasValue?line.ActualQty-(count?line.ExpectedQty:line.Qty):null);}
            row.Add(line.UnitCost/100m);row.Add(line.TotalCost/100m);table.Rows.Add(row);
        }
        var total=Enumerable.Repeat<object?>(null,table.Columns.Count).ToList();total[1]="Итого, ₸";total[^1]=doc.TotalCost/100m;table.Rows.Add(total);
        return new RemoteStreamContent(new MemoryStream(renderer.Render(table,title,"pdf")),"stock-"+id.ToString("N")+".pdf","application/pdf");
    }
    private static string Unit(BaseUnit unit)=>unit switch{BaseUnit.Pcs=>"шт",BaseUnit.G=>"г",BaseUnit.Ml=>"мл",BaseUnit.Pack=>"упак",_=>unit.ToString()};
}
