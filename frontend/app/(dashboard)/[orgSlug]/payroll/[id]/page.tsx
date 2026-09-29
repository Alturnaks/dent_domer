"use client";

import * as React from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { ArrowLeft, Banknote, CheckCircle2, ChevronDown, ChevronRight, Lock, RefreshCw, SlidersHorizontal } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { useConfirm } from "@/components/ui/confirm";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { EmptyState, PageHeader } from "@/components/ui/empty-state";
import { Textarea } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { ErrorBlock } from "@/components/admin/common";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TBody, TD, TFoot, TH, THead, TR } from "@/components/ui/table";
import { apiErrorText, MoneyInput, moneyInputValue, parseMoneyInput } from "@/components/cash/shared";
import {
  payrollKeys,
  periodText,
  PeriodStatusBadge,
  schemeText,
  Stat,
  VisitLinesTable,
  type PayrollEntry,
  type PayrollEntryDetail,
  type PayrollPeriodDetail,
} from "@/components/payroll/shared";
import { api } from "@/lib/api-client";
import { useAuth, useOrgHref } from "@/lib/auth";
import { formatDate, formatDateTime, formatMoney } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";

export default function PayrollPeriodPage() {
  const { id } = useParams<{ id: string }>();
  const { can } = useAuth();
  const { href } = useOrgHref();
  const qc = useQueryClient();
  const confirm = useConfirm();
  const [expanded, setExpanded] = React.useState<string | null>(null);
  const [adjust, setAdjust] = React.useState<PayrollEntry | null>(null);
  const canManage = can(P.payrollManage);

  const q = useQuery({ queryKey: payrollKeys.period(id), queryFn: () => api<PayrollPeriodDetail>(`/payroll-periods/${id}`) });

  const action = useMutation({
    mutationFn: (kind: "calculate" | "approve" | "pay") => api<PayrollPeriodDetail>(`/payroll-periods/${id}/${kind}`, { method: "POST" }),
    onSuccess: (res, kind) => {
      qc.setQueryData(payrollKeys.period(id), res);
      qc.invalidateQueries({ queryKey: ["payroll", "periods"] });
      qc.invalidateQueries({ queryKey: ["payroll", "entry"] });
      toast.success(t(kind === "calculate" ? "payroll.period.recalculated" : kind === "approve" ? "payroll.period.approved" : "payroll.period.paid"));
    },
    onError: (e) => toast.error(apiErrorText(e)),
  });

  if (q.isLoading) return <Skeleton className="h-96 w-full" />;
  if (!q.data) return <EmptyState title={t("common.notFound")} />;

  const { period: p, entries, onlyPaidVisits } = q.data;
  const draft = p.status === "Draft";
  const sum = (f: (e: PayrollEntry) => number) => entries.reduce((s, e) => s + f(e), 0);

  return (
    <div>
      <Link href={href("payroll")} className="mb-2 inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
        <ArrowLeft className="h-4 w-4" /> {t("payroll.period.back")}
      </Link>
      <PageHeader
        title={`${t("payroll.title")}: ${periodText(p.periodStart, p.periodEnd)}`}
        description={
          <span className="flex flex-wrap items-center gap-2">
            <span>{p.branchName}</span>
            <PeriodStatusBadge status={p.status} />
            {p.approvedAt ? <span>{t("payroll.period.approvedBy", { name: p.approvedByName ?? "—", date: formatDateTime(p.approvedAt) })}</span> : null}
            {draft ? <span>{t("payroll.period.updatedAt", { date: formatDateTime(p.updatedAt) })}</span> : null}
          </span>
        }
        actions={
          canManage ? (
            <>
              {draft ? (
                <>
                  <Button variant="outline" loading={action.isPending && action.variables === "calculate"} onClick={() => action.mutate("calculate")}>
                    <RefreshCw /> {t("payroll.period.recalc")}
                  </Button>
                  <Button
                    loading={action.isPending && action.variables === "approve"}
                    onClick={async () => {
                      if (await confirm({ title: t("payroll.period.approveConfirm"), description: t("payroll.period.approveHint"), confirmText: t("payroll.period.approve") }))
                        action.mutate("approve");
                    }}
                  >
                    <CheckCircle2 /> {t("payroll.period.approve")}
                  </Button>
                </>
              ) : null}
              {p.status === "Approved" ? (
                <Button
                  variant="success"
                  loading={action.isPending && action.variables === "pay"}
                  onClick={async () => {
                    if (await confirm({ title: t("payroll.period.payConfirm"), confirmText: t("payroll.period.pay") })) action.mutate("pay");
                  }}
                >
                  <Banknote /> {t("payroll.period.pay")}
                </Button>
              ) : null}
            </>
          ) : undefined
        }
      />

      {!draft ? (
        <div className="mb-3 flex items-center gap-2 rounded-md border border-warning/40 bg-warning/10 px-3 py-2 text-sm">
          <Lock className="h-4 w-4 shrink-0" /> {t("payroll.period.lockedHint")}
        </div>
      ) : null}

      <div className="mb-4 grid grid-cols-2 gap-2 sm:grid-cols-4">
        <Stat label={t("payroll.period.revenue")} value={formatMoney(sum((e) => e.baseRevenue))} />
        <Stat label={t("payroll.period.accrued")} value={formatMoney(p.accruedTotal)} />
        <Stat label={`${t("payroll.period.bonus")} / ${t("payroll.period.penalty")}`} value={`${formatMoney(p.bonusTotal)} / ${formatMoney(p.penaltyTotal)}`} />
        <Stat label={t("payroll.periods.total")} value={formatMoney(p.total)} strong />
      </div>
      <p className="mb-2 text-xs text-muted-foreground">{onlyPaidVisits ? t("payroll.onlyPaid") : t("payroll.allClosed")}</p>

      <Card>
        {entries.length === 0 ? (
          <EmptyState className="m-4" title={t("payroll.period.noEntries")} />
        ) : (
          <div className="overflow-x-auto">
            <Table>
              <THead>
                <TR>
                  <TH className="w-8" />
                  <TH>{t("payroll.period.doctor")}</TH>
                  <TH>{t("payroll.period.scheme")}</TH>
                  <TH className="text-right">{t("payroll.period.visits")}</TH>
                  <TH className="text-right">{t("payroll.period.shifts")}</TH>
                  <TH className="text-right">{t("payroll.period.revenue")}</TH>
                  <TH className="text-right">{t("payroll.period.materials")}</TH>
                  <TH className="text-right">{t("payroll.period.accrued")}</TH>
                  <TH className="text-right">{t("payroll.period.bonus")}</TH>
                  <TH className="text-right">{t("payroll.period.penalty")}</TH>
                  <TH className="text-right">{t("payroll.period.total")}</TH>
                  {canManage && draft ? <TH /> : null}
                </TR>
              </THead>
              <TBody>
                {entries.map((e) => {
                  const open = expanded === e.id;
                  return (
                    <React.Fragment key={e.id}>
                      <TR className="cursor-pointer hover:bg-muted/40" onClick={() => setExpanded(open ? null : e.id)}>
                        <TD>{open ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}</TD>
                        <TD className="font-medium">
                          {e.doctorName}
                          {e.comment ? <div className="text-xs font-normal text-muted-foreground">{e.comment}</div> : null}
                        </TD>
                        <TD className={`text-sm ${e.scheme ? "" : "text-warning"}`}>{schemeText(e.scheme)}</TD>
                        <TD className="tabular text-right">{e.visitsCount}</TD>
                        <TD className="tabular text-right">{e.shifts}</TD>
                        <TD className="tabular text-right">{formatMoney(e.baseRevenue)}</TD>
                        <TD className="tabular text-right">{formatMoney(e.materialsCost)}</TD>
                        <TD className="tabular text-right">{formatMoney(e.accrued)}</TD>
                        <TD className="tabular text-right">{e.bonus ? formatMoney(e.bonus) : "—"}</TD>
                        <TD className="tabular text-right">{e.penalty ? formatMoney(e.penalty) : "—"}</TD>
                        <TD className="tabular text-right font-semibold">{formatMoney(e.total)}</TD>
                        {canManage && draft ? (
                          <TD className="text-right">
                            <Button
                              size="sm"
                              variant="outline"
                              onClick={(ev) => {
                                ev.stopPropagation();
                                setAdjust(e);
                              }}
                            >
                              <SlidersHorizontal /> {t("payroll.period.adjust")}
                            </Button>
                          </TD>
                        ) : null}
                      </TR>
                      {open ? (
                        <TR>
                          <TD colSpan={canManage && draft ? 12 : 11} className="bg-muted/20 p-0">
                            <EntryDrilldown entryId={e.id} />
                          </TD>
                        </TR>
                      ) : null}
                    </React.Fragment>
                  );
                })}
              </TBody>
              <TFoot>
                <TR>
                  <TD />
                  <TD colSpan={2} className="font-medium">
                    {t("payroll.period.totals")}
                  </TD>
                  <TD className="tabular text-right">{sum((e) => e.visitsCount)}</TD>
                  <TD />
                  <TD className="tabular text-right font-medium">{formatMoney(sum((e) => e.baseRevenue))}</TD>
                  <TD className="tabular text-right font-medium">{formatMoney(sum((e) => e.materialsCost))}</TD>
                  <TD className="tabular text-right font-medium">{formatMoney(p.accruedTotal)}</TD>
                  <TD className="tabular text-right font-medium">{formatMoney(p.bonusTotal)}</TD>
                  <TD className="tabular text-right font-medium">{formatMoney(p.penaltyTotal)}</TD>
                  <TD className="tabular text-right font-semibold">{formatMoney(p.total)}</TD>
                  {canManage && draft ? <TD /> : null}
                </TR>
              </TFoot>
            </Table>
          </div>
        )}
      </Card>

      {adjust ? <AdjustDialog entry={adjust} periodId={id} onClose={() => setAdjust(null)} /> : null}
    </div>
  );
}

function EntryDrilldown({ entryId }: { entryId: string }) {
  const q = useQuery({ queryKey: payrollKeys.entry(entryId), queryFn: () => api<PayrollEntryDetail>(`/payroll-entries/${entryId}`) });
  if (q.isError) return <ErrorBlock onRetry={() => q.refetch()} />;
  if (q.isLoading || !q.data) return <Skeleton className="m-3 h-24" />;
  const d = q.data;
  return (
    <CardContent className="space-y-2 p-3">
      {d.workedDays.length > 0 ? (
        <p className="text-xs text-muted-foreground">{t("payroll.visits.workedDays", { days: d.workedDays.map((x) => formatDate(x).slice(0, 5)).join(", ") })}</p>
      ) : null}
      <VisitLinesTable visits={d.visits} />
    </CardContent>
  );
}

function AdjustDialog({ entry, periodId, onClose }: { entry: PayrollEntry; periodId: string; onClose: () => void }) {
  const qc = useQueryClient();
  const [bonus, setBonus] = React.useState(moneyInputValue(entry.bonus));
  const [penalty, setPenalty] = React.useState(moneyInputValue(entry.penalty));
  const [comment, setComment] = React.useState(entry.comment ?? "");
  const b = parseMoneyInput(bonus);
  const pn = parseMoneyInput(penalty);
  const changed = b !== entry.bonus || pn !== entry.penalty || comment.trim() !== (entry.comment ?? "");
  const needComment = (b !== 0 || pn !== 0) && comment.trim() === "";

  const mutation = useMutation({
    mutationFn: () => api<PayrollEntry>(`/payroll-entries/${entry.id}`, { method: "PATCH", body: { bonus: b, penalty: pn, comment: comment.trim() || null } }),
    onSuccess: () => {
      toast.success(t("payroll.adjust.saved"));
      qc.invalidateQueries({ queryKey: payrollKeys.period(periodId) });
      qc.invalidateQueries({ queryKey: ["payroll", "periods"] });
      onClose();
    },
    onError: (e) => toast.error(apiErrorText(e)),
  });

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("payroll.adjust.title")}</DialogTitle>
          <DialogDescription>
            {entry.doctorName} · {t("payroll.period.accrued")}: {formatMoney(entry.accrued)}
          </DialogDescription>
        </DialogHeader>
        <form
          id="adjust-form"
          className="space-y-3"
          onSubmit={(e) => {
            e.preventDefault();
            if (changed && !needComment) mutation.mutate();
          }}
        >
          <div className="grid grid-cols-2 gap-3">
            <Field label={t("payroll.adjust.bonus")}>
              <MoneyInput autoFocus value={bonus} onChange={(e) => setBonus(e.target.value)} placeholder="0" />
            </Field>
            <Field label={t("payroll.adjust.penalty")}>
              <MoneyInput value={penalty} onChange={(e) => setPenalty(e.target.value)} placeholder="0" />
            </Field>
          </div>
          <Field label={t("payroll.adjust.comment")} hint={t("payroll.adjust.commentHint")} error={needComment ? t("payroll.adjust.commentRequired") : undefined}>
            <Textarea rows={3} value={comment} onChange={(e) => setComment(e.target.value)} />
          </Field>
          <div className="flex justify-between rounded-md border px-3 py-2 text-sm font-semibold">
            <span>{t("payroll.adjust.resultTotal")}</span>
            <span className="tabular">{formatMoney(entry.accrued + b - pn)}</span>
          </div>
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            {t("common.cancel")}
          </Button>
          <Button type="submit" form="adjust-form" disabled={!changed || needComment} loading={mutation.isPending}>
            {t("common.save")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
