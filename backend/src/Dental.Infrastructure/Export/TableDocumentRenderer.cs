using System.Globalization;
using ClosedXML.Excel;
using Dental.Application.Common;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Dental.Infrastructure.Export;

/// <summary>Excel (ClosedXML) и PDF (QuestPDF, Community License — см. DECISIONS.md) для табличных документов.</summary>
public sealed class TableDocumentRenderer : ITableDocumentRenderer
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    static TableDocumentRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] ToXlsx(TableDocument document)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName(document.Title));
        var row = 1;
        ws.Cell(row, 1).Value = document.Title;
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 14;
        row += 2;
        foreach (var line in document.HeaderLines) ws.Cell(row++, 1).Value = line;
        if (document.HeaderLines.Count > 0) row++;

        var headerRow = row;
        for (var c = 0; c < document.Columns.Count; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = document.Columns[c].Title;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2F7");
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }
        row++;
        foreach (var values in document.Rows)
        {
            for (var c = 0; c < document.Columns.Count && c < values.Count; c++)
            {
                var cell = ws.Cell(row, c + 1);
                switch (values[c])
                {
                    case null: break;
                    case decimal d: cell.Value = d; break;
                    case long l: cell.Value = l; break;
                    case int i: cell.Value = i; break;
                    case double db: cell.Value = db; break;
                    default: cell.Value = values[c]!.ToString(); break;
                }
                if (document.Columns[c].Format is { } f) cell.Style.NumberFormat.Format = f;
                if (document.Columns[c].Numeric) cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }
            row++;
        }
        if (document.Rows.Count > 0) ws.Range(headerRow, 1, row - 1, document.Columns.Count).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        row++;
        foreach (var line in document.FooterLines)
        {
            ws.Cell(row, 1).Value = line;
            ws.Cell(row, 1).Style.Font.Bold = true;
            row++;
        }
        ws.Columns(1, document.Columns.Count).AdjustToContents(headerRow, row);
        foreach (var col in ws.Columns(1, document.Columns.Count))
        {
            if (col.Width > 60) col.Width = 60;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] ToPdf(TableDocument document) =>
        Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(30);
            page.DefaultTextStyle(x => x.FontSize(9)); // Lato из пакета QuestPDF (кириллица есть), системные шрифты не нужны
            page.Header().Column(col =>
            {
                col.Item().Text(document.Title).FontSize(14).Bold();
                foreach (var line in document.HeaderLines) col.Item().Text(line);
                col.Item().PaddingBottom(8);
            });
            page.Content().Column(col =>
            {
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cd =>
                    {
                        foreach (var c in document.Columns) cd.RelativeColumn(c.RelativeWidth);
                    });
                    table.Header(h =>
                    {
                        foreach (var c in document.Columns)
                        {
                            var cell = h.Cell().Background("#EEF2F7").BorderBottom(1).BorderColor("#94A3B8").Padding(3);
                            if (c.Numeric) cell.AlignRight().Text(c.Title).Bold();
                            else cell.Text(c.Title).Bold();
                        }
                    });
                    foreach (var values in document.Rows)
                    {
                        for (var i = 0; i < document.Columns.Count; i++)
                        {
                            var c = document.Columns[i];
                            var text = Format(i < values.Count ? values[i] : null, c);
                            var cell = table.Cell().BorderBottom(0.5f).BorderColor("#E2E8F0").Padding(3);
                            if (c.Numeric) cell.AlignRight().Text(text);
                            else cell.Text(text);
                        }
                    }
                });
                col.Item().PaddingTop(10).Column(f =>
                {
                    foreach (var line in document.FooterLines) f.Item().Text(line).Bold();
                });
            });
            page.Footer().AlignRight().Text(t =>
            {
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        })).GeneratePdf();

    private static string Format(object? value, TableColumn column) => value switch
    {
        null => "",
        decimal d => column.Format?.Contains(".00", StringComparison.Ordinal) == true ? d.ToString("#,##0.00", Ru) : d.ToString("#,##0.###", Ru),
        long l => l.ToString("#,##0", Ru),
        int i => i.ToString(Ru),
        _ => value.ToString() ?? "",
    };

    private static string SheetName(string title)
    {
        char[] invalid = [':', '\\', '/', '?', '*', '[', ']'];
        var name = new string(title.Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
        if (name.Length == 0) name = "Лист1";
        return name.Length > 31 ? name[..31] : name;
    }
}
