"use client";

import * as React from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { AlertTriangle, Plus, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input, NativeSelect } from "@/components/ui/input";
import { Field, Label } from "@/components/ui/label";
import { api, newIdempotencyKey } from "@/lib/api-client";
import { useAuth, useBranch } from "@/lib/auth";
import { formatDate, formatMoney } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import type { Schemas } from "@/lib/types";
import { OpenShiftForm } from "./open-shift-form";
import { PatientPicker, type PickedPatient } from "./patient-picker";
import {
  apiErrorText,
  MoneyInput,
  moneyInputValue,
  parseMoneyInput,
  PAYMENT_METHODS,
  useAvailableBranches,
  useOpenShifts,
  type PatientBalance,
  type PaymentMethod,
} from "./shared";

type CreatePaymentRequest = Schemas["CreatePaymentRequest"];
type CreatePaymentResult = Schemas["CreatePaymentResult"];

export type PaymentVisitRef = { id: string; openedAt?: string | null; debt: number; branchId?: string | null };

type Row = { key: number; method: PaymentMethod; amount: string };

export type PaymentDialogProps = {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  /** Пациент; если не задан — в диалоге показывается поиск пациента. */
  patient?: PickedPatient | null;
  /** Визит, за который принимается оплата (долг подставляется как сумма). */
  visit?: PaymentVisitRef | null;
  /** Филиал оплаты; по умолчанию — филиал визита или текущий выбранный. */
  branchId?: string | null;
  onPaid?: (result: CreatePaymentResult) => void;
};

/** Диалог приёма оплаты: смешанная оплата, сдача, аванс, проверка открытой смены. POST /payments с Idempotency-Key. */
export function PaymentDialog({ open, onOpenChange, patient: patientProp, visit, branchId: branchProp, onPaid }: PaymentDialogProps) {
  const qc = useQueryClient();
  const { can } = useAuth();
  const { branchId: currentBranch } = useBranch();
  const branches = useAvailableBranches();

  const [patient, setPatient] = React.useState<PickedPatient | null>(patientProp ?? null);
  const [branchId, setBranchId] = React.useState<string>("");
  const [rows, setRows] = React.useState<Row[]>([]);
  const [received, setReceived] = React.useState("");
  const [comment, setComment] = React.useState("");
  const idemKey = React.useRef<string>(newIdempotencyKey());
  const rowSeq = React.useRef(1);
  const initializedFor = React.useRef<string | null>(null);

  const effectiveBranch = branchProp ?? visit?.branchId ?? currentBranch ?? branchId ?? "";

  // Сброс при открытии.
  React.useEffect(() => {
    if (!open) return;
    initializedFor.current = null;
    setPatient(patientProp ?? null);
    setReceived("");
    setComment("");
    setBranchId(branches[0]?.id ?? "");
    idemKey.current = newIdempotencyKey();
    setRows([]);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, patientProp?.id, visit?.id]);

  // Список филиалов владельца может загрузиться позже открытия.
  React.useEffect(() => {
    if (open && !branchId && branches.length > 0) setBranchId(branches[0].id);
  }, [open, branchId, branches]);

  const balance = useQuery({
    queryKey: ["patient", patient?.id, "balance"],
    queryFn: () => api<PatientBalance>(`/patients/${patient!.id}/balance`),
    enabled: open && !!patient,
    retry: false,
  });
  const shifts = useOpenShifts(effectiveBranch, open);
  const hasShift = (shifts.data ?? []).length > 0;

  const due = visit ? Math.max(visit.debt, 0) : Math.max(balance.data?.debt ?? 0, 0);
  const advance = balance.data?.advance ?? 0;

  // Сумма по умолчанию — долг по визиту/пациенту, после загрузки баланса.
  React.useEffect(() => {
    if (!open || !patient || initializedFor.current === patient.id) return;
    if (!visit && balance.isLoading) return;
    initializedFor.current = patient.id;
    setRows([{ key: rowSeq.current++, method: "Cash", amount: moneyInputValue(due) }]);
  }, [open, patient, visit, balance.isLoading, due]);

  const methods = PAYMENT_METHODS.filter((m) => m !== "Balance" || (visit && advance > 0));
  const total = rows.reduce((s, r) => s + parseMoneyInput(r.amount), 0);
  const single = rows.length === 1 ? rows[0] : null;
  const receivedMinor = parseMoneyInput(received);
  const change = single?.method === "Cash" && receivedMinor > 0 ? receivedMinor - total : null;
  const invalid = rows.length === 0 || rows.some((r) => parseMoneyInput(r.amount) <= 0);

  const mutation = useMutation({
    mutationFn: () => {
      const body: CreatePaymentRequest = {
        patientId: patient!.id,
        visitId: visit?.id ?? null,
        branchId: effectiveBranch,
        method: single ? single.method : null,
        amount: single ? parseMoneyInput(single.amount) : null,
        splits: single ? null : rows.map((r) => ({ method: r.method, amount: parseMoneyInput(r.amount) })),
        comment: comment.trim() || null,
      };
      return api<CreatePaymentResult>("/payments", { method: "POST", body, idempotencyKey: idemKey.current });
    },
    onSuccess: (res) => {
      toast.success(t("cash.pay.success", { balance: formatMoney(res.patientBalance) }));
      qc.invalidateQueries({ queryKey: ["cash"] });
      qc.invalidateQueries({ queryKey: ["patient", patient!.id] });
      qc.invalidateQueries({ queryKey: ["patients"] });
      if (visit) qc.invalidateQueries({ queryKey: ["visit", visit.id] });
      onPaid?.(res);
      onOpenChange(false);
    },
    onError: (e) => {
      toast.error(apiErrorText(e));
      // Бизнес-ошибка — запрос не выполнен, следующую попытку отправляем с новым ключом.
      idemKey.current = newIdempotencyKey();
      qc.invalidateQueries({ queryKey: ["cash", "shifts"] });
    },
  });

  const updateRow = (key: number, patch: Partial<Row>) => setRows((rs) => rs.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  const addRow = () => {
    const rest = Math.max(due - total, 0);
    const used = new Set(rows.map((r) => r.method));
    const next = methods.find((m) => !used.has(m)) ?? "Card";
    setRows((rs) => [...rs, { key: rowSeq.current++, method: next, amount: moneyInputValue(rest) }]);
  };

  const needBranchSelect = !branchProp && !visit?.branchId && !currentBranch;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("cash.pay.title")}</DialogTitle>
          {visit?.openedAt ? <DialogDescription>{t("cash.pay.forVisit", { date: formatDate(visit.openedAt) })}</DialogDescription> : null}
        </DialogHeader>

        {needBranchSelect ? (
          <Field label={t("cash.branch")}>
            <NativeSelect value={branchId} onChange={(e) => setBranchId(e.target.value)}>
              {branches.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.name}
                </option>
              ))}
            </NativeSelect>
          </Field>
        ) : null}

        {/* Пациент */}
        {!patient ? (
          <div className="space-y-1.5">
            <Label>{t("cash.pay.patient")}</Label>
            <PatientPicker autoFocus onPick={(p) => setPatient(p)} />
          </div>
        ) : (
          <div className="rounded-md border p-3">
            <div className="flex items-start justify-between gap-2">
              <div className="min-w-0">
                <div className="truncate font-medium">{patient.fullName}</div>
                {balance.data ? (
                  <div className="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
                    <span>
                      {t("cash.pay.balance")}:{" "}
                      <span className={`tabular ${balance.data.balance < 0 ? "text-destructive" : balance.data.balance > 0 ? "text-success" : ""}`}>
                        {formatMoney(balance.data.balance)}
                      </span>
                    </span>
                    <span>
                      {t("cash.pay.debt")}: <span className="tabular">{formatMoney(balance.data.debt)}</span>
                    </span>
                    {balance.data.advance > 0 ? (
                      <span>
                        {t("cash.pay.advance")}: <span className="tabular">{formatMoney(balance.data.advance)}</span>
                      </span>
                    ) : null}
                  </div>
                ) : null}
                {visit ? (
                  <div className="mt-1 text-xs">
                    {t("cash.pay.visitDebt")}: <span className="font-medium tabular">{formatMoney(visit.debt)}</span>
                  </div>
                ) : null}
              </div>
              {!patientProp ? (
                <Button type="button" variant="ghost" size="sm" onClick={() => { setPatient(null); setRows([]); initializedFor.current = null; }}>
                  {t("cash.pay.changePatient")}
                </Button>
              ) : null}
            </div>
          </div>
        )}

        {/* Смена */}
        {effectiveBranch && shifts.isSuccess && !hasShift ? (
          <div className="space-y-3 rounded-md border border-warning/50 bg-warning/10 p-3">
            <div className="flex items-start gap-2 text-sm">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-[oklch(0.5_0.12_70)]" />
              <div>
                <div className="font-medium">{t("cash.pay.noShift")}</div>
                <div className="text-muted-foreground">{can(P.cashShift) ? t("cash.pay.noShiftHint") : t("cash.pay.noShiftNoRight")}</div>
              </div>
            </div>
            {can(P.cashShift) ? <OpenShiftForm branchId={effectiveBranch} /> : null}
          </div>
        ) : null}

        {/* Способы оплаты */}
        {patient && hasShift ? (
          <form
            id="payment-form"
            className="space-y-3"
            onSubmit={(e) => {
              e.preventDefault();
              if (invalid) {
                toast.error(t("cash.pay.amountRequired"));
                return;
              }
              mutation.mutate();
            }}
          >
            <div className="space-y-2">
              <div className="grid grid-cols-[1fr_9rem_2rem] gap-2 text-xs font-medium text-muted-foreground">
                <span>{t("cash.pay.method")}</span>
                <span className="text-right">{t("cash.pay.amount")}</span>
                <span />
              </div>
              {rows.map((r) => (
                <div key={r.key} className="grid grid-cols-[1fr_9rem_2rem] items-center gap-2">
                  <NativeSelect value={r.method} onChange={(e) => updateRow(r.key, { method: e.target.value as PaymentMethod })}>
                    {methods.map((m) => (
                      <option key={m} value={m}>
                        {t(`cash.methods.${m}`)}
                      </option>
                    ))}
                  </NativeSelect>
                  <MoneyInput value={r.amount} onChange={(e) => updateRow(r.key, { amount: e.target.value })} aria-invalid={parseMoneyInput(r.amount) <= 0} />
                  {rows.length > 1 ? (
                    <Button type="button" variant="ghost" size="icon-sm" title={t("cash.pay.removeMethod")} onClick={() => setRows((rs) => rs.filter((x) => x.key !== r.key))}>
                      <X />
                    </Button>
                  ) : (
                    <span />
                  )}
                </div>
              ))}
              <div className="flex items-center justify-between">
                <Button type="button" variant="link" size="sm" className="px-0" onClick={addRow}>
                  <Plus /> {t("cash.pay.addMethod")}
                </Button>
                <span className="text-sm">
                  {t("cash.pay.total")}: <span className="font-semibold tabular">{formatMoney(total)}</span>
                </span>
              </div>
            </div>

            {single?.method === "Cash" ? (
              <div className="grid grid-cols-2 items-end gap-3">
                <Field label={t("cash.pay.received")}>
                  <MoneyInput value={received} onChange={(e) => setReceived(e.target.value)} placeholder={moneyInputValue(total)} />
                </Field>
                <div className="pb-2 text-sm">
                  {change !== null ? (
                    <span className={change < 0 ? "text-destructive" : ""}>
                      {t("cash.pay.change")}: <span className="font-semibold tabular">{formatMoney(change)}</span>
                    </span>
                  ) : null}
                </div>
              </div>
            ) : null}

            {due > 0 && total > due ? <p className="text-xs text-muted-foreground">{t("cash.pay.advanceHint")}</p> : null}

            <Field label={t("cash.pay.comment")}>
              <Input value={comment} onChange={(e) => setComment(e.target.value)} maxLength={500} />
            </Field>
          </form>
        ) : null}

        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
            {t("common.cancel")}
          </Button>
          <Button type="submit" form="payment-form" variant="success" loading={mutation.isPending} disabled={!patient || !hasShift || invalid}>
            {t("cash.pay.submit", { amount: formatMoney(total) })}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
