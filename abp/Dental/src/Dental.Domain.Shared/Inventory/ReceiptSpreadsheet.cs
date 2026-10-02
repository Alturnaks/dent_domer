using System;
using System.Collections.Generic;

namespace Dental.Inventory;

public record ReceiptSpreadsheetRow(int Row, string Sku, decimal Quantity, string Unit, decimal Price, string? BatchNumber, string? SerialNumber, DateOnly? ExpiresAt);
public record ReceiptImportError(int Row, string Message);
public class ReceiptSpreadsheetResult
{
    public List<ReceiptSpreadsheetRow> Rows { get; set; } = [];
    public List<ReceiptImportError> Errors { get; set; } = [];
}
public record ReceiptTemplateItem(string Sku, string Name, string Units);
public interface IReceiptSpreadsheet
{
    ReceiptSpreadsheetResult Read(byte[] content);
    byte[] Template(IReadOnlyList<ReceiptTemplateItem> items);
}
