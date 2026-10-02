using System;
using System.Collections.Generic;
using Dental.Permissions;
namespace Dental.Reports;
public record ReportDefinition(string Code, string Name, string Permission)
{
    public string[] Groupings => ReportCatalog.Groupings(Code);
}
public static class ReportCatalog
{
    public static string[] Groupings(string code) => code switch
    {
        "revenue" => ["default", "day", "doctor", "service", "category", "branch", "detail"],
        "appointments_summary" => ["default", "day", "doctor", "branch", "detail"],
        "utilization" => ["default", "doctor", "chair", "heatmap", "detail"],
        "consumption" => ["default", "doctor", "service", "detail"],
        "purchases" => ["default", "supplier", "month", "detail"],
        "pnl" => ["default", "branch", "category", "detail"],
        "stock_on_date" or "stock_movement" or "abc_analysis" or "expiring" => ["default", "detail"],
        "doctor_performance" or "discounts" or "patients_flow" or "patient_debts" or "advances" or "service_margin" or "cash_shifts" or "writeoffs" or "inventory_discrepancies" or "in_transit" or "payroll" or "cancellations" or "lost_patients" or "branches_comparison" => ["default", "detail"],
        _ => ["default"]
    };
    public static readonly ReportDefinition DailySummary = new("daily_summary","Ежедневная сводка владельца",DentalPermissions.Reports.Network);
    public static ReportDefinition? Find(string code) => code == DailySummary.Code ? DailySummary : System.Linq.Enumerable.SingleOrDefault(All,r=>r.Code==code);
    public static readonly ReportDefinition[] All = [
        new("appointments_summary","Записи по дням и статусам",DentalPermissions.Reports.Branch),new("cancellations","Отмены и переносы",DentalPermissions.Reports.Branch),new("utilization","Загрузка врачей",DentalPermissions.Reports.Branch),new("patients_flow","Поток пациентов",DentalPermissions.Reports.Branch),new("lost_patients","Пациенты без повторного визита",DentalPermissions.Reports.Branch),
        new("revenue","Выручка и поступления",DentalPermissions.Reports.Finance),new("payments_by_method","Способы оплаты и возвраты",DentalPermissions.Reports.Finance),new("patient_debts","Задолженность пациентов",DentalPermissions.Reports.Finance),new("advances","Авансы пациентов",DentalPermissions.Reports.Finance),new("cash_shifts","Кассовые смены",DentalPermissions.Reports.Finance),new("discounts","Скидки",DentalPermissions.Reports.Finance),new("service_margin","Маржинальность услуг",DentalPermissions.Reports.Finance),new("pnl","Доходы и расходы",DentalPermissions.Reports.Network),new("doctor_performance","Показатели врачей",DentalPermissions.Reports.Branch),
        new("stock_on_date","Остатки на дату",DentalPermissions.Inventory.View),new("stock_movement","Оборотная ведомость",DentalPermissions.Inventory.View),new("consumption","Расход материалов и нормы",DentalPermissions.Inventory.View),new("writeoffs","Списания",DentalPermissions.Inventory.View),new("expiring","Сроки годности",DentalPermissions.Inventory.View),new("inventory_discrepancies","Расхождения инвентаризаций",DentalPermissions.Inventory.View),new("abc_analysis","ABC-анализ материалов",DentalPermissions.Inventory.View),new("in_transit","Товары в пути",DentalPermissions.Inventory.View),new("purchases","Закупки",DentalPermissions.Inventory.View),
        new("branches_comparison","Сравнение филиалов",DentalPermissions.Reports.Network),new("payroll","Начисления зарплаты",DentalPermissions.Reports.Payroll)
    ];
}
public record ReportColumn(string Name, string Type);
public class ReportTable
{
    public List<ReportColumn> Columns { get; set; } = [];
    public List<List<object?>> Rows { get; set; } = [];
    public bool Truncated { get; set; }
    public List<string?> DrillKeys { get; set; } = [];
    public List<string?> Links { get; set; } = [];
    public string Timezone { get; set; } = "UTC";
}
public record ReportQuery(string Code, Guid TenantId, Guid[] BranchIds, Guid? DoctorId, bool IncludeCentral, DateTime FromUtc, DateTime ToUtc, string Timezone, int InactiveMonths, string GroupBy = "default", string? DetailKey = null, string? DetailGroup = null);
