using System;

namespace Dental.Reports;

public partial class SqlReportStore
{
    // Only catalogued expressions are interpolated. User filters are database parameters.
    private const string ExtendedCommon = """
        , services AS (SELECT * FROM "AppServices" WHERE "TenantId"=@tenant AND NOT "IsDeleted"),
        categories AS (SELECT * FROM "AppServiceCategories" WHERE "TenantId"=@tenant AND NOT "IsDeleted"),
        chairs AS (SELECT * FROM "AppChairs" WHERE "TenantId"=@tenant AND NOT "IsDeleted" AND "BranchId"=ANY(@branches)),
        branch_work AS (
          SELECT b."Id" branch,dates.local_date,tsrange((dates.local_date+(h->>'open')::time) AT TIME ZONE @tz AT TIME ZONE 'UTC',(dates.local_date+(h->>'close')::time) AT TIME ZONE @tz AT TIME ZONE 'UTC','[)') span
          FROM b CROSS JOIN dates CROSS JOIN LATERAL jsonb_array_elements(b."WorkingHours") h
          WHERE (h->>'dayOfWeek')::int=extract(dow FROM dates.local_date) AND (h->>'isWorking')::bool
        ),
        chair_capacity AS (SELECT chairs."Id" chair,branch_work.branch,local_date,extract(epoch FROM upper(span)-lower(span))/60 minutes FROM chairs JOIN branch_work ON branch_work.branch=chairs."BranchId" WHERE chairs."IsActive"),
        chair_occupied AS (SELECT "ChairId" chair,"BranchId" branch,("StartsAt" AT TIME ZONE 'UTC' AT TIME ZONE @tz)::date local_date,sum(extract(epoch FROM "EndsAt"-"StartsAt"))/60 minutes FROM a WHERE "ChairId" IS NOT NULL AND "Status" NOT IN(5,6) GROUP BY 1,2,3),
        margin_items AS (
          SELECT vi.*,coalesce((SELECT sum("Cost") FROM vm WHERE "VisitItemId"=vi."Id"),0)+
            coalesce((SELECT sum("Cost") FROM vm WHERE "VisitId"=vi."VisitId" AND "VisitItemId" IS NULL),0)*
            CASE WHEN v."Total">0 THEN vi."Total"::numeric/v."Total" ELSE 1.0/greatest((SELECT count(*) FROM "AppVisitItems" z WHERE z."TenantId"=@tenant AND NOT z."IsDeleted" AND z."VisitId"=vi."VisitId"),1) END material_cost
          FROM vi JOIN v ON v."Id"=vi."VisitId"
        )
        """;

    private static (string Key, string Label) RevenueDimension(string group) => group switch
    {
        "doctor" => ("vi.\"DoctorId\"::text", "d.\"FullName\""),
        "service" => ("vi.\"ServiceId\"::text", "s.\"Name\""),
        "category" => ("s.\"CategoryId\"::text", "coalesce(c.\"Name\",'Без категории')"),
        "branch" => ("v.\"BranchId\"::text", "b.\"Name\""),
        _ => ("(v.\"ClosedAt\" AT TIME ZONE 'UTC' AT TIME ZONE @tz)::date::text", "(v.\"ClosedAt\" AT TIME ZONE 'UTC' AT TIME ZONE @tz)::date::text")
    };

    private static string Revenue(string group, bool doctorScoped)
    {
        var (key,label)=RevenueDimension(group);
        // Payments for an advance have no doctor/service/category; preserve them as a separate group.
        var payKey=group switch { "doctor"=>"pi.\"DoctorId\"::text", "service"=>"pi.\"ServiceId\"::text", "category"=>"ps.\"CategoryId\"::text", "branch"=>"pay.\"BranchId\"::text", _=>"(pay.\"CreationTime\" AT TIME ZONE 'UTC' AT TIME ZONE @tz)::date::text" };
        var payLabel=group switch { "doctor"=>"pd.\"FullName\"", "service"=>"ps.\"Name\"", "category"=>"pc.\"Name\"", "branch"=>"pb.\"Name\"", _=>payKey };
        var allocated=doctorScoped || group is "doctor" or "service" or "category";
        var ratio=allocated?"CASE WHEN pv.\"Total\">0 THEN pi.\"Total\"::numeric/pv.\"Total\" WHEN pi.\"Id\" IS NOT NULL THEN 1.0/greatest((SELECT count(*) FROM \"AppVisitItems\" z WHERE z.\"TenantId\"=@tenant AND NOT z.\"IsDeleted\" AND z.\"VisitId\"=pv.\"Id\"),1) ELSE 1 END":"1";
        var join=allocated?"LEFT JOIN \"AppVisitItems\" pi ON pi.\"VisitId\"=pv.\"Id\" AND pi.\"TenantId\"=@tenant AND NOT pi.\"IsDeleted\" LEFT JOIN services ps ON ps.\"Id\"=pi.\"ServiceId\" LEFT JOIN categories pc ON pc.\"Id\"=ps.\"CategoryId\" LEFT JOIN d pd ON pd.\"Id\"=pi.\"DoctorId\"":"";
        return $$"""
            SELECT label AS "Группа",round(sum(revenue)/100,2) AS "Выручка, ₸",round(sum(received)/100,2) AS "Поступления, ₸",count(DISTINCT visit_id) AS "Визиты",round(sum(revenue)/100/nullif(count(DISTINCT visit_id),0),2) AS "Средний чек, ₸",key AS "_key"
            FROM (
              SELECT {{key}} key,{{label}} label,vi."Total"::numeric revenue,0::numeric received,v."Id" visit_id FROM vi JOIN v ON v."Id"=vi."VisitId" JOIN services s ON s."Id"=vi."ServiceId" LEFT JOIN categories c ON c."Id"=s."CategoryId" JOIN d ON d."Id"=vi."DoctorId" JOIN b ON b."Id"=v."BranchId"
              UNION ALL
              SELECT coalesce({{payKey}},'unassigned'),coalesce({{payLabel}},'Авансы / без распределения'),0,(CASE WHEN pay."Type"=1 THEN -pay."Amount" ELSE pay."Amount" END)*{{ratio}},NULL::uuid
              FROM pay LEFT JOIN "AppVisits" pv ON pv."Id"=pay."VisitId" AND pv."TenantId"=@tenant AND NOT pv."IsDeleted" AND pv."BranchId"=ANY(@branches) JOIN b pb ON pb."Id"=pay."BranchId" {{join}}
              WHERE @doctor IS NULL {{(allocated?"OR pi.\"DoctorId\"=@doctor":"OR pv.\"DoctorId\"=@doctor")}}
            ) z GROUP BY key,label ORDER BY label
            """;
    }

    private static string RevenueDetail(ReportQuery q)
    {
        var (key,_)=RevenueDimension(q.DetailGroup??"day");
        return $$"""
            SELECT v."ClosedAt" AS "Закрыт",b."Name" AS "Филиал",concat_ws(' ',p."LastName",p."FirstName",p."MiddleName") AS "Пациент",d."FullName" AS "Врач",s."Name" AS "Услуга",vi."Qty" AS "Количество",vi."Total"/100.0 AS "Выручка, ₸",'/Visits/Detail?id='||v."Id" AS "_link"
            FROM vi JOIN v ON v."Id"=vi."VisitId" JOIN services s ON s."Id"=vi."ServiceId" LEFT JOIN categories c ON c."Id"=s."CategoryId" JOIN d ON d."Id"=vi."DoctorId" JOIN b ON b."Id"=v."BranchId" JOIN p ON p."Id"=v."PatientId"
            WHERE @key IS NULL OR {{key}}=@key ORDER BY v."ClosedAt",s."Name"
            """;
    }

    private static string? ExtendedQuery(ReportQuery q)
    {
        if(q.GroupBy=="detail") return Detail(q);
        return q.Code switch
        {
            "revenue" => Revenue(q.GroupBy, q.DoctorId.HasValue),
            "appointments_summary" when q.GroupBy is "day" or "doctor" or "branch" => AppointmentSummary(q.GroupBy),
            "utilization" when q.GroupBy=="chair" => """
                SELECT chairs."Name" AS "Кресло",b."Name" AS "Филиал",coalesce(c.local_date,o.local_date) AS "Дата",round(coalesce(o.minutes,0),1) AS "Занято, мин",round(coalesce(c.minutes,0),1) AS "Рабочее время, мин",round(100*coalesce(o.minutes,0)/nullif(c.minutes,0),2) AS "Загрузка, %",chairs."Id"::text AS "_key"
                FROM chair_capacity c FULL JOIN chair_occupied o ON o.chair=c.chair AND o.local_date=c.local_date JOIN chairs ON chairs."Id"=coalesce(c.chair,o.chair) JOIN b ON b."Id"=chairs."BranchId" ORDER BY 3,1
                """,
            "utilization" when q.GroupBy=="heatmap" => Heatmap,
            "utilization" => Query("utilization"),
            "consumption" when q.GroupBy=="service" => """
                SELECT d."FullName" AS "Врач",coalesce(s."Name",'Без привязки к услуге') AS "Услуга",i."Name" AS "Материал",sum(vm."Quantity") AS "Факт",sum(vm."NormQuantity") AS "Норма",sum(vm."Quantity"-vm."NormQuantity") AS "Отклонение",sum(vm."Quantity")/nullif(sum(vi."Qty"),0) AS "На единицу услуги",sum(vm."NormQuantity")/nullif(sum(vi."Qty"),0) AS "Норма на единицу",sum(vm."Cost")/100.0 AS "Стоимость, ₸",coalesce(s."Id"::text,'unassigned') AS "_key"
                FROM vm JOIN v ON v."Id"=vm."VisitId" LEFT JOIN vi ON vi."Id"=vm."VisitItemId" LEFT JOIN services s ON s."Id"=vi."ServiceId" JOIN d ON d."Id"=coalesce(vi."DoctorId",v."DoctorId") JOIN i ON i."Id"=vm."ItemId" WHERE @doctor IS NULL OR d."Id"=@doctor GROUP BY d."FullName",s."Name",i."Name",s."Id" ORDER BY 1,2,3
                """,
            "consumption" => Query("consumption"),
            "purchases" when q.GroupBy=="month" => """
                SELECT date_trunc('month',sd."PostedAt" AT TIME ZONE 'UTC' AT TIME ZONE @tz)::date AS "Месяц",s."Name" AS "Поставщик",i."Name" AS "Товар",sum(sl."Qty") AS "Количество",sum(sl."TotalCost")/100.0 AS "Стоимость, ₸",round(sum(sl."TotalCost")/100.0/nullif(sum(sl."Qty"),0),2) AS "Средняя цена, ₸",min(sl."UnitCost")/100.0 AS "Мин. цена, ₸",max(sl."UnitCost")/100.0 AS "Макс. цена, ₸",s."Id"::text AS "_key"
                FROM sd JOIN sl ON sl."DocumentId"=sd."Id" JOIN i ON i."Id"=sl."ItemId" JOIN "AppSuppliers" s ON s."Id"=sd."SupplierId" AND s."TenantId"=@tenant WHERE sd."Type"=0 AND sd."Status"=2 AND sd."PostedAt">=@from AND sd."PostedAt"<@to GROUP BY 1,2,3,s."Id" ORDER BY 1,2,3
                """,
            "purchases" => Query("purchases"),
            "pnl" when q.GroupBy=="category" => """
                SELECT b."Name" AS "Филиал",c."Name" AS "Категория расхода",sum(e."Amount")/100.0 AS "Расход, ₸",c."Id"::text AS "_key" FROM "AppExpenses" e JOIN b ON b."Id"=e."BranchId" JOIN "AppExpenseCategories" c ON c."Id"=e."CategoryId" AND c."TenantId"=@tenant AND NOT c."IsDeleted" WHERE e."TenantId"=@tenant AND NOT e."IsDeleted" AND c."Type"<>2 AND e."PaidAt">=@from AND e."PaidAt"<@to GROUP BY 1,2,c."Id" ORDER BY 1,2
                """,
            "pnl" => """
                SELECT b."Name" AS "Филиал",coalesce(x.rev,0)/100.0 AS "Выручка, ₸",coalesce(x.mat,0)/100.0 AS "Материалы, ₸",coalesce(ex.amount,0)/100.0 AS "Расходы без зарплаты, ₸",round(coalesce(pr.amount,0)/100,2) AS "Зарплата, ₸",round((coalesce(x.rev,0)-coalesce(x.mat,0)-coalesce(ex.amount,0)-coalesce(pr.amount,0))/100,2) AS "Результат, ₸"
                FROM b LEFT JOIN (SELECT v."BranchId",sum(v."Total") rev,sum(coalesce(mc.cost,0)) mat FROM v LEFT JOIN (SELECT "VisitId",sum("Cost") cost FROM vm GROUP BY 1) mc ON mc."VisitId"=v."Id" GROUP BY 1) x ON x."BranchId"=b."Id"
                LEFT JOIN (SELECT e."BranchId",sum(e."Amount") amount FROM "AppExpenses" e JOIN "AppExpenseCategories" c ON c."Id"=e."CategoryId" AND c."TenantId"=@tenant WHERE e."TenantId"=@tenant AND NOT e."IsDeleted" AND c."Type"<>2 AND e."PaidAt">=@from AND e."PaidAt"<@to GROUP BY 1) ex ON ex."BranchId"=b."Id"
                LEFT JOIN (SELECT pp."BranchId",sum(pe."Total"::numeric*(least(pp."PeriodEnd"+1,(@to AT TIME ZONE 'UTC' AT TIME ZONE @tz)::date)-greatest(pp."PeriodStart",(@from AT TIME ZONE 'UTC' AT TIME ZONE @tz)::date))/(pp."PeriodEnd"-pp."PeriodStart"+1)) amount FROM pe JOIN pp ON pp."Id"=pe."PeriodId" GROUP BY 1) pr ON pr."BranchId"=b."Id"
                """,
            "service_margin" => """
                SELECT s."Name" AS "Услуга",sum(mi."Qty") AS "Количество",sum(mi."Total")/100.0 AS "Выручка, ₸",round(sum(mi.material_cost)/100,2) AS "Материалы, ₸",round((sum(mi."Total")-sum(mi.material_cost))/100,2) AS "Маржа, ₸",round(100*(sum(mi."Total")-sum(mi.material_cost))/nullif(sum(mi."Total"),0),2) AS "Маржа, %",s."Id"::text AS "_key" FROM margin_items mi JOIN services s ON s."Id"=mi."ServiceId" GROUP BY s."Id",s."Name" ORDER BY 1
                """,
            "stock_movement" => """
                SELECT w."Name" AS "Склад",i."Name" AS "Товар",coalesce(sum(mov."Qty") FILTER(WHERE mov."MovedAt"<@from),0) AS "На начало",
                coalesce(sum(mov."Qty") FILTER(WHERE mov."MovedAt">=@from AND mov."DocumentType"=0),0) AS "Приходы, нетто",
                coalesce(-sum(mov."Qty") FILTER(WHERE mov."MovedAt">=@from AND mov."DocumentType"=5),0) AS "Визиты",
                coalesce(-sum(mov."Qty") FILTER(WHERE mov."MovedAt">=@from AND mov."DocumentType"=2),0) AS "Списания",
                coalesce(sum(mov."Qty") FILTER(WHERE mov."MovedAt">=@from AND mov."DocumentType"=1 AND mov."Qty">0),0) AS "Перемещения приход",
                coalesce(-sum(mov."Qty") FILTER(WHERE mov."MovedAt">=@from AND mov."DocumentType"=1 AND mov."Qty"<0),0) AS "Перемещения расход",
                coalesce(-sum(mov."Qty") FILTER(WHERE mov."MovedAt">=@from AND mov."DocumentType"=4),0) AS "Возвраты поставщику",
                coalesce(sum(mov."Qty") FILTER(WHERE mov."MovedAt">=@from AND mov."DocumentType"=3),0) AS "Инвентаризации",
                sum(mov."Qty") AS "На конец",round(coalesce(sum(mov."Qty"*mov."UnitCost") FILTER(WHERE mov."MovedAt"<@from),0)/100,2) AS "На начало, ₸",round(sum(mov."Qty"*mov."UnitCost")/100,2) AS "На конец, ₸"
                FROM mov JOIN w ON w."Id"=mov."WarehouseId" JOIN i ON i."Id"=mov."ItemId" GROUP BY 1,2 ORDER BY 1,2
                """,
            "doctor_performance" => DoctorPerformance,
            "branches_comparison" => BranchComparison,
            "patients_flow" => """
                SELECT coalesce(s."Name",'Не указан') AS "Источник",count(DISTINCT v."PatientId") AS "Пациенты",count(DISTINCT v."PatientId") FILTER(WHERE NOT EXISTS(SELECT 1 FROM v_all older WHERE older."PatientId"=v."PatientId" AND older."ClosedAt"<@from)) AS "Новые",count(DISTINCT v."PatientId") FILTER(WHERE EXISTS(SELECT 1 FROM v_all older WHERE older."PatientId"=v."PatientId" AND older."ClosedAt"<@from)) AS "Повторные" FROM v JOIN p ON p."Id"=v."PatientId" LEFT JOIN "AppLeadSources" s ON s."Id"=p."SourceId" AND s."TenantId"=@tenant GROUP BY 1
                """,
            "cancellations" => """
                SELECT action AS "Действие",reason AS "Причина",count(*) AS "Количество" FROM (
                SELECT 'Отмена' action,coalesce(r."Name",a."CancelComment",'Без причины') reason FROM a LEFT JOIN "AppCancelReasons" r ON r."Id"=a."CancelReasonId" AND r."TenantId"=@tenant WHERE a."Status"=5
                UNION ALL SELECT 'Перенос',coalesce(r."Name",'Без причины') FROM a LEFT JOIN "AppCancelReasons" r ON r."Id"=a."MoveReasonId" AND r."TenantId"=@tenant WHERE a."MoveReasonId" IS NOT NULL) z GROUP BY 1,2 ORDER BY 1,2
                """,
            "cash_shifts" => """
                SELECT r."Name" AS "Касса",s."OpenedAt" AS "Открыта",s."ClosedAt" AS "Закрыта",s."OpeningBalance"/100.0 AS "На начало, ₸",s."ClosingBalanceExpected"/100.0 AS "Ожидалось, ₸",s."ClosingBalanceActual"/100.0 AS "Факт, ₸",s."Difference"/100.0 AS "Разница, ₸",coalesce((SELECT sum(o."Amount") FROM "AppCashOperations" o WHERE o."TenantId"=@tenant AND NOT o."IsDeleted" AND o."CashShiftId"=s."Id" AND o."Type"=0),0)/100.0 AS "Инкассации, ₸",'/Cash?shiftId='||s."Id" AS "_link" FROM "AppCashShifts" s JOIN "AppCashRegisters" r ON r."Id"=s."CashRegisterId" AND r."TenantId"=@tenant WHERE s."TenantId"=@tenant AND NOT s."IsDeleted" AND s."BranchId"=ANY(@branches) AND s."OpenedAt">=@from AND s."OpenedAt"<@to ORDER BY s."OpenedAt"
                """,
            "writeoffs" => """
                SELECT coalesce(r."Name",'Без причины') AS "Причина",coalesce(d."FullName",'Сотрудник не указан') AS "Сотрудник",i."Name" AS "Товар",sum(sl."Qty") AS "Количество",sum(sl."TotalCost")/100.0 AS "Стоимость, ₸" FROM sd JOIN sl ON sl."DocumentId"=sd."Id" JOIN i ON i."Id"=sl."ItemId" LEFT JOIN "AppWriteoffReasons" r ON r."Id"=sd."ReasonId" AND r."TenantId"=@tenant LEFT JOIN d ON d."UserId"=sd."PostedBy" WHERE sd."Type"=2 AND sd."Status"=2 AND sd."PostedAt">=@from AND sd."PostedAt"<@to GROUP BY 1,2,3 ORDER BY 1,2,3
                """,
            "discounts" => """
                SELECT d."FullName" AS "Врач",coalesce(e."FullName",'Сотрудник не указан') AS "Сотрудник",sum(vi."DiscountAmount")/100.0 AS "Скидки, ₸",max(vi."DiscountPct") AS "Максимум, %",count(*) AS "Строки услуг",count(*) FILTER(WHERE EXISTS(SELECT 1 FROM "AppApprovalRequests" ar WHERE ar."TenantId"=@tenant AND ar."EntityId"=v."Id" AND ar."Type"='Discount')) AS "Требовали подтверждения",d."Id"::text AS "_key" FROM vi JOIN v ON v."Id"=vi."VisitId" JOIN d ON d."Id"=vi."DoctorId" LEFT JOIN d e ON e."UserId"=coalesce(vi."LastModifierId",vi."CreatorId") WHERE vi."DiscountAmount">0 GROUP BY d."Id",d."FullName",e."FullName" ORDER BY 1,2
                """,
            _ => null
        };
    }

    private static string AppointmentSummary(string group)
    {
        var key=group switch { "doctor"=>"a.\"DoctorId\"::text", "branch"=>"a.\"BranchId\"::text", _=>"(a.\"StartsAt\" AT TIME ZONE 'UTC' AT TIME ZONE @tz)::date::text" };
        var label=group switch { "doctor"=>"d.\"FullName\"", "branch"=>"b.\"Name\"", _=>key };
        return $$"""
            SELECT {{label}} AS "Группа",count(*) AS "Записи",count(*) FILTER(WHERE a."Status"=0) AS "Не подтверждены",count(*) FILTER(WHERE a."Status"=1) AS "Подтверждены",count(*) FILTER(WHERE a."Status" IN(2,3)) AS "В клинике",count(*) FILTER(WHERE a."Status"=4) AS "Завершены",count(*) FILTER(WHERE a."Status"=5) AS "Отмены",count(*) FILTER(WHERE a."Status"=6) AS "Неявки",round(100.0*count(*) FILTER(WHERE a."Status"=6)/greatest(count(*),1),2) AS "Неявки, %",round(100.0*count(*) FILTER(WHERE a."Status"=5)/greatest(count(*),1),2) AS "Отмены, %",{{key}} AS "_key" FROM a JOIN d ON d."Id"=a."DoctorId" JOIN b ON b."Id"=a."BranchId" GROUP BY {{key}},{{label}} ORDER BY 1
            """;
    }

    private const string Heatmap = """
        SELECT ((extract(isodow FROM dates.local_date))::int) AS "День недели",hour AS "Час",round(sum(coalesce(o.minutes,0)),1) AS "Занято, мин",round(sum(coalesce(c.minutes,0)),1) AS "По графику, мин",round(100*sum(coalesce(o.minutes,0))/nullif(sum(coalesce(c.minutes,0)),0),2) AS "Загрузка, %"
        FROM dates CROSS JOIN generate_series(0,23) hour
        CROSS JOIN LATERAL (SELECT tsrange((dates.local_date+make_time(hour,0,0)) AT TIME ZONE @tz AT TIME ZONE 'UTC',(dates.local_date+make_time(hour,0,0)+interval '1 hour') AT TIME ZONE @tz AT TIME ZONE 'UTC','[)') span) bucket
        LEFT JOIN LATERAL (SELECT sum(extract(epoch FROM upper(span*bucket.span)-lower(span*bucket.span)))/60 minutes FROM work_ranges CROSS JOIN LATERAL unnest(spans) span WHERE work_ranges.local_date=dates.local_date AND span && bucket.span) c ON true
        LEFT JOIN LATERAL (SELECT sum(extract(epoch FROM least(a."EndsAt",upper(bucket.span))-greatest(a."StartsAt",lower(bucket.span))))/60 minutes FROM a WHERE a."Status" NOT IN(5,6) AND a."StartsAt"<upper(bucket.span) AND a."EndsAt">lower(bucket.span)) o ON true
        GROUP BY 1,2 HAVING sum(coalesce(c.minutes,0))+sum(coalesce(o.minutes,0))>0 ORDER BY 1,2
        """;

    private const string DoctorPerformance = """
        SELECT d."FullName" AS "Врач",coalesce(x.visits,0) AS "Визиты",coalesce(x.patients,0) AS "Пациенты",coalesce(x.revenue,0)/100.0 AS "Выручка, ₸",round(coalesce(x.revenue,0)/100.0/nullif(x.visits,0),2) AS "Средний чек, ₸",round(100*coalesce(o.minutes,0)/nullif(c.minutes,0),2) AS "Загрузка, %",round(100.0*coalesce(ac.no_show,0)/nullif(ac.total,0),2) AS "Неявки, %",d."Id"::text AS "_key"
        FROM d LEFT JOIN (SELECT vi."DoctorId" doctor,count(DISTINCT vi."VisitId") visits,count(DISTINCT v."PatientId") patients,sum(vi."Total") revenue FROM vi JOIN v ON v."Id"=vi."VisitId" GROUP BY 1) x ON x.doctor=d."Id"
        LEFT JOIN (SELECT doctor,sum(minutes) minutes FROM capacity GROUP BY 1) c ON c.doctor=d."Id" LEFT JOIN (SELECT doctor,sum(minutes) minutes FROM occupied GROUP BY 1) o ON o.doctor=d."Id"
        LEFT JOIN (SELECT "DoctorId" doctor,count(*) total,count(*) FILTER(WHERE "Status"=6) no_show FROM a GROUP BY 1) ac ON ac.doctor=d."Id"
        WHERE (x.doctor IS NOT NULL OR c.doctor IS NOT NULL OR ac.doctor IS NOT NULL) AND (@doctor IS NULL OR d."Id"=@doctor) ORDER BY 1
        """;

    private const string BranchComparison = """
        SELECT b."Name" AS "Филиал",coalesce(x.revenue,0)/100.0 AS "Выручка, ₸",coalesce(x.visits,0) AS "Визиты",coalesce(x.patients,0) AS "Пациенты",round(coalesce(x.revenue,0)/100.0/nullif(x.visits,0),2) AS "Средний чек, ₸",round(100*coalesce(o.minutes,0)/nullif(c.minutes,0),2) AS "Загрузка, %",coalesce(ac.no_show,0) AS "Неявки",round(100.0*coalesce(ac.no_show,0)/nullif(ac.total,0),2) AS "Неявки, %",coalesce(mc.cost,0)/100.0 AS "Материалы, ₸",(coalesce(x.revenue,0)-coalesce(mc.cost,0))/100.0 AS "Маржа, ₸",round(100.0*(coalesce(x.revenue,0)-coalesce(mc.cost,0))/nullif(x.revenue,0),2) AS "Маржа, %",round(coalesce(mc.cost,0)*1000.0/nullif(x.revenue,0),2) AS "Материалы на 1000 ₸",b."Id"::text AS "_key"
        FROM b LEFT JOIN (SELECT "BranchId",sum("Total") revenue,count(*) visits,count(DISTINCT "PatientId") patients FROM v GROUP BY 1) x ON x."BranchId"=b."Id"
        LEFT JOIN (SELECT branch,sum(minutes) minutes FROM capacity GROUP BY 1) c ON c.branch=b."Id" LEFT JOIN (SELECT branch,sum(minutes) minutes FROM occupied GROUP BY 1) o ON o.branch=b."Id"
        LEFT JOIN (SELECT "BranchId",count(*) total,count(*) FILTER(WHERE "Status"=6) no_show FROM a GROUP BY 1) ac ON ac."BranchId"=b."Id" LEFT JOIN (SELECT v."BranchId",sum(vm."Cost") cost FROM vm JOIN v ON v."Id"=vm."VisitId" GROUP BY 1) mc ON mc."BranchId"=b."Id" ORDER BY 1
        """;

    private static string Detail(ReportQuery q)
    {
        if(q.Code is "revenue" or "service_margin" or "doctor_performance" or "discounts" or "branches_comparison")
            return RevenueDetail(q with {DetailGroup=q.DetailGroup is null or "default" ? (q.Code=="service_margin"?"service":q.Code=="branches_comparison"?"branch":q.Code=="revenue"?"day":"doctor") : q.DetailGroup});
        if(q.Code is "appointments_summary" or "utilization" or "cancellations")
        {
            var key=q.DetailGroup switch { "chair"=>"a.\"ChairId\"::text", "branch"=>"a.\"BranchId\"::text", "day"=>"(a.\"StartsAt\" AT TIME ZONE 'UTC' AT TIME ZONE @tz)::date::text", _=>"a.\"DoctorId\"::text" };
            return $$"""
                SELECT a."StartsAt" AS "Начало",a."EndsAt" AS "Конец",b."Name" AS "Филиал",d."FullName" AS "Врач",chairs."Name" AS "Кресло",concat_ws(' ',p."LastName",p."FirstName") AS "Пациент",CASE a."Status" WHEN 0 THEN 'Запланирована' WHEN 1 THEN 'Подтверждена' WHEN 2 THEN 'Пришёл' WHEN 3 THEN 'В кресле' WHEN 4 THEN 'Завершена' WHEN 5 THEN 'Отмена' ELSE 'Неявка' END AS "Статус",cr."Name" AS "Причина отмены",mr."Name" AS "Причина переноса",'/Schedule?date='||(a."StartsAt" AT TIME ZONE 'UTC' AT TIME ZONE @tz)::date||'&branchId='||a."BranchId" AS "_link"
                FROM a JOIN b ON b."Id"=a."BranchId" JOIN d ON d."Id"=a."DoctorId" JOIN p ON p."Id"=a."PatientId" LEFT JOIN chairs ON chairs."Id"=a."ChairId" LEFT JOIN "AppCancelReasons" cr ON cr."Id"=a."CancelReasonId" AND cr."TenantId"=@tenant LEFT JOIN "AppCancelReasons" mr ON mr."Id"=a."MoveReasonId" AND mr."TenantId"=@tenant WHERE (@key IS NULL OR {{key}}=@key) {{(q.Code=="cancellations"?"AND (a.\"Status\"=5 OR a.\"MoveReasonId\" IS NOT NULL)":"")}} ORDER BY a."StartsAt"
                """;
        }
        if(q.Code=="consumption") return """
            SELECT v."ClosedAt" AS "Дата",d."FullName" AS "Врач",coalesce(s."Name",'Без привязки к услуге') AS "Услуга",i."Name" AS "Материал",vm."Quantity" AS "Факт",vm."NormQuantity" AS "Норма",vm."Quantity"-vm."NormQuantity" AS "Отклонение",vm."Cost"/100.0 AS "Стоимость, ₸",'/Visits/Detail?id='||v."Id" AS "_link" FROM vm JOIN v ON v."Id"=vm."VisitId" LEFT JOIN vi ON vi."Id"=vm."VisitItemId" LEFT JOIN services s ON s."Id"=vi."ServiceId" JOIN d ON d."Id"=coalesce(vi."DoctorId",v."DoctorId") JOIN i ON i."Id"=vm."ItemId" WHERE (@doctor IS NULL OR d."Id"=@doctor) AND (@key IS NULL OR coalesce(s."Id"::text,'unassigned')=@key) ORDER BY 1,2,3
            """;
        if(q.Code=="advances")return """
            SELECT concat_ws(' ',p."LastName",p."FirstName",p."MiddleName") AS "Пациент",p."Phone" AS "Телефон",pb."Balance"/100.0 AS "Баланс, ₸",'/Patients/Detail?id='||p."Id" AS "_link" FROM "AppPatientBalances" pb JOIN p ON p."Id"=pb."PatientId" WHERE pb."TenantId"=@tenant AND pb."Balance">0 AND (EXISTS(SELECT 1 FROM a_all WHERE "PatientId"=p."Id") OR EXISTS(SELECT 1 FROM v_all WHERE "PatientId"=p."Id")) ORDER BY 1
            """;
        if(q.Code=="lost_patients")return """
            SELECT concat_ws(' ',p."LastName",p."FirstName",p."MiddleName") AS "Пациент",p."Phone" AS "Телефон",max(v_all."ClosedAt") AS "Последний визит",'/Patients/Detail?id='||p."Id" AS "_link" FROM p JOIN v_all ON v_all."PatientId"=p."Id" GROUP BY p."Id",p."LastName",p."FirstName",p."MiddleName",p."Phone" HAVING max(v_all."ClosedAt")<@to-make_interval(months=>@months) AND NOT EXISTS(SELECT 1 FROM a_all x WHERE x."PatientId"=p."Id" AND x."StartsAt">=@to AND x."Status" NOT IN(5,6)) ORDER BY 3
            """;
        if(q.Code=="patients_flow")return """
            SELECT concat_ws(' ',p."LastName",p."FirstName",p."MiddleName") AS "Пациент",min(v."ClosedAt") AS "Первый визит в периоде",CASE WHEN EXISTS(SELECT 1 FROM v_all older WHERE older."PatientId"=p."Id" AND older."ClosedAt"<@from) THEN 'Повторный' ELSE 'Новый' END AS "Тип пациента",'/Patients/Detail?id='||p."Id" AS "_link" FROM v JOIN p ON p."Id"=v."PatientId" GROUP BY p."Id",p."LastName",p."FirstName",p."MiddleName" ORDER BY 1
            """;
        if(q.Code=="patient_debts") return """
            SELECT concat_ws(' ',p."LastName",p."FirstName",p."MiddleName") AS "Пациент",p."Phone" AS "Телефон",v_all."ClosedAt" AS "Визит",greatest(0,v_all."Total"-v_all."PaidTotal")/100.0 AS "Долг по визиту, ₸",'/Visits/Detail?id='||v_all."Id" AS "_link" FROM p JOIN v_all ON v_all."PatientId"=p."Id" WHERE v_all."Total">v_all."PaidTotal" AND (@key IS NULL OR p."Id"::text=@key) ORDER BY 1,3
            """;
        if(q.Code=="payroll") return """
            SELECT d."FullName" AS "Сотрудник",pp."PeriodStart" AS "Начало",pp."PeriodEnd" AS "Конец",pe."Total"/100.0 AS "Итого, ₸",pe."Comment" AS "Комментарий",'/Payroll?periodId='||pp."Id" AS "_link" FROM pe JOIN pp ON pp."Id"=pe."PeriodId" JOIN d ON d."Id"=pe."EmployeeId" ORDER BY 1,2
            """;
        if(q.Code=="cash_shifts")return Query("cash_shifts");
        if(q.Code=="pnl")return """
            SELECT e."PaidAt" AS "Дата",b."Name" AS "Филиал",c."Name" AS "Категория",e."Amount"/100.0 AS "Расход, ₸",e."Counterparty" AS "Получатель",e."Comment" AS "Комментарий",'/Cash' AS "_link" FROM "AppExpenses" e JOIN b ON b."Id"=e."BranchId" JOIN "AppExpenseCategories" c ON c."Id"=e."CategoryId" AND c."TenantId"=@tenant WHERE e."TenantId"=@tenant AND NOT e."IsDeleted" AND e."PaidAt">=@from AND e."PaidAt"<@to AND c."Type"<>2 AND (@key IS NULL OR c."Id"::text=@key) ORDER BY 1
            """;
        if(q.Code is "stock_on_date" or "stock_movement" or "abc_analysis" or "expiring")return """
            SELECT mov."MovedAt" AS "Дата",w."Name" AS "Склад",i."Name" AS "Товар",mov."Qty" AS "Количество",mov."UnitCost"/100.0 AS "Цена, ₸",round(mov."Qty"*mov."UnitCost"/100,2) AS "Сумма, ₸",sd."Number" AS "Документ",'/Inventory/Documents/Edit?id='||sd."Id" AS "_link" FROM mov JOIN w ON w."Id"=mov."WarehouseId" JOIN i ON i."Id"=mov."ItemId" LEFT JOIN sd ON sd."Id"=mov."DocumentId" WHERE mov."MovedAt">=@from ORDER BY 1
            """;
        return $$"""
            SELECT sd."PostedAt" AS "Дата",sd."Number" AS "Документ",i."Name" AS "Товар",sl."Qty" AS "Количество",sl."UnitCost"/100.0 AS "Цена, ₸",sl."TotalCost"/100.0 AS "Стоимость, ₸",sl."ExpectedQty" AS "Ожидалось",sl."ActualQty" AS "Факт",'/Inventory/Documents/Edit?id='||sd."Id" AS "_link" FROM sd JOIN sl ON sl."DocumentId"=sd."Id" JOIN i ON i."Id"=sl."ItemId"
            WHERE {{(q.Code=="in_transit"?"sd.\"Type\"=1 AND sd.\"Status\"=3":$"sd.\"Type\"={(q.Code=="purchases"?0:q.Code=="writeoffs"?2:3)} AND sd.\"Status\"=2 AND sd.\"PostedAt\">=@from AND sd.\"PostedAt\"<@to")}} AND (@key IS NULL OR sd."SupplierId"::text=@key) ORDER BY 1,2
            """;
    }
}
