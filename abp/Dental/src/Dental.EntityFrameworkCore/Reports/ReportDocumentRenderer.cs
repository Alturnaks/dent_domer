using System;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Volo.Abp.DependencyInjection;
namespace Dental.Reports;
public class ReportDocumentRenderer : IReportDocumentRenderer, ITransientDependency
{
    static ReportDocumentRenderer() => QuestPDF.Settings.License = LicenseType.Community;
    public byte[] Render(ReportTable table, string title, string format)
    {
        var timezone=TimeZoneInfo.FindSystemTimeZoneById(table.Timezone);
        DateTime Local(DateTime value)=>TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value,DateTimeKind.Utc),timezone);
        if (format == "xlsx")
        {
            using var workbook=new XLWorkbook();var sheet=workbook.Worksheets.Add("Отчёт");sheet.Cell(1,1).Value=title;sheet.Cell(1,1).Style.Font.Bold=true;
            for(var c=0;c<table.Columns.Count;c++){sheet.Cell(3,c+1).Value=table.Columns[c].Name;sheet.Cell(3,c+1).Style.Font.Bold=true;sheet.Cell(3,c+1).Style.Fill.BackgroundColor=XLColor.FromHtml("#EEF2F7");}
            for(var r=0;r<table.Rows.Count;r++) for(var c=0;c<table.Columns.Count;c++) {var cell=sheet.Cell(r+4,c+1);var value=table.Rows[r][c];switch(value){case decimal n:cell.Value=n;break;case long n:cell.Value=n;break;case int n:cell.Value=n;break;case double n:cell.Value=n;break;case DateTime d:cell.Value=Local(d);cell.Style.DateFormat.Format="dd.MM.yyyy hh:mm";break;case DateOnly d:cell.Value=d.ToDateTime(TimeOnly.MinValue);cell.Style.DateFormat.Format="dd.MM.yyyy";break;default:cell.Value=Format(value);break;}}
            sheet.SheetView.FreezeRows(3);sheet.Columns().AdjustToContents();foreach(var col in sheet.Columns()) if(col.Width>60)col.Width=60;
            using var stream=new MemoryStream();workbook.SaveAs(stream);return stream.ToArray();
        }
        return Document.Create(document => document.Page(page => {
            page.Size(PageSizes.A4.Landscape());page.Margin(20);page.DefaultTextStyle(t=>t.FontSize(8));page.Header().PaddingBottom(10).Text(title).FontSize(13).Bold();
            page.Content().Table(t => {t.ColumnsDefinition(cols => {foreach(var c in table.Columns) cols.RelativeColumn();});t.Header(h => {foreach(var c in table.Columns)h.Cell().Background("#EEF2F7").Padding(3).Text(c.Name).Bold();});foreach(var row in table.Rows)foreach(var value in row)t.Cell().BorderBottom(0.5f).BorderColor("#E2E8F0").Padding(3).Text(Format(value is DateTime d?Local(d):value));});
            page.Footer().AlignRight().Text(t=>{t.CurrentPageNumber();t.Span(" / ");t.TotalPages();});
        })).GeneratePdf();
    }
    private static string Format(object? value) => value switch {null=>"",DateTime d=>d.ToString("dd.MM.yyyy HH:mm",CultureInfo.InvariantCulture),DateOnly d=>d.ToString("dd.MM.yyyy",CultureInfo.InvariantCulture),decimal d=>d.ToString("N2",CultureInfo.GetCultureInfo("ru-RU")),_=>Convert.ToString(value,CultureInfo.GetCultureInfo("ru-RU"))??""};
}
