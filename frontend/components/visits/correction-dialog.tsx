"use client";

import * as React from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input, Textarea } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { apiErrorText, MoneyInput, moneyInputValue, parseMoneyInput } from "@/components/cash/shared";
import { api } from "@/lib/api-client";
import { formatMoney } from "@/lib/format";
import { t } from "@/lib/i18n";
import type { Schemas } from "@/lib/types";
import { parseTeeth, ServicePicker } from "./item-dialog";

type Visit = Schemas["VisitDto"];
type Row = { key: string; serviceId: string; name: string; doctorId: string | null; qty: string; price: string; discount: string; teeth: string };

function rowTotal(r: Row): number {
  const qty = Number(r.qty) || 0;
  const gross = parseMoneyInput(r.price) * qty;
  const pct = Number(r.discount.replace(",", ".")) || 0;
  return gross - Math.round((gross * pct) / 100);
}

/** Корректировка закрытого визита (visits.edit_closed): новый состав позиций + причина. */
export function VisitCorrectionDialog({ open, onOpenChange, visit }: { open: boolean; onOpenChange: (o: boolean) => void; visit: Visit }) {
  const qc = useQueryClient();
  const [rows, setRows] = React.useState<Row[]>([]);
  const [reason, setReason] = React.useState("");
  const [picking, setPicking] = React.useState(false);

  React.useEffect(() => {
    if (!open) return;
    setRows(
      visit.items.map((i) => ({
        key: i.id,
        serviceId: i.serviceId,
        name: i.serviceName,
        doctorId: i.doctorId,
        qty: String(i.qty),
        price: moneyInputValue(i.unitPrice),
        discount: i.discountPct ? String(i.discountPct) : "",
        teeth: i.toothNumbers.join(", "),
      })),
    );
    setReason("");
    setPicking(false);
  }, [open, visit]);

  const update = (key: string, patch: Partial<Row>) => setRows((rs) => rs.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  const rowValid = (r: Row) => {
    const q = Number(r.qty);
    const d = r.discount.trim() === "" ? 0 : Number(r.discount.replace(",", "."));
    return Number.isInteger(q) && q >= 1 && parseMoneyInput(r.price) >= 0 && d >= 0 && d <= 100 && parseTeeth(r.teeth) !== null;
  };
  const valid = rows.length > 0 && rows.every(rowValid) && reason.trim().length > 0;
  const newTotal = rows.reduce((s, r) => s + rowTotal(r), 0);

  const mutation = useMutation({
    mutationFn: () =>
      api<Visit>(`/visits/${visit.id}/corrections`, {
        method: "POST",
        body: {
          items: rows.map((r) => ({
            serviceId: r.serviceId,
            qty: Number(r.qty),
            unitPrice: parseMoneyInput(r.price),
            discountPct: r.discount.trim() === "" ? 0 : Number(r.discount.replace(",", ".")),
            doctorId: r.doctorId,
            toothNumbers: parseTeeth(r.teeth) ?? [],
          })),
          materials: null,
          reason: reason.trim(),
          version: visit.version,
        } satisfies Schemas["VisitCorrectionRequest"],
      }),
    onSuccess: (v) => {
      qc.setQueryData(["visit", visit.id], v);
      qc.invalidateQueries({ queryKey: ["patient", v.patientId] });
      toast[v.approvalState === "PendingCorrection" ? "warning" : "success"](
        v.approvalState === "PendingCorrection" ? t("visits.correction.requested") : t("visits.correction.applied"),
      );
      onOpenChange(false);
    },
    onError: (e) => {
      toast.error(apiErrorText(e));
      qc.invalidateQueries({ queryKey: ["visit", visit.id] });
    },
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent wide>
        <DialogHeader>
          <DialogTitle>{t("visits.correction.title")}</DialogTitle>
          <DialogDescription>{t("visits.correction.hint")}</DialogDescription>
        </DialogHeader>
        <div className="rounded-md border">
          <Table>
            <THead>
              <TR>
                <TH>{t("visits.items.service")}</TH>
                <TH>{t("visits.items.tooth")}</TH>
                <TH className="w-20">{t("visits.items.qty")}</TH>
                <TH className="w-32">{t("visits.items.price")}</TH>
                <TH className="w-20">{t("visits.items.discount")}, %</TH>
                <TH className="text-right">{t("visits.items.total")}</TH>
                <TH />
              </TR>
            </THead>
            <TBody>
              {rows.map((r) => (
                <TR key={r.key}>
                  <TD className="min-w-[10rem]">{r.name}</TD>
                  <TD>
                    <Input className="h-8 w-24" value={r.teeth} onChange={(e) => update(r.key, { teeth: e.target.value })} aria-invalid={parseTeeth(r.teeth) === null} />
                  </TD>
                  <TD>
                    <Input className="h-8" type="number" min={1} value={r.qty} onChange={(e) => update(r.key, { qty: e.target.value })} />
                  </TD>
                  <TD>
                    <MoneyInput className="h-8" value={r.price} onChange={(e) => update(r.key, { price: e.target.value })} />
                  </TD>
                  <TD>
                    <Input className="h-8" inputMode="decimal" value={r.discount} onChange={(e) => update(r.key, { discount: e.target.value })} placeholder="0" />
                  </TD>
                  <TD className="tabular whitespace-nowrap text-right">{formatMoney(rowTotal(r))}</TD>
                  <TD>
                    <Button size="icon-sm" variant="ghost" onClick={() => setRows((rs) => rs.filter((x) => x.key !== r.key))} title={t("common.delete")}>
                      <Trash2 />
                    </Button>
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
        </div>
        {picking ? (
          <ServicePicker
            autoFocus
            branchId={visit.branchId}
            onPick={(s) => {
              setRows((rs) => [
                ...rs,
                { key: `new-${s.id}-${Date.now()}`, serviceId: s.id, name: s.name, doctorId: null, qty: "1", price: moneyInputValue(s.price), discount: "", teeth: "" },
              ]);
              setPicking(false);
            }}
          />
        ) : null}
        <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
          <Button size="sm" variant="outline" onClick={() => setPicking((p) => !p)}>
            <Plus /> {t("visits.items.add")}
          </Button>
          <span>
            {formatMoney(visit.total)} → <span className="font-semibold tabular">{formatMoney(newTotal)}</span>
          </span>
        </div>
        {rows.length === 0 ? <p className="text-xs text-destructive">{t("visits.correction.noItems")}</p> : null}
        <Field label={t("visits.correction.reason")}>
          <Textarea value={reason} onChange={(e) => setReason(e.target.value)} rows={2} maxLength={500} />
        </Field>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t("common.cancel")}
          </Button>
          <Button loading={mutation.isPending} disabled={!valid} onClick={() => mutation.mutate()}>
            {t("common.save")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
