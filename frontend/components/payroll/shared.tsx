"use client";

// Общие типы и элементы раздела «Зарплата». Типы — только из сгенерированной схемы.
import * as React from "react";
import Link from "next/link";
import { Badge } from "@/components/ui/badge";
import { Table, TBody, TD, TFoot, TH, THead, TR } from "@/components/ui/table";
import { useOrgHref } from "@/lib/auth";
import { formatDate, formatMoney, formatPercent } from "@/lib/format";
import { t } from "@/lib/i18n";
import type { Schemas } from "@/lib/types";

export type PayrollScheme = Schemas["PayrollSchemeDto"];
export type PayrollSchemeType = Schemas["PayrollSchemeType"];
export type PayrollSchemeSnapshot = Schemas["PayrollSchemeSnapshot"];
export type PayrollPeriod = Schemas["PayrollPeriodDto"];
export type PayrollPeriodDetail = Schemas["PayrollPeriodDetailDto"];
export type PayrollPeriodStatus = Schemas["PayrollPeriodStatus"];
export type PayrollEntry = Schemas["PayrollEntryDto"];
export type PayrollEntryDetail = Schemas["PayrollEntryDetailDto"];
export type PayrollVisitLine = Schemas["PayrollVisitLine"];
export type MyPayroll = Schemas["MyPayrollDto"];

export const SCHEME_TYPES: PayrollSchemeType[] = ["PercentRevenue", "PercentRevenueMinusMaterials", "FixedPlusPercent", "PerShift"];

export const payrollKeys = {
  periods: (branchId: string | null | undefined) => ["payroll", "periods", branchId ?? "all"] as const,
  period: (id: string) => ["payroll", "period", id] as const,
  entry: (id: string) => ["payroll", "entry", id] as const,
  schemes: ["payroll", "schemes"] as const,
  me: ["payroll", "me"] as const,
};

const STATUS_VARIANT: Record<PayrollPeriodStatus, "warning" | "default" | "success"> = { Draft: "warning", Approved: "default", Paid: "success" };

export function PeriodStatusBadge({ status }: { status: PayrollPeriodStatus }) {
  return <Badge variant={STATUS_VARIANT[status]}>{t(`payroll.status.${status}`)}</Badge>;
}

/** «30% от выручки», «100 000 ₸ + 10% от выручки», «25 000 ₸ за смену». */
export function schemeText(s: PayrollSchemeSnapshot | null | undefined): string {
  if (!s) return t("payroll.noScheme");
  return t(`payroll.schemeShort.${s.type}`, {
    percent: formatPercent(s.percent, 2),
    fixed: formatMoney(s.fixedAmount),
    rate: formatMoney(s.shiftRate),
  });
}

export function periodText(start: string, end: string): string {
  return `${formatDate(start)} — ${formatDate(end)}`;
}

/** Детализация начисления по визитам. */
export function VisitLinesTable({ visits, showMaterials = true }: { visits: PayrollVisitLine[]; showMaterials?: boolean }) {
  const { href } = useOrgHref();
  if (visits.length === 0) return <p className="p-3 text-sm text-muted-foreground">{t("payroll.visits.empty")}</p>;
  const revenue = visits.reduce((s, v) => s + v.revenue, 0);
  const materials = visits.reduce((s, v) => s + v.materialsCost, 0);
  return (
    <div className="overflow-x-auto">
      <Table>
        <THead>
          <TR>
            <TH>{t("payroll.visits.date")}</TH>
            <TH>{t("payroll.visits.patient")}</TH>
            <TH>{t("payroll.visits.services")}</TH>
            <TH className="text-right">{t("payroll.visits.revenue")}</TH>
            {showMaterials ? <TH className="text-right">{t("payroll.visits.materials")}</TH> : null}
            <TH />
          </TR>
        </THead>
        <TBody>
          {visits.map((v) => (
            <TR key={v.visitId}>
              <TD className="tabular whitespace-nowrap">
                <Link className="text-primary hover:underline" href={href(`visits/${v.visitId}`)}>
                  {formatDate(v.date)}
                </Link>
              </TD>
              <TD>{v.patientName}</TD>
              <TD className="max-w-[28rem] text-muted-foreground">{v.services}</TD>
              <TD className="tabular text-right">{formatMoney(v.revenue)}</TD>
              {showMaterials ? <TD className="tabular text-right">{formatMoney(v.materialsCost)}</TD> : null}
              <TD>{v.paid ? null : <Badge variant="warning">{t("payroll.visits.unpaid")}</Badge>}</TD>
            </TR>
          ))}
        </TBody>
        <TFoot>
          <TR>
            <TD colSpan={3} className="font-medium">
              {t("payroll.period.totals")}
            </TD>
            <TD className="tabular text-right font-medium">{formatMoney(revenue)}</TD>
            {showMaterials ? <TD className="tabular text-right font-medium">{formatMoney(materials)}</TD> : null}
            <TD />
          </TR>
        </TFoot>
      </Table>
    </div>
  );
}

/** Первое и последнее число месяца (YYYY-MM-DD), shift = -1 — прошлый месяц. */
export function monthRange(shift = 0): { start: string; end: string } {
  const now = new Date();
  const first = new Date(now.getFullYear(), now.getMonth() + shift, 1);
  const last = new Date(now.getFullYear(), now.getMonth() + shift + 1, 0);
  const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
  return { start: iso(first), end: iso(last) };
}

export function Stat({ label, value, strong }: { label: string; value: React.ReactNode; strong?: boolean }) {
  return (
    <div className={`rounded-md border px-3 py-2 ${strong ? "border-primary/40 bg-primary/5" : ""}`}>
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className={`tabular font-semibold ${strong ? "text-lg" : "text-base"}`}>{value}</div>
    </div>
  );
}
