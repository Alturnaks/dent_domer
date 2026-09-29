"use client";

import * as React from "react";
import Link from "next/link";
import { useQueries, useQuery } from "@tanstack/react-query";
import { BadgeCheck, BarChart3, Banknote, CalendarDays, ChevronRight, Package, Star, UserCog, Users, Wallet } from "lucide-react";
import { PageHeader } from "@/components/ui/empty-state";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { api } from "@/lib/api-client";
import { useAuth, useBranch, useOrgHref } from "@/lib/auth";
import { formatMoney, formatTime } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import { STATUS_COLORS } from "@/lib/status";
import type { Appointment, CalendarResponse, Schemas } from "@/lib/types";

const APPROVAL_PERMS = [P.discountsApply, P.writeoff, P.cashRefund, P.visitsEditClosed, P.purchaseApprove, P.payrollManage];
const CASH_PERMS = [P.cashShift, P.cashPayment, P.cashRefund, P.cashExpense, P.reportsFinance];

/** Сегодняшняя дата (YYYY-MM-DD) в часовом поясе организации. */
function todayIn(timeZone: string): string {
  try {
    return new Intl.DateTimeFormat("en-CA", { timeZone, year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
  } catch {
    return new Date().toISOString().slice(0, 10);
  }
}

export default function DashboardPage() {
  const { me, can } = useAuth();
  const { href } = useOrgHref();
  if (!me) return null;
  const firstName = me.user.fullName.split(" ")[1] ?? me.user.fullName;

  const actions = [
    { key: "schedule", path: "schedule", icon: CalendarDays, show: can(P.scheduleViewAll, P.scheduleViewOwn, P.scheduleManage) },
    { key: "newPatient", path: "patients", icon: Users, show: can(P.patientsView) },
    { key: "cash", path: "cash", icon: Wallet, show: can(P.cashShift, P.cashPayment, P.cashRefund, P.cashExpense) },
    { key: "inventory", path: "inventory", icon: Package, show: can(P.inventoryView) },
    { key: "approvals", path: "approvals", icon: BadgeCheck, show: can(...APPROVAL_PERMS) },
    { key: "reports", path: "reports", icon: BarChart3, show: can(P.reportsBranch, P.reportsFinance, P.reportsNetwork, P.reportsPayroll) },
    { key: "payroll", path: "payroll", icon: Banknote, show: can(P.payrollOwn, P.payrollAll, P.payrollManage) },
    { key: "staff", path: "staff", icon: UserCog, show: can(P.staffManage, P.rolesManage) },
  ].filter((a) => a.show);

  return (
    <div>
      <PageHeader
        title={t("dashboard.welcome", { name: firstName })}
        description={`${t("dashboard.role", { role: me.role.name })} · ${me.organization.name}`}
      />
      {actions.length ? (
        <div className="mb-4 grid grid-cols-2 gap-2 sm:grid-cols-4 xl:grid-cols-8">
          {actions.map((a) => (
            <Link
              key={a.key}
              href={href(a.path)}
              className="flex flex-col items-center gap-1.5 rounded-xl border bg-card p-3 text-center text-sm shadow-sm transition-colors hover:bg-accent"
            >
              <a.icon className="h-5 w-5 text-primary" />
              {t(`home.actions.${a.key}`)}
            </Link>
          ))}
        </div>
      ) : null}
      <div className="grid gap-4 lg:grid-cols-3">
        {can(P.scheduleViewAll, P.scheduleViewOwn, P.scheduleManage) ? (
          <div className="lg:col-span-2">
            <TodayWidget />
          </div>
        ) : null}
        <div className="space-y-4">
          {can(...APPROVAL_PERMS) ? <ApprovalsWidget /> : null}
          {can(...CASH_PERMS) ? <CashWidget /> : null}
          {can(P.inventoryView) ? <LowStockWidget /> : null}
        </div>
      </div>
    </div>
  );
}

function WidgetError() {
  return <p className="text-sm text-muted-foreground">{t("home.widgetError")}</p>;
}

function TodayWidget() {
  const { can, me } = useAuth();
  const { href } = useOrgHref();
  const { branchId, branches, timezone } = useBranch();
  const today = todayIn(timezone);
  const own = !can(P.scheduleViewAll, P.scheduleManage);
  const ids = branchId ? [branchId] : branches.map((b) => b.id);
  const [expanded, setExpanded] = React.useState(false);

  const results = useQueries({
    queries: ids.map((id) => ({
      queryKey: ["calendar", id, "day", today],
      queryFn: () => api<CalendarResponse>("/calendar", { query: { branch_id: id, from: today, to: today, view: "day" } }),
      staleTime: 60_000,
    })),
  });
  const loading = results.some((r) => r.isLoading);
  const failed = results.length > 0 && results.every((r) => r.isError);
  const appts: Appointment[] = results
    .flatMap((r) => r.data?.appointments ?? [])
    .filter((a) => a.status !== "Cancelled")
    .sort((a, b) => a.startsAt.localeCompare(b.startsAt));
  const arrived = appts.filter((a) => a.status === "Arrived" || a.status === "InChair").length;
  const done = appts.filter((a) => a.status === "Completed").length;
  const multiBranch = ids.length > 1;
  const branchName = new Map((me?.branches ?? []).map((b) => [b.id, b.name]));
  const shown = expanded ? appts : appts.slice(0, 12);

  return (
    <Card>
      <CardHeader className="flex-row items-start justify-between space-y-0">
        <div>
          <CardTitle>{own ? t("home.myToday") : t("home.today")}</CardTitle>
          <p className="mt-1 text-xs text-muted-foreground">
            {branchId ? t("home.branchNote", { name: branchName.get(branchId) ?? "" }) : t("home.allBranchesNote")}
            {!loading && !failed ? ` · ${t("home.todayCount", { count: appts.length, arrived, done })}` : ""}
          </p>
        </div>
        <Button size="sm" variant="outline" asChild>
          <Link href={href("schedule")}>
            {t("home.openSchedule")} <ChevronRight />
          </Link>
        </Button>
      </CardHeader>
      <CardContent>
        {loading ? (
          <div className="space-y-2">
            {Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} className="h-9" />
            ))}
          </div>
        ) : failed ? (
          <WidgetError />
        ) : appts.length === 0 ? (
          <p className="py-6 text-center text-sm text-muted-foreground">{t("home.noAppointments")}</p>
        ) : (
          <>
            <ul className="divide-y">
              {shown.map((a) => {
                const c = STATUS_COLORS[a.status];
                return (
                  <li key={a.id}>
                    <Link href={href(`patients/${a.patientId}`)} className="flex items-center gap-3 py-2 text-sm hover:bg-muted/40">
                      <span className="w-24 shrink-0 tabular-nums text-muted-foreground">
                        {formatTime(a.startsAt, timezone)}–{formatTime(a.endsAt, timezone)}
                      </span>
                      <span className="h-8 w-1 shrink-0 rounded-full" style={{ backgroundColor: a.doctorColor ?? "#94a3b8" }} />
                      <span className="min-w-0 flex-1">
                        <span className="flex items-center gap-1 font-medium">
                          {a.patientIsVip ? <Star className="h-3.5 w-3.5 fill-warning text-warning" /> : null}
                          <span className="truncate">{a.patientName}</span>
                        </span>
                        <span className="block truncate text-xs text-muted-foreground">
                          {own ? a.services.map((s) => s.name).join(", ") || "—" : a.doctorName}
                          {a.chairName ? ` · ${a.chairName}` : ""}
                          {multiBranch ? ` · ${branchName.get(a.branchId) ?? ""}` : ""}
                        </span>
                      </span>
                      <span className="shrink-0 rounded-full border px-2 py-0.5 text-xs" style={{ backgroundColor: c.bg, borderColor: c.border, color: c.text }}>
                        {t(`schedule.status.${a.status}`)}
                      </span>
                    </Link>
                  </li>
                );
              })}
            </ul>
            {appts.length > shown.length ? (
              <Button variant="ghost" size="sm" className="mt-2 w-full" onClick={() => setExpanded(true)}>
                {t("home.showAll", { count: appts.length })}
              </Button>
            ) : null}
          </>
        )}
      </CardContent>
    </Card>
  );
}

function ApprovalsWidget() {
  const { href } = useOrgHref();
  const q = useQuery({
    queryKey: ["approvals", "Pending"],
    queryFn: () => api<Schemas["ApprovalDto"][]>("/approvals", { query: { status: "Pending" } }),
    staleTime: 30_000,
  });
  const count = q.data?.length ?? 0;
  const decidable = q.data?.filter((a) => a.canDecide).length ?? 0;
  return (
    <Card>
      <CardHeader className="flex-row items-center justify-between space-y-0">
        <CardTitle>{t("home.approvals")}</CardTitle>
        <Button size="sm" variant="ghost" asChild>
          <Link href={href("approvals")}>
            {t("home.openApprovals")} <ChevronRight />
          </Link>
        </Button>
      </CardHeader>
      <CardContent>
        {q.isLoading ? (
          <Skeleton className="h-8 w-24" />
        ) : q.isError ? (
          <WidgetError />
        ) : count === 0 ? (
          <p className="text-sm text-muted-foreground">{t("home.approvalsNone")}</p>
        ) : (
          <div className="flex items-baseline gap-2">
            <span className="text-3xl font-semibold tabular-nums">{count}</span>
            {decidable !== count ? <Badge variant="muted">{decidable}</Badge> : null}
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function CashWidget() {
  const { href } = useOrgHref();
  const { branchId, timezone } = useBranch();
  const q = useQuery({
    queryKey: ["cash-shifts", branchId ?? "all", "Open"],
    queryFn: () => api<Schemas["CashShiftDto"][]>("/cash-shifts", { query: { branch_id: branchId ?? undefined, status: "Open" } }),
    staleTime: 30_000,
  });
  const shifts = q.data ?? [];
  return (
    <Card>
      <CardHeader className="flex-row items-center justify-between space-y-0">
        <CardTitle>{t("home.cashShift")}</CardTitle>
        <Button size="sm" variant="ghost" asChild>
          <Link href={href("cash")}>
            {t("home.openCash")} <ChevronRight />
          </Link>
        </Button>
      </CardHeader>
      <CardContent className="space-y-2">
        {q.isLoading ? (
          <Skeleton className="h-8" />
        ) : q.isError ? (
          <WidgetError />
        ) : shifts.length === 0 ? (
          <Badge variant="warning">{t("home.shiftClosed")}</Badge>
        ) : (
          shifts.map((s) => (
            <div key={s.id} className="text-sm">
              <div className="flex items-center gap-2">
                <Badge variant="success">{s.cashRegisterName}</Badge>
                <span className="text-xs text-muted-foreground">{t("home.shiftOpen", { time: formatTime(s.openedAt, timezone), name: s.openedByName })}</span>
              </div>
              <div className="mt-1 flex justify-between">
                <span className="text-muted-foreground">{t("home.expectedNow")}</span>
                <span className="font-medium tabular-nums">{formatMoney(s.expectedNow)}</span>
              </div>
            </div>
          ))
        )}
      </CardContent>
    </Card>
  );
}

function LowStockWidget() {
  const { href } = useOrgHref();
  const q = useQuery({
    queryKey: ["stock-balances", "below-min"],
    queryFn: () => api<Schemas["StockBalanceRow"][]>("/stock/balances", { query: { below_min: true } }),
    staleTime: 5 * 60_000,
  });
  const rows = q.data ?? [];
  return (
    <Card>
      <CardHeader className="flex-row items-center justify-between space-y-0">
        <CardTitle>{t("home.lowStock")}</CardTitle>
        <Button size="sm" variant="ghost" asChild>
          <Link href={href("inventory")}>
            {t("home.openInventory")} <ChevronRight />
          </Link>
        </Button>
      </CardHeader>
      <CardContent>
        {q.isLoading ? (
          <Skeleton className="h-8" />
        ) : q.isError ? (
          <WidgetError />
        ) : rows.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("home.lowStockNone")}</p>
        ) : (
          <>
            <div className="mb-2 text-sm font-medium text-destructive">{t("home.lowStockCount", { count: rows.length })}</div>
            <ul className="space-y-1 text-xs">
              {rows.slice(0, 5).map((r) => (
                <li key={`${r.warehouseId}-${r.itemId}`} className="flex justify-between gap-2">
                  <span className="truncate">{r.itemName}</span>
                  <span className="shrink-0 tabular-nums text-muted-foreground">
                    {r.qty} / {r.minQty ?? "—"} {r.baseUnit}
                  </span>
                </li>
              ))}
            </ul>
          </>
        )}
      </CardContent>
    </Card>
  );
}
