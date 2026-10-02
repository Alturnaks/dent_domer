using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Dental.Inventory;
using Dental.Reports;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Dental.EntityFrameworkCore.Applications;

public class ReceiptSpreadsheetTests
{
    private static byte[] Workbook(Action<IXLWorksheet> fill)
    {
        using var book=new XLWorkbook();var sheet=book.Worksheets.Add("Приход");
        for(var n=0;n<ReceiptSpreadsheet.Headers.Length;n++)sheet.Cell(1,n+1).Value=ReceiptSpreadsheet.Headers[n];
        fill(sheet);using var memory=new MemoryStream();book.SaveAs(memory);return memory.ToArray();
    }
    [Fact]
    public void Parses_Text_Skus_Russian_Decimals_And_Excel_Dates()
    {
        var result=new ReceiptSpreadsheet().Read(Workbook(s=>{s.Cell(2,1).Value="000123";s.Cell(2,2).Value="1,5";s.Cell(2,3).Value="упаковка";s.Cell(2,4).Value="1 250,50";s.Cell(2,5).Value="Партия А";s.Cell(2,7).Value=new DateTime(2027,6,30);}));
        result.Errors.ShouldBeEmpty();var row=result.Rows.Single();row.Sku.ShouldBe("000123");row.Quantity.ShouldBe(1.5m);row.Price.ShouldBe(1250.50m);row.ExpiresAt.ShouldBe(new DateOnly(2027,6,30));
    }
    [Fact]
    public void Rejects_Formulas_Without_Evaluating_Them()
    {
        var result=new ReceiptSpreadsheet().Read(Workbook(s=>{s.Cell(2,1).Value="ABC";s.Cell(2,2).FormulaA1="1+1";s.Cell(2,4).Value=100;}));
        result.Rows.ShouldBeEmpty();result.Errors.Single().Row.ShouldBe(2);result.Errors.Single().Message.ShouldContain("Формулы");
    }
    [Theory]
    [InlineData(0,10)]
    [InlineData(-1,10)]
    [InlineData(1,-10)]
    [InlineData(1,1.001)]
    [InlineData(1.00001,10)]
    public void Rejects_Invalid_Quantities_And_Prices(double quantity,double price)
    {
        var result=new ReceiptSpreadsheet().Read(Workbook(s=>{s.Cell(2,1).Value="ABC";s.Cell(2,2).Value=quantity;s.Cell(2,4).Value=price;}));
        result.Rows.ShouldBeEmpty();result.Errors.Single().Row.ShouldBe(2);
    }
    [Fact]
    public void Rejects_Wrong_Headers_And_Excessive_Rows()
    {
        Should.Throw<UserFriendlyException>(()=>new ReceiptSpreadsheet().Read(Workbook(s=>s.Cell(1,1).Value="Wrong")));
        Should.Throw<UserFriendlyException>(()=>new ReceiptSpreadsheet().Read(Workbook(s=>s.Cell(5002,1).Value="ABC")));
        new ReceiptSpreadsheet().Read(Workbook(s=>s.Cell(2,8).Value="Outside template")).Errors.Single().Row.ShouldBe(2);
    }
    [Fact]
    public void Template_Has_Empty_Import_Sheet_And_Text_Sku_Catalog()
    {
        var bytes=new ReceiptSpreadsheet().Template([new("00123","Ватные валики","шт; упаковка (100 шт)")]);
        using var book=new XLWorkbook(new MemoryStream(bytes));book.Worksheet(1).LastRowUsed()!.RowNumber().ShouldBe(1);
        book.Worksheet("Номенклатура").Cell(2,1).GetString().ShouldBe("00123");
        new ReceiptSpreadsheet().Read(bytes).Errors.Single().Message.ShouldContain("не содержит");
    }
    [Fact]
    public void Pdf_Handles_Long_Document_Metadata_And_Multiple_Pages()
    {
        var table=new ReportTable{Description=string.Join("\n",Enumerable.Repeat("Длинный комментарий к складскому документу, партия и получатель.",70)),Columns=[new("Товар","String"),new("Количество","Decimal")],Rows=[["Ватные валики",123.5m]]};
        var bytes=new ReportDocumentRenderer().Render(table,"Приход REC-000001 · Черновик","pdf");
        System.Text.Encoding.ASCII.GetString(bytes,0,4).ShouldBe("%PDF");bytes.Length.ShouldBeGreaterThan(1000);
    }
}
