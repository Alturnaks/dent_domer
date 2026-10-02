using System;
using System.IO;
using ClosedXML.Excel;
using Dental.Reports;
using Shouldly;
using Xunit;

namespace Dental.EntityFrameworkCore.Applications;
public class ReportExportTests
{
    [Fact]
    public void Excel_Preserves_Typed_Numbers_Clinic_Dates_And_Protects_Formulas()
    {
        var table=new ReportTable{Timezone="Asia/Almaty",Columns=[new("Дата","DateTime"),new("Сумма","Decimal"),new("Пациент","String")],Rows=[[new DateTime(2026,10,1,21,30,0,DateTimeKind.Utc),-12500.25m,"=1+1"]]};
        using var book=new XLWorkbook(new MemoryStream(new ReportDocumentRenderer().Render(table,"Проверка","xlsx")));var sheet=book.Worksheet(1);
        sheet.Cell(4,1).DataType.ShouldBe(XLDataType.DateTime);sheet.Cell(4,1).GetDateTime().ShouldBe(new DateTime(2026,10,2,2,30,0));
        sheet.Cell(4,2).DataType.ShouldBe(XLDataType.Number);sheet.Cell(4,2).GetDouble().ShouldBe(-12500.25);
        sheet.Cell(4,3).HasFormula.ShouldBeFalse();sheet.Cell(4,3).GetString().ShouldBe("=1+1");
    }
    [Fact]
    public void Pdf_Renders_Cyrillic_And_A_Wide_Network_Report()
    {
        var table=new ReportTable{Columns=[new("Филиал","String"),new("Выручка, ₸","Decimal"),new("Пациенты","Int32"),new("Маржа, %","Decimal")],Rows=[["Дентал Плюс — Есиль",12500.25m,7,34.5m]]};
        var bytes=new ReportDocumentRenderer().Render(table,"Сравнение филиалов","pdf");System.Text.Encoding.ASCII.GetString(bytes,0,4).ShouldBe("%PDF");bytes.Length.ShouldBeGreaterThan(1000);
    }
}
