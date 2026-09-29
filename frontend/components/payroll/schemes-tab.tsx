"use client";

import * as React from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { ChevronDown, ChevronRight, Pencil, Plus } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { EmptyState } from "@/components/ui/empty-state";
import { Input, NativeSelect } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { TableSkeleton } from "@/components/ui/skeleton";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { apiErrorText, MoneyInput, moneyInputValue, parseMoneyInput } from "@/components/cash/shared";
import { api } from "@/lib/api-client";
import { useAuth, useBranch } from "@/lib/auth";
import { formatDate, isoDate } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import { useDoctors } from "@/lib/queries";
import { payrollKeys, SCHEME_TYPES, schemeText, type PayrollScheme, type PayrollSchemeType } from "./shared";

type DoctorRef = { membershipId: string; fullName: string; specialty?: string | null };

export function SchemesTab() {
  const { can } = useAuth();
  const { branchId } = useBranch();
  const doctors = useDoctors(branchId);
  const schemes = useQuery({ queryKey: payrollKeys.schemes, queryFn: () => api<PayrollScheme[]>("/payroll-schemes") });
  const [editFor, setEditFor] = React.useState<{ doctor: DoctorRef; current: PayrollScheme | null } | null>(null);
  const [expanded, setExpanded] = React.useState<Set<string>>(new Set());
  const canManage = can(P.payrollManage);

  const byDoctor = React.useMemo(() => {
    const m = new Map<string, PayrollScheme[]>();
    for (const s of schemes.data ?? []) m.set(s.membershipId, [...(m.get(s.membershipId) ?? []), s]);
    return m;
  }, [schemes.data]);

  const toggle = (id: string) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  if (doctors.isLoading || schemes.isLoading) return <TableSkeleton />;
  const list = doctors.data ?? [];
  if (list.length === 0) return <EmptyState title={t("payroll.schemes.empty")} />;

  return (
    <Card>
      <div className="overflow-x-auto">
        <Table>
          <THead>
            <TR>
              <TH className="w-8" />
              <TH>{t("payroll.schemes.doctor")}</TH>
              <TH>{t("payroll.period.scheme")}</TH>
              <TH>{t("payroll.schemes.validFrom")}</TH>
              <TH />
            </TR>
          </THead>
          <TBody>
            {list.map((d) => {
              const history = byDoctor.get(d.membershipId) ?? [];
              const current = history.find((s) => s.isCurrent) ?? null;
              // Список отсортирован по дате начала по убыванию: ближайшая будущая версия — последняя из будущих.
              const future = history.filter((s) => !s.isCurrent && current !== null && s.validFrom > current.validFrom).reverse();
              const upcoming = current === null ? history[history.length - 1] ?? null : null;
              const open = expanded.has(d.membershipId);
              return (
                <React.Fragment key={d.membershipId}>
                  <TR>
                    <TD>
                      {history.length > 0 ? (
                        <Button size="icon-sm" variant="ghost" onClick={() => toggle(d.membershipId)} aria-label={t("payroll.schemes.history")}>
                          {open ? <ChevronDown /> : <ChevronRight />}
                        </Button>
                      ) : null}
                    </TD>
                    <TD>
                      <div className="font-medium">{d.fullName}</div>
                      {d.specialty ? <div className="text-xs text-muted-foreground">{d.specialty}</div> : null}
                    </TD>
                    <TD>
                      {current ? (
                        <span>{schemeText(current)}</span>
                      ) : upcoming ? (
                        <span className="text-muted-foreground">{schemeText(upcoming)}</span>
                      ) : (
                        <Badge variant="warning">{t("payroll.schemes.noSchemeYet")}</Badge>
                      )}
                      {future.length > 0 ? (
                        <div className="text-xs text-muted-foreground">
                          {t("payroll.schemes.future", { date: formatDate(future[0].validFrom) })}: {schemeText(future[0])}
                        </div>
                      ) : null}
                    </TD>
                    <TD className="tabular">{current ? formatDate(current.validFrom) : upcoming ? t("payroll.schemes.future", { date: formatDate(upcoming.validFrom) }) : "—"}</TD>
                    <TD className="text-right">
                      {canManage ? (
                        <Button size="sm" variant="outline" onClick={() => setEditFor({ doctor: d, current: current ?? upcoming })}>
                          {current || upcoming ? <Pencil /> : <Plus />} {current || upcoming ? t("payroll.schemes.change") : t("payroll.schemes.add")}
                        </Button>
                      ) : null}
                    </TD>
                  </TR>
                  {open
                    ? history.map((s) => (
                        <TR key={s.id} className="bg-muted/30 text-sm">
                          <TD />
                          <TD className="text-muted-foreground">{s.isCurrent ? <Badge variant="success">{t("payroll.schemes.current")}</Badge> : null}</TD>
                          <TD>
                            {t(`payroll.schemeType.${s.type}`)} · {schemeText(s)}
                          </TD>
                          <TD className="tabular">{formatDate(s.validFrom)}</TD>
                          <TD />
                        </TR>
                      ))
                    : null}
                </React.Fragment>
              );
            })}
          </TBody>
        </Table>
      </div>
      {editFor ? (
        <SchemeDialog open={!!editFor} onOpenChange={(o) => !o && setEditFor(null)} doctor={editFor.doctor} current={editFor.current} />
      ) : null}
    </Card>
  );
}

function SchemeDialog({
  open,
  onOpenChange,
  doctor,
  current,
}: {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  doctor: DoctorRef;
  current: PayrollScheme | null;
}) {
  const qc = useQueryClient();
  const [type, setType] = React.useState<PayrollSchemeType>(current?.type ?? "PercentRevenue");
  const [percent, setPercent] = React.useState(current?.percent ? String(current.percent) : "");
  const [fixed, setFixed] = React.useState(moneyInputValue(current?.fixedAmount));
  const [rate, setRate] = React.useState(moneyInputValue(current?.shiftRate));
  const [validFrom, setValidFrom] = React.useState(() => {
    const d = new Date();
    return isoDate(new Date(d.getFullYear(), d.getMonth(), 1));
  });

  const pct = Number(percent.replace(",", "."));
  const needsPercent = type !== "PerShift";
  const percentOk = !needsPercent || (percent.trim() === "" ? type === "FixedPlusPercent" : Number.isFinite(pct) && pct >= 0 && pct <= 100);
  const valid =
    !!validFrom &&
    percentOk &&
    (type !== "PercentRevenue" && type !== "PercentRevenueMinusMaterials" ? true : pct > 0) &&
    (type !== "FixedPlusPercent" || parseMoneyInput(fixed) > 0) &&
    (type !== "PerShift" || parseMoneyInput(rate) > 0);

  const mutation = useMutation({
    mutationFn: () =>
      api<PayrollScheme>("/payroll-schemes", {
        method: "POST",
        body: {
          membershipId: doctor.membershipId,
          type,
          percent: needsPercent && percent.trim() !== "" ? pct : null,
          fixedAmount: type === "FixedPlusPercent" ? parseMoneyInput(fixed) : null,
          shiftRate: type === "PerShift" ? parseMoneyInput(rate) : null,
          validFrom,
        },
      }),
    onSuccess: () => {
      toast.success(t("payroll.schemes.saved"));
      qc.invalidateQueries({ queryKey: ["payroll"] });
      onOpenChange(false);
    },
    onError: (e) => toast.error(apiErrorText(e)),
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("payroll.schemes.addFor", { name: doctor.fullName })}</DialogTitle>
          <DialogDescription>{t("payroll.schemes.validFromHint")}</DialogDescription>
        </DialogHeader>
        <form
          id="scheme-form"
          className="space-y-3"
          onSubmit={(e) => {
            e.preventDefault();
            if (valid) mutation.mutate();
          }}
        >
          <Field label={t("payroll.schemes.type")} hint={t(`payroll.schemes.typeHint.${type}`)}>
            <NativeSelect value={type} onChange={(e) => setType(e.target.value as PayrollSchemeType)}>
              {SCHEME_TYPES.map((s) => (
                <option key={s} value={s}>
                  {t(`payroll.schemeType.${s}`)}
                </option>
              ))}
            </NativeSelect>
          </Field>
          {type === "FixedPlusPercent" ? (
            <Field label={t("payroll.schemes.fixed")}>
              <MoneyInput value={fixed} onChange={(e) => setFixed(e.target.value)} />
            </Field>
          ) : null}
          {needsPercent ? (
            <Field label={t("payroll.schemes.percent")}>
              <Input inputMode="decimal" className="text-right tabular" value={percent} onChange={(e) => setPercent(e.target.value)} />
            </Field>
          ) : (
            <Field label={t("payroll.schemes.shiftRate")}>
              <MoneyInput value={rate} onChange={(e) => setRate(e.target.value)} />
            </Field>
          )}
          <Field label={t("payroll.schemes.validFrom")}>
            <Input type="date" value={validFrom} onChange={(e) => setValidFrom(e.target.value)} />
          </Field>
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t("common.cancel")}
          </Button>
          <Button type="submit" form="scheme-form" disabled={!valid} loading={mutation.isPending}>
            {t("common.save")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
