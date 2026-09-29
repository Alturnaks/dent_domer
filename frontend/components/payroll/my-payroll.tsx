"use client";

import * as React from "react";
import { useQuery } from "@tanstack/react-query";
import { ChevronDown, ChevronRight, Wallet } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { ErrorBlock } from "@/components/admin/common";
import { Skeleton } from "@/components/ui/skeleton";
import { api } from "@/lib/api-client";
import { formatDate, formatMoney } from "@/lib/format";
import { t } from "@/lib/i18n";
import { payrollKeys, periodText, PeriodStatusBadge, schemeText, Stat, VisitLinesTable, type MyPayroll } from "./shared";

/** «Моя зарплата» (payroll.view_own): начисление текущего месяца и утверждённые ведомости с детализацией. */
export function MyPayrollView() {
  const q = useQuery({ queryKey: payrollKeys.me, queryFn: () => api<MyPayroll>("/payroll/me") });
  const [open, setOpen] = React.useState<string | null>(null);
  const [showCurrent, setShowCurrent] = React.useState(false);

  if (q.isError) return <ErrorBlock onRetry={() => q.refetch()} />;
  if (q.isLoading || !q.data) return <Skeleton className="h-64 w-full" />;
  const d = q.data;
  const c = d.currentMonth;
  const showMaterials = c.scheme?.type === "PercentRevenueMinusMaterials";

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <CardTitle>{t("payroll.my.currentMonth")}</CardTitle>
          <CardDescription>
            {t("payroll.my.currentHint", { start: formatDate(c.periodStart), end: formatDate(c.periodEnd) })}{" "}
            {d.onlyPaidVisits ? t("payroll.onlyPaid") : t("payroll.allClosed")}.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="text-sm">
            {t("payroll.period.scheme")}: <span className="font-medium">{schemeText(c.scheme)}</span>
          </div>
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
            <Stat label={t("payroll.period.visits")} value={c.visitsCount} />
            <Stat label={t("payroll.period.shifts")} value={c.shifts} />
            <Stat label={t("payroll.period.revenue")} value={formatMoney(c.baseRevenue)} />
            {showMaterials ? <Stat label={t("payroll.period.materials")} value={formatMoney(c.materialsCost)} /> : null}
            <Stat label={t("payroll.my.accrued")} value={formatMoney(c.accrued)} strong />
          </div>
          {c.visits.length > 0 ? (
            <div>
              <Button variant="ghost" size="sm" onClick={() => setShowCurrent((v) => !v)}>
                {showCurrent ? <ChevronDown /> : <ChevronRight />} {t("payroll.my.visits")} ({c.visits.length})
              </Button>
              {showCurrent ? <VisitLinesTable visits={c.visits} showMaterials={showMaterials} /> : null}
            </div>
          ) : null}
        </CardContent>
      </Card>

      <div>
        <h2 className="mb-2 text-base font-semibold">{t("payroll.my.history")}</h2>
        {d.periods.length === 0 ? (
          <EmptyState icon={<Wallet className="h-8 w-8" />} title={t("payroll.my.historyEmpty")} />
        ) : (
          <div className="space-y-2">
            {d.periods.map((p) => {
              const expanded = open === p.entryId;
              return (
                <Card key={p.entryId}>
                  <button
                    type="button"
                    className="flex w-full flex-wrap items-center gap-3 p-3 text-left hover:bg-muted/40"
                    onClick={() => setOpen(expanded ? null : p.entryId)}
                  >
                    {expanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                    <span className="tabular font-medium">{periodText(p.periodStart, p.periodEnd)}</span>
                    <span className="text-sm text-muted-foreground">{p.branchName}</span>
                    <PeriodStatusBadge status={p.status} />
                    <span className="ml-auto tabular text-base font-semibold">{formatMoney(p.total)}</span>
                  </button>
                  {expanded ? (
                    <CardContent className="space-y-3 border-t pt-3">
                      <div className="text-sm">
                        {t("payroll.period.scheme")}: <span className="font-medium">{schemeText(p.scheme)}</span>
                      </div>
                      <div className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-6">
                        <Stat label={t("payroll.period.visits")} value={p.visitsCount} />
                        <Stat label={t("payroll.period.revenue")} value={formatMoney(p.baseRevenue)} />
                        <Stat label={t("payroll.period.accrued")} value={formatMoney(p.accrued)} />
                        <Stat label={t("payroll.period.bonus")} value={formatMoney(p.bonus)} />
                        <Stat label={t("payroll.period.penalty")} value={formatMoney(p.penalty)} />
                        <Stat label={t("payroll.period.total")} value={formatMoney(p.total)} strong />
                      </div>
                      {p.comment ? (
                        <p className="text-sm">
                          {t("payroll.period.comment")}: {p.comment}
                        </p>
                      ) : null}
                      <VisitLinesTable visits={p.visits} showMaterials={p.scheme?.type === "PercentRevenueMinusMaterials"} />
                    </CardContent>
                  ) : null}
                </Card>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
