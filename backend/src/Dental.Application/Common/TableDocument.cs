namespace Dental.Application.Common;

/// <summary>
/// Табличный документ для выгрузки (заказ поставщику, отчёты): заголовок, строки «шапки», таблица, итоговые строки.
/// Значения ячеек — string, decimal, long или null; числа выводятся в Excel числами.
/// </summary>
public sealed record TableDocument(
    string Title,
    IReadOnlyList<string> HeaderLines,
    IReadOnlyList<TableColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    IReadOnlyList<string> FooterLines);

/// <summary>Колонка: Numeric — выравнивание вправо; Format — формат числа Excel (например, "#,##0.00").</summary>
public sealed record TableColumn(string Title, bool Numeric = false, string? Format = null, float RelativeWidth = 1);

/// <summary>Рендер табличного документа в Excel (ClosedXML) и PDF (QuestPDF). Реализация — Infrastructure.</summary>
public interface ITableDocumentRenderer
{
    byte[] ToXlsx(TableDocument document);
    byte[] ToPdf(TableDocument document);
}
