"use client";

import * as React from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { NativeSelect } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { api } from "@/lib/api-client";
import { t } from "@/lib/i18n";
import { apiErrorText, cashKeys, MoneyInput, parseMoneyInput, useCashRegisters, useOpenShifts, type CashShift } from "./shared";

/** Форма открытия смены: касса филиала + остаток на начало. Кассы с уже открытой сменой скрыты. */
export function OpenShiftForm({ branchId, onOpened, onCancel }: { branchId: string; onOpened?: (s: CashShift) => void; onCancel?: () => void }) {
  const qc = useQueryClient();
  const registers = useCashRegisters(branchId);
  const open = useOpenShifts(branchId);
  const busy = new Set((open.data ?? []).map((s) => s.cashRegisterId));
  const available = (registers.data ?? []).filter((r) => !busy.has(r.id));
  const [registerId, setRegisterId] = React.useState("");
  const [opening, setOpening] = React.useState("");

  React.useEffect(() => {
    if (!registerId && available.length > 0) setRegisterId(available[0].id);
  }, [available, registerId]);

  const mutation = useMutation({
    mutationFn: () => api<CashShift>("/cash-shifts/open", { method: "POST", body: { cashRegisterId: registerId, openingBalance: parseMoneyInput(opening) } }),
    onSuccess: (s) => {
      toast.success(t("cash.shift.opened"));
      qc.invalidateQueries({ queryKey: ["cash"] });
      qc.setQueryData(cashKeys.openShifts(branchId), (old: CashShift[] | undefined) => [...(old ?? []).filter((x) => x.id !== s.id), s]);
      onOpened?.(s);
    },
    onError: (e) => toast.error(apiErrorText(e)),
  });

  if (registers.isLoading) return <p className="text-sm text-muted-foreground">{t("common.loading")}</p>;
  if ((registers.data ?? []).length === 0) return <p className="text-sm text-muted-foreground">{t("cash.noRegisters")}</p>;

  return (
    <form
      className="space-y-3"
      onSubmit={(e) => {
        e.preventDefault();
        if (registerId) mutation.mutate();
      }}
    >
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label={t("cash.register")}>
          <NativeSelect value={registerId} onChange={(e) => setRegisterId(e.target.value)}>
            {available.map((r) => (
              <option key={r.id} value={r.id}>
                {r.name}
              </option>
            ))}
          </NativeSelect>
        </Field>
        <Field label={t("cash.shift.openingBalance")}>
          <MoneyInput value={opening} onChange={(e) => setOpening(e.target.value)} placeholder="0" />
        </Field>
      </div>
      <div className="flex justify-end gap-2">
        {onCancel ? (
          <Button type="button" variant="outline" onClick={onCancel}>
            {t("common.cancel")}
          </Button>
        ) : null}
        <Button type="submit" loading={mutation.isPending} disabled={!registerId}>
          {t("cash.shift.open")}
        </Button>
      </div>
    </form>
  );
}

export function OpenShiftDialog({ open, onOpenChange, branchId }: { open: boolean; onOpenChange: (o: boolean) => void; branchId: string }) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("cash.shift.openTitle")}</DialogTitle>
        </DialogHeader>
        {open ? <OpenShiftForm branchId={branchId} onOpened={() => onOpenChange(false)} onCancel={() => onOpenChange(false)} /> : null}
      </DialogContent>
    </Dialog>
  );
}
