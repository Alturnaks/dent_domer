"use client";

import * as React from "react";
import Link from "next/link";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { FileSpreadsheet, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { EmptyState } from "@/components/ui/empty-state";
import { Input, NativeSelect } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { TableSkeleton } from "@/components/ui/skeleton";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { apiErrorText, useAvailableBranches } from "@/components/cash/shared";
import { api } from "@/lib/api-client";
import { useAuth, useBranch, useOrgHref } from "@/lib/auth";
import { formatMoney } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import { monthRange, payrollKeys, periodText, PeriodStatusBadge, type PayrollPeriod, type PayrollPeriodDetail } from "./shared";

export function PeriodsTab() {
  const { can } = useAuth();
  const { branchId } = useBranch();
  const { href, push } = useOrgHref();
  const [createOpen, setCreateOpen] = React.useState(false);
  const periods = useQuery({
    queryKey: payrollKeys.periods(branchId),
    queryFn: () => api<PayrollPeriod[]>("/payroll-periods", { query: { branch_id: branchId ?? undefined } }),
  });
  const rows = periods.data ?? [];
  const canManage = can(P.payrollManage);

  return (
    <div>
      {canManage ? (
        <div className="mb-3 flex justify-end">
          <Button onClick={() => setCreateOpen(true)}>
            <Plus /> {t("payroll.periods.create")}
          </Button>
        </div>
      ) : null}
      <Card>
        {periods.isLoading ? (
          <TableSkeleton />
        ) : rows.length === 0 ? (
          <EmptyState
            className="m-4"
            icon={<FileSpreadsheet className="h-8 w-8" />}
            title={t("payroll.periods.empty")}
            description={t("payroll.periods.emptyHint")}
            action={
              canManage ? (
                <Button onClick={() => setCreateOpen(true)}>
                  <Plus /> {t("payroll.periods.create")}
                </Button>
              ) : undefined
            }
          />
        ) : (
          <div className="overflow-x-auto">
            <Table>
              <THead>
                <TR>
                  <TH>{t("payroll.periods.period")}</TH>
                  <TH>{t("payroll.periods.branch")}</TH>
                  <TH>{t("common.status")}</TH>
                  <TH className="text-right">{t("payroll.periods.doctors")}</TH>
                  <TH className="text-right">{t("payroll.periods.accrued")}</TH>
                  <TH className="text-right">{t("payroll.period.bonus")}</TH>
                  <TH className="text-right">{t("payroll.period.penalty")}</TH>
                  <TH className="text-right">{t("payroll.periods.total")}</TH>
                </TR>
              </THead>
              <TBody>
                {rows.map((p) => (
                  <TR key={p.id} className="cursor-pointer hover:bg-muted/50" onClick={() => push(`payroll/${p.id}`)}>
                    <TD className="tabular whitespace-nowrap">
                      <Link className="text-primary hover:underline" href={href(`payroll/${p.id}`)} onClick={(e) => e.stopPropagation()}>
                        {periodText(p.periodStart, p.periodEnd)}
                      </Link>
                    </TD>
                    <TD>{p.branchName}</TD>
                    <TD>
                      <PeriodStatusBadge status={p.status} />
                    </TD>
                    <TD className="tabular text-right">{p.entriesCount}</TD>
                    <TD className="tabular text-right">{formatMoney(p.accruedTotal)}</TD>
                    <TD className="tabular text-right">{p.bonusTotal ? formatMoney(p.bonusTotal) : "—"}</TD>
                    <TD className="tabular text-right">{p.penaltyTotal ? formatMoney(p.penaltyTotal) : "—"}</TD>
                    <TD className="tabular text-right font-medium">{formatMoney(p.total)}</TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          </div>
        )}
      </Card>
      {canManage ? <CreatePeriodDialog open={createOpen} onOpenChange={setCreateOpen} /> : null}
    </div>
  );
}

function CreatePeriodDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (o: boolean) => void }) {
  const qc = useQueryClient();
  const { push } = useOrgHref();
  const { branchId: currentBranch } = useBranch();
  const branches = useAvailableBranches();
  const [branchId, setBranchId] = React.useState("");
  const [start, setStart] = React.useState("");
  const [end, setEnd] = React.useState("");

  React.useEffect(() => {
    if (!open) return;
    const prev = monthRange(-1);
    setStart(prev.start);
    setEnd(prev.end);
    setBranchId(currentBranch ?? branches[0]?.id ?? "");
  }, [open, currentBranch, branches]);

  const mutation = useMutation({
    mutationFn: () => api<PayrollPeriodDetail>("/payroll-periods", { method: "POST", body: { branchId, periodStart: start, periodEnd: end } }),
    onSuccess: (res) => {
      toast.success(t("payroll.periods.created"));
      qc.invalidateQueries({ queryKey: ["payroll"] });
      onOpenChange(false);
      push(`payroll/${res.period.id}`);
    },
    onError: (e) => toast.error(apiErrorText(e)),
  });

  const valid = !!branchId && !!start && !!end && start <= end;
  const preset = (shift: number) => {
    const r = monthRange(shift);
    setStart(r.start);
    setEnd(r.end);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("payroll.periods.createTitle")}</DialogTitle>
          <DialogDescription>{t("payroll.periods.createHint")}</DialogDescription>
        </DialogHeader>
        <form
          id="create-period-form"
          className="space-y-3"
          onSubmit={(e) => {
            e.preventDefault();
            if (valid) mutation.mutate();
          }}
        >
          <Field label={t("payroll.periods.branch")}>
            <NativeSelect value={branchId} onChange={(e) => setBranchId(e.target.value)}>
              {branches.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.name}
                </option>
              ))}
            </NativeSelect>
          </Field>
          <div className="flex gap-2">
            <Button type="button" size="sm" variant="outline" onClick={() => preset(-1)}>
              {t("payroll.periods.lastMonth")}
            </Button>
            <Button type="button" size="sm" variant="outline" onClick={() => preset(0)}>
              {t("payroll.periods.thisMonth")}
            </Button>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <Field label={t("payroll.periods.start")}>
              <Input type="date" value={start} max={end || undefined} onChange={(e) => setStart(e.target.value)} />
            </Field>
            <Field label={t("payroll.periods.end")}>
              <Input type="date" value={end} min={start || undefined} onChange={(e) => setEnd(e.target.value)} />
            </Field>
          </div>
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t("common.cancel")}
          </Button>
          <Button type="submit" form="create-period-form" disabled={!valid} loading={mutation.isPending}>
            {t("payroll.periods.create")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
