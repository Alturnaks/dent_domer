"use client";

import * as React from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input, NativeSelect, Textarea } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { api, newIdempotencyKey } from "@/lib/api-client";
import { useAuth } from "@/lib/auth";
import { formatDateTime, formatMoney, isoDate } from "@/lib/format";
import { t } from "@/lib/i18n";
import { useReference } from "@/lib/queries";
import type { Schemas } from "@/lib/types";
import {
  apiErrorText,
  MoneyInput,
  moneyInputValue,
  parseMoneyInput,
  PAYMENT_METHODS,
  type CashOperation,
  type CashShift,
  type Expense,
  type Payment,
  type PaymentMethod,
} from "./shared";

type DialogBase = { open: boolean; onOpenChange: (o: boolean) => void };

/* ---------------- Закрытие смены ---------------- */

export function CloseShiftDialog({ open, onOpenChange, shift }: DialogBase & { shift: CashShift }) {
  const qc = useQueryClient();
  const [actual, setActual] = React.useState("");
  const [comment, setComment] = React.useState("");
  // Ожидаемый остаток берём свежим с сервера на момент открытия диалога.
  const fresh = useQuery({
    queryKey: ["cash", "shift", shift.id],
    queryFn: () => api<CashShift>(`/cash-shifts/${shift.id}`),
    enabled: open,
  });
  const s = fresh.data ?? shift;
  React.useEffect(() => {
    if (open) {
      setActual("");
      setComment("");
    }
  }, [open]);

  const actualMinor = parseMoneyInput(actual);
  const diff = actual.trim() === "" ? null : actualMinor - s.expectedNow;

  const mutation = useMutation({
    mutationFn: () =>
      api<CashShift>(`/cash-shifts/${shift.id}/close`, {
        method: "POST",
        body: { closingBalanceActual: actualMinor, comment: comment.trim() || null, version: s.version },
      }),
    onSuccess: (res) => {
      const d = res.difference ?? 0;
      if (d === 0) toast.success(t("cash.shift.closed"));
      else toast.warning(t("cash.shift.closedResult", { amount: formatMoney(d) }));
      qc.invalidateQueries({ queryKey: ["cash"] });
      onOpenChange(false);
    },
    onError: (e) => {
      toast.error(apiErrorText(e));
      qc.invalidateQueries({ queryKey: ["cash", "shift", shift.id] });
    },
  });

  const rows: [string, number, string?][] = [
    ["cash.shift.opening", s.openingBalance],
    ["cash.shift.cashIn", s.cashIn, "+"],
    ["cash.shift.deposits", s.deposits, "+"],
    ["cash.shift.refunds", s.refunds, "−"],
    ["cash.shift.expenses", s.expenses, "−"],
    ["cash.shift.collections", s.collections, "−"],
  ];

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("cash.shift.closeTitle")}</DialogTitle>
          <DialogDescription>
            {s.cashRegisterName} · {t("cash.shift.openedAt")} {formatDateTime(s.openedAt)}
          </DialogDescription>
        </DialogHeader>
        <form
          id="close-shift-form"
          className="space-y-3"
          onSubmit={(e) => {
            e.preventDefault();
            if (actual.trim() === "") return;
            mutation.mutate();
          }}
        >
          <div className="rounded-md border p-3 text-sm">
            {rows.map(([k, v, sign]) => (
              <div key={k} className="flex justify-between py-0.5">
                <span className="text-muted-foreground">{t(k)}</span>
                <span className="tabular">
                  {sign && v ? `${sign} ` : ""}
                  {formatMoney(v)}
                </span>
              </div>
            ))}
            <div className="mt-1 flex justify-between border-t pt-2 font-semibold">
              <span>{t("cash.shift.expected")}</span>
              <span className="tabular">{fresh.isFetching && !fresh.data ? "…" : formatMoney(s.expectedNow)}</span>
            </div>
          </div>
          <Field label={t("cash.shift.actual")}>
            <MoneyInput autoFocus value={actual} onChange={(e) => setActual(e.target.value)} placeholder={moneyInputValue(s.expectedNow) || "0"} />
          </Field>
          {diff !== null ? (
            <div
              className={`flex justify-between rounded-md px-3 py-2 text-sm ${diff === 0 ? "bg-success/10" : "bg-destructive/10 text-destructive"}`}
            >
              <span>{diff === 0 ? t("cash.shift.difference") : diff < 0 ? t("cash.shift.shortage") : t("cash.shift.surplus")}</span>
              <span className="font-semibold tabular">{formatMoney(diff)}</span>
            </div>
          ) : null}
          {diff !== null && diff !== 0 ? <p className="text-xs text-muted-foreground">{t("cash.shift.differenceHint")}</p> : null}
          <Field label={t("common.comment")}>
            <Textarea value={comment} onChange={(e) => setComment(e.target.value)} rows={2} maxLength={500} />
          </Field>
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t("common.cancel")}
          </Button>
          <Button type="submit" form="close-shift-form" loading={mutation.isPending} disabled={actual.trim() === ""}>
            {t("cash.shift.close")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/* ---------------- Внесение / изъятие ---------------- */

export function CashOperationDialog({ open, onOpenChange, shift }: DialogBase & { shift: CashShift }) {
  const qc = useQueryClient();
  const [type, setType] = React.useState<Schemas["CashOperationType"]>("Collection");
  const [amount, setAmount] = React.useState("");
  const [comment, setComment] = React.useState("");
  React.useEffect(() => {
    if (open) {
      setType("Collection");
      setAmount("");
      setComment("");
    }
  }, [open]);

  const mutation = useMutation({
    mutationFn: () =>
      api<CashOperation>(`/cash-shifts/${shift.id}/operations`, {
        method: "POST",
        body: { type, amount: parseMoneyInput(amount), comment: comment.trim() || null },
      }),
    onSuccess: () => {
      toast.success(t("cash.operations.done"));
      qc.invalidateQueries({ queryKey: ["cash"] });
      onOpenChange(false);
    },
    onError: (e) => toast.error(apiErrorText(e)),
  });
  const valid = parseMoneyInput(amount) > 0;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("cash.operations.title")}</DialogTitle>
          <DialogDescription>
            {t("cash.shift.expectedNow")}: {formatMoney(shift.expectedNow)}
          </DialogDescription>
        </DialogHeader>
        <form
          id="cash-op-form"
          className="space-y-3"
          onSubmit={(e) => {
            e.preventDefault();
            if (valid) mutation.mutate();
          }}
        >
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label={t("cash.operations.type")}>
              <NativeSelect value={type} onChange={(e) => setType(e.target.value as Schemas["CashOperationType"])}>
                {(["Collection", "Deposit"] as const).map((x) => (
                  <option key={x} value={x}>
                    {t(`cash.operations.types.${x}`)}
                  </option>
                ))}
              </NativeSelect>
            </Field>
            <Field label={t("cash.operations.amount")}>
              <MoneyInput autoFocus value={amount} onChange={(e) => setAmount(e.target.value)} placeholder="0" />
            </Field>
          </div>
          <Field label={t("cash.operations.comment")}>
            <Input value={comment} onChange={(e) => setComment(e.target.value)} maxLength={500} />
          </Field>
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t("common.cancel")}
          </Button>
          <Button type="submit" form="cash-op-form" loading={mutation.isPending} disabled={!valid}>
            {t("common.save")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/* ---------------- Возврат ---------------- */

export function RefundDialog({ open, onOpenChange, payment, refunded = 0 }: DialogBase & { payment: Payment | null; refunded?: number }) {
  const qc = useQueryClient();
  const { me } = useAuth();
  const [amount, setAmount] = React.useState("");
  const [method, setMethod] = React.useState<PaymentMethod | "">("");
  const [reason, setReason] = React.useState("");
  const idemKey = React.useRef(newIdempotencyKey());
  const available = payment ? Math.max(payment.amount - refunded, 0) : 0;
  const limit = me?.limits.maxRefundAmount ?? null;

  React.useEffect(() => {
    if (open) {
      setAmount(moneyInputValue(available));
      setMethod("");
      setReason("");
      idemKey.current = newIdempotencyKey();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, payment?.id]);

  const mutation = useMutation({
    mutationFn: () =>
      api<Payment>(`/payments/${payment!.id}/refund`, {
        method: "POST",
        body: { amount: parseMoneyInput(amount), method: method || null, reason: reason.trim() },
        idempotencyKey: idemKey.current,
      }),
    onSuccess: (res) => {
      if (res.pendingApproval) toast.warning(t("cash.refund.pending"));
      else toast.success(t("cash.refund.done"));
      qc.invalidateQueries({ queryKey: ["cash"] });
      qc.invalidateQueries({ queryKey: ["patient", res.patientId] });
      if (res.visitId) qc.invalidateQueries({ queryKey: ["visit", res.visitId] });
      onOpenChange(false);
    },
    onError: (e) => {
      toast.error(apiErrorText(e));
      idemKey.current = newIdempotencyKey();
    },
  });

  const amt = parseMoneyInput(amount);
  const valid = amt > 0 && amt <= available && reason.trim().length > 0;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("cash.refund.title")}</DialogTitle>
          {payment ? (
            <DialogDescription>
              {payment.patientName} · {formatDateTime(payment.createdAt)} · {t(`cash.methods.${payment.method}`)} · {formatMoney(payment.amount)}
            </DialogDescription>
          ) : null}
        </DialogHeader>
        <form
          id="refund-form"
          className="space-y-3"
          onSubmit={(e) => {
            e.preventDefault();
            if (valid) mutation.mutate();
          }}
        >
          <p className="text-sm">{t("cash.refund.available", { amount: formatMoney(available) })}</p>
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label={t("cash.refund.amount")} error={amt > available ? t("cash.refund.available", { amount: formatMoney(available) }) : undefined}>
              <MoneyInput value={amount} onChange={(e) => setAmount(e.target.value)} aria-invalid={amt > available} />
            </Field>
            <Field label={t("cash.refund.method")}>
              <NativeSelect value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod | "")}>
                <option value="">{t("cash.refund.sameMethod")}</option>
                {PAYMENT_METHODS.filter((m) => m !== "Balance").map((m) => (
                  <option key={m} value={m}>
                    {t(`cash.methods.${m}`)}
                  </option>
                ))}
              </NativeSelect>
            </Field>
          </div>
          <Field label={t("cash.refund.reason")}>
            <Textarea value={reason} onChange={(e) => setReason(e.target.value)} rows={2} maxLength={500} />
          </Field>
          {limit !== null && limit !== undefined && amt > limit ? (
            <p className="text-xs text-[oklch(0.5_0.12_70)]">{t("cash.refund.limit", { amount: formatMoney(limit) })}</p>
          ) : null}
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t("common.cancel")}
          </Button>
          <Button type="submit" form="refund-form" variant="destructive" loading={mutation.isPending} disabled={!valid}>
            {t("cash.refund.button")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/* ---------------- Расход ---------------- */

export function ExpenseDialog({ open, onOpenChange, branchId, hasOpenShift }: DialogBase & { branchId: string; hasOpenShift: boolean }) {
  const qc = useQueryClient();
  const categories = useReference("/expense-categories");
  const [categoryId, setCategoryId] = React.useState("");
  const [amount, setAmount] = React.useState("");
  const [paidAt, setPaidAt] = React.useState(isoDate(new Date()));
  const [fromCash, setFromCash] = React.useState(false);
  const [counterparty, setCounterparty] = React.useState("");
  const [comment, setComment] = React.useState("");
  const [documentUrl, setDocumentUrl] = React.useState("");

  React.useEffect(() => {
    if (open) {
      setCategoryId("");
      setAmount("");
      setPaidAt(isoDate(new Date()));
      setFromCash(hasOpenShift);
      setCounterparty("");
      setComment("");
      setDocumentUrl("");
    }
  }, [open, hasOpenShift]);

  const mutation = useMutation({
    mutationFn: () => {
      const today = isoDate(new Date());
      const body: Schemas["ExpenseRequest"] = {
        branchId,
        categoryId,
        amount: parseMoneyInput(amount),
        // Сегодня — текущее время, иначе полдень выбранной даты.
        paidAt: paidAt === today ? null : new Date(`${paidAt}T12:00:00`).toISOString(),
        fromCash,
        counterparty: counterparty.trim() || null,
        comment: comment.trim() || null,
        documentUrl: documentUrl.trim() || null,
      };
      return api<Expense>("/expenses", { method: "POST", body });
    },
    onSuccess: () => {
      toast.success(t("cash.expenses.done"));
      qc.invalidateQueries({ queryKey: ["cash"] });
      onOpenChange(false);
    },
    onError: (e) => toast.error(apiErrorText(e)),
  });
  const valid = !!categoryId && parseMoneyInput(amount) > 0;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("cash.expenses.title")}</DialogTitle>
        </DialogHeader>
        <form
          id="expense-form"
          className="space-y-3"
          onSubmit={(e) => {
            e.preventDefault();
            if (valid) mutation.mutate();
          }}
        >
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label={t("cash.expenses.category")}>
              <NativeSelect value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
                <option value="">—</option>
                {(categories.data ?? []).map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </NativeSelect>
            </Field>
            <Field label={t("cash.expenses.amount")}>
              <MoneyInput value={amount} onChange={(e) => setAmount(e.target.value)} placeholder="0" />
            </Field>
            <Field label={t("cash.expenses.paidAt")}>
              <Input type="date" value={paidAt} max={isoDate(new Date())} onChange={(e) => setPaidAt(e.target.value)} />
            </Field>
            <Field label={t("cash.expenses.counterparty")}>
              <Input value={counterparty} onChange={(e) => setCounterparty(e.target.value)} maxLength={200} />
            </Field>
          </div>
          <label className="flex items-center gap-2 text-sm">
            <Checkbox checked={fromCash} disabled={!hasOpenShift} onCheckedChange={(v) => setFromCash(v === true)} />
            {t("cash.expenses.fromCash")}
          </label>
          <Field label={t("cash.expenses.comment")}>
            <Input value={comment} onChange={(e) => setComment(e.target.value)} maxLength={500} />
          </Field>
          <Field label={t("cash.expenses.documentUrl")}>
            <Input type="url" value={documentUrl} onChange={(e) => setDocumentUrl(e.target.value)} placeholder="https://" />
          </Field>
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t("common.cancel")}
          </Button>
          <Button type="submit" form="expense-form" loading={mutation.isPending} disabled={!valid}>
            {t("common.save")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
