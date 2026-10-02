using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ClosedXML.Excel;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Dental.Inventory;

public class ReceiptSpreadsheet : IReceiptSpreadsheet, ITransientDependency
{
    public static readonly string[] Headers = ["Артикул", "Количество", "Единица", "Цена, ₸", "Партия", "Серийный номер", "Годен до"];
    public const int MaxRows = 5000;

    public ReceiptSpreadsheetResult Read(byte[] content)
    {
        if(content.Length == 0 || content.Length > 20 * 1024 * 1024) throw new UserFriendlyException("Файл должен быть непустым и не превышать 20 МБ.");
        try
        {
            using(var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read))
            {
                if(zip.Entries.Count > 5000 || zip.Entries.Sum(e => e.Length) > 100 * 1024 * 1024L)
                    throw new UserFriendlyException("Слишком большой распакованный Excel-файл.");
                if(zip.Entries.Any(e => e.FullName.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)))
                    throw new UserFriendlyException("Используйте XLSX без макросов и внешних связей.");
            }
            using var book = new XLWorkbook(new MemoryStream(content));
            var sheet = book.Worksheet(1);
            for(var c = 0; c < Headers.Length; c++)
                if(sheet.Cell(1,c+1).HasFormula || sheet.Cell(1,c+1).GetString().Trim() != Headers[c])
                    throw new UserFriendlyException("Заголовки не соответствуют шаблону. Скачайте шаблон импорта и заполните первый лист.");
            var last = sheet.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 1;
            if(last > MaxRows + 1) throw new UserFriendlyException("Импорт ограничен 5000 строками. Разделите файл.");
            var result = new ReceiptSpreadsheetResult();
            for(var r=2; r<=last; r++)
            {
                var cells = Enumerable.Range(1,Headers.Length).Select(c=>sheet.Cell(r,c)).ToArray();
                if(cells.All(c=>c.IsEmpty()) && !sheet.Row(r).CellsUsed(XLCellsUsedOptions.Contents).Any()) continue;
                try
                {
                    if(cells.Any(c=>c.HasFormula)) throw new FormatException("Формулы не поддерживаются: замените их значениями.");
                    if(sheet.Row(r).CellsUsed(XLCellsUsedOptions.Contents).Any(c=>c.Address.ColumnNumber>Headers.Length)) throw new FormatException("Данные за пределами семи колонок шаблона.");
                    var sku = Text(cells[0], InventoryConsts.MaxSkuLength);
                    if(sku.Length == 0) throw new FormatException("Укажите артикул.");
                    var qty = Number(cells[1]); var price = Number(cells[3]);
                    if(qty<=0 || qty>100_000_000 || decimal.Round(qty,InventoryConsts.QtyScale)!=qty) throw new FormatException("Количество должно быть больше нуля, не больше 100 000 000, до 4 знаков после запятой.");
                    if(price<0 || price>long.MaxValue/100m || decimal.Round(price,2)!=price) throw new FormatException("Цена в тенге должна быть неотрицательной, до 2 знаков после запятой.");
                    result.Rows.Add(new(r,sku,qty,Text(cells[2],100),price,Empty(Text(cells[4],InventoryConsts.MaxBatchNumberLength)),Empty(Text(cells[5],InventoryConsts.MaxSerialNumberLength)),Date(cells[6])));
                }
                catch(FormatException e) { result.Errors.Add(new(r,e.Message)); }
                if(result.Errors.Count >= 100) break;
            }
            if(result.Rows.Count==0 && result.Errors.Count==0) result.Errors.Add(new(0,"Первый лист не содержит строк прихода."));
            return result;
        }
        catch(UserFriendlyException) { throw; }
        catch(Exception e) when(e is not OutOfMemoryException) { throw new UserFriendlyException("Не удалось прочитать XLSX. Используйте исправный файл по шаблону."); }
    }

    public byte[] Template(IReadOnlyList<ReceiptTemplateItem> items)
    {
        using var book = new XLWorkbook(); var sheet=book.Worksheets.Add("Приход");
        for(var c=0;c<Headers.Length;c++){sheet.Cell(1,c+1).Value=Headers[c];sheet.Cell(1,c+1).Style.Font.Bold=true;sheet.Cell(1,c+1).Style.Fill.BackgroundColor=XLColor.FromHtml("#EEF2F7");}
        sheet.Columns().Width=22;sheet.Column(1).Style.NumberFormat.Format="@";sheet.Column(7).Style.DateFormat.Format="dd.MM.yyyy";sheet.SheetView.FreezeRows(1);
        var help=book.Worksheets.Add("Инструкция");
        string[] notes=["Заполните первый лист «Приход». Заголовки и порядок колонок сохраняйте.","Артикул берите из листа «Номенклатура». Пустая единица означает базовую; упаковка должна быть заведена у товара.","Количество и цена относятся к указанной единице. Цена — в тенге, максимум две цифры после запятой.","Срок: дата Excel, ГГГГ-ММ-ДД или ДД.ММ.ГГГГ. Формулы, макросы и внешние связи не поддерживаются.","Для партийного товара обязательна партия, для серийного — серия и одна штука, для срока годности — дата.","Лимиты: 20 МБ, 5000 строк. Импорт сначала проверяется, затем добавляется в редактор. Остатки меняются только после проведения."];
        for(var r=0;r<notes.Length;r++)help.Cell(r+1,1).Value=notes[r];help.Column(1).Width=100;help.Column(1).Style.Alignment.WrapText=true;
        var catalog=book.Worksheets.Add("Номенклатура");catalog.Cell(1,1).Value="Артикул";catalog.Cell(1,2).Value="Товар";catalog.Cell(1,3).Value="Доступные единицы";
        for(var r=0;r<items.Count;r++){catalog.Cell(r+2,1).Value=items[r].Sku;catalog.Cell(r+2,2).Value=items[r].Name;catalog.Cell(r+2,3).Value=items[r].Units;}
        catalog.Row(1).Style.Font.Bold=true;catalog.Columns().AdjustToContents();foreach(var col in catalog.Columns())if(col.Width>70)col.Width=70;
        using var stream=new MemoryStream();book.SaveAs(stream);return stream.ToArray();
    }
    private static string Text(IXLCell cell,int max){var s=cell.GetString().Trim();if(s.Length>max)throw new FormatException($"Значение «{cell.Address.ColumnLetter}» длиннее {max} символов.");return s;}
    private static string? Empty(string s)=>s.Length==0?null:s;
    private static decimal Number(IXLCell cell)
    {
        if(cell.DataType==XLDataType.Number && cell.TryGetValue<decimal>(out var n))return n;
        var s=cell.GetString().Trim().Replace("\u00a0","").Replace(" ","");
        if(decimal.TryParse(s,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out n) || decimal.TryParse(s,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.GetCultureInfo("ru-RU"),out n))return n;
        throw new FormatException("Количество и цена должны быть числами.");
    }
    private static DateOnly? Date(IXLCell cell)
    {
        if(cell.IsEmpty())return null;
        if(cell.DataType==XLDataType.DateTime)return DateOnly.FromDateTime(cell.GetDateTime());
        if(DateOnly.TryParseExact(cell.GetString().Trim(),["yyyy-MM-dd","dd.MM.yyyy"],CultureInfo.InvariantCulture,DateTimeStyles.None,out var d))return d;
        throw new FormatException("Срок годности: дата Excel, ГГГГ-ММ-ДД или ДД.ММ.ГГГГ.");
    }
}
