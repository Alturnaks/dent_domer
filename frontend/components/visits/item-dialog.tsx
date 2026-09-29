"use client";

import * as React from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Search } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { MoneyInput, moneyInputValue, parseMoneyInput, apiErrorText } from "@/components/cash/shared";
import { api } from "@/lib/api-client";
import { useAuth } from "@/lib/auth";
import { formatMoney } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import { useServices } from "@/lib/queries";
import type { Schemas, ServiceItem } from "@/lib/types";

type Visit = Schemas["VisitDto"];
type VisitItem = Schemas["VisitItemDto"];

/** Разбор номеров зубов (FDI): 11–48 постоянные, 51–85 молочные. null — ошибка. */
export function parseTeeth(value: string): number[] | null {
  const parts = value.split(/[\s,;]+/).filter(Boolean);
  const res: number[] = [];
  for (const p of parts) {
    if (!/^\d{2}$/.test(p)) return null;
    const q = Number(p[0]);
    const n = Number(p[1]);
    if (q < 1 || q > 8 || n < 1 || n > (q >= 5 ? 5 : 8)) return null;
    if (!res.includes(Number(p))) res.push(Number(p));
  }
  return res;
}

/** Выбор услуги из прайса филиала с поиском по названию/коду. */
export function ServicePicker({ branchId, onPick, autoFocus }: { branchId: string; onPick: (s: ServiceItem) => void; autoFocus?: boolean }) {
  const services = useServices(branchId);
  const [q, setQ] = React.useState("");
  const list = React.useMemo(() => {
    const s = q.trim().toLowerCase();
    return (services.data ?? [])
      .filter((x) => x.isActive && (!s || x.name.toLowerCase().includes(s) || x.code.toLowerCase().includes(s)))
      .slice(0, 50);
  }, [services.data, q]);
  return (
    <div className="rounded-md border">
      <div className="flex items-center gap-2 border-b px-3">
        <Search className="h-4 w-4 text-muted-foreground" />
        <input
          autoFocus={autoFocus}
          value={q}
          onChange={(e) => setQ(e.target.value)}
          placeholder={t("visits.items.searchService")}
          className="h-10 flex-1 bg-transparent text-sm outline-none"
        />
      </div>
      <div className="max-h-64 overflow-y-auto p-1">
        {services.isLoading ? <p className="p-2 text-sm text-muted-foreground">{t("common.loading")}</p> : null}
        {!services.isLoading && list.length === 0 ? <p className="p-2 text-sm text-muted-foreground">{t("visits.items.noServices")}</p> : null}
        {list.map((s) => (
          <button
            key={s.id}
            type="button"
            className="flex w-full items-center justify-between gap-3 rounded-md px-3 py-1.5 text-left text-sm hover:bg-muted"
            onClick={() => onPick(s)}
          >
            <span className="min-w-0 truncate">
              <span className="mr-2 text-xs text-muted-foreground">{s.code}</span>
              {s.name}
            </span>
            <span className="shrink-0 tabular text-muted-foreground">{formatMoney(s.price)}</span>
          </button>
        ))}
      </div>
    </div>
  );
}

/** Добавление/изменение позиции открытого визита. */
export function VisitItemDialog({
  open,
  onOpenChange,
  visit,
  item,
}: {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  visit: Visit;
  item?: VisitItem | null;
}) {
  const qc = useQueryClient();
  const { can, me } = useAuth();
  const canDiscount = can(P.discountsApply);
  const canPrice = can(P.pricesManage);
  const [service, setService] = React.useState<{ id: string; name: string; code: string; price: number | null } | null>(null);
  const [qty, setQty] = React.useState("1");
  const [teeth, setTeeth] = React.useState("");
  const [discount, setDiscount] = React.useState("");
  const [price, setPrice] = React.useState("");

  React.useEffect(() => {
    if (!open) return;
    setService(item ? { id: item.serviceId, name: item.serviceName, code: item.serviceCode, price: item.unitPrice } : null);
    setQty(String(item?.qty ?? 1));
    setTeeth((item?.toothNumbers ?? []).join(", "));
    setDiscount(item && item.discountPct ? String(item.discountPct) : "");
    setPrice(item ? moneyInputValue(item.unitPrice) : "");
  }, [open, item]);

  const parsedTeeth = parseTeeth(teeth);
  const qtyNum = Number(qty);
  const discountNum = discount.trim() === "" ? 0 : Number(discount.replace(",", "."));
  const discountValid = Number.isFinite(discountNum) && discountNum >= 0 && discountNum <= 100;
  const valid = !!service && parsedTeeth !== null && Number.isInteger(qtyNum) && qtyNum >= 1 && discountValid;
  const limit = me?.limits.maxDiscountPct ?? 0;

  const mutation = useMutation({
    mutationFn: () => {
      const priceMinor = canPrice && price.trim() !== "" ? parseMoneyInput(price) : null;
      const body: Schemas["VisitItemRequest"] = {
        serviceId: service!.id,
        qty: qtyNum,
        discountPct: canDiscount ? discountNum : item ? null : 0,
        doctorId: null,
        toothNumbers: parsedTeeth,
        unitPrice: priceMinor !== null && priceMinor !== (item?.unitPrice ?? service!.price) ? priceMinor : null,
      };
      return item
        ? api<Visit>(`/visits/${visit.id}/items/${item.id}`, { method: "PATCH", body })
        : api<Visit>(`/visits/${visit.id}/items`, { method: "POST", body });
    },
    onSuccess: (v) => {
      qc.setQueryData(["visit", visit.id], v);
      qc.invalidateQueries({ queryKey: ["visit", visit.id, "materials"] });
      if (v.approvalState === "PendingDiscount" && v.items.some((i) => i.discountPendingApproval && (!item || i.id === item.id)))
        toast.warning(t("visits.items.discountRequested"));
      else toast.success(item ? t("visits.items.updated") : t("visits.items.added"));
      onOpenChange(false);
    },
    onError: (e) => toast.error(apiErrorText(e)),
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{item ? t("visits.items.edit") : t("visits.items.add")}</DialogTitle>
          {service ? (
            <DialogDescription>
              {service.code} · {service.name}
            </DialogDescription>
          ) : null}
        </DialogHeader>

        {!service ? (
          <ServicePicker
            autoFocus
            branchId={visit.branchId}
            onPick={(s) => {
              setService({ id: s.id, name: s.name, code: s.code, price: s.price });
              setPrice(moneyInputValue(s.price));
            }}
          />
        ) : (
          <form
            id="visit-item-form"
            className="space-y-3"
            onSubmit={(e) => {
              e.preventDefault();
              if (valid) mutation.mutate();
            }}
          >
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label={t("visits.items.teeth")} error={parsedTeeth === null ? t("visits.items.teethInvalid") : undefined} hint={t("visits.items.teethHint")}>
                <Input autoFocus value={teeth} onChange={(e) => setTeeth(e.target.value)} aria-invalid={parsedTeeth === null} />
              </Field>
              <Field label={t("visits.items.qty")}>
                <Input type="number" min={1} step={1} value={qty} onChange={(e) => setQty(e.target.value)} />
              </Field>
              {canPrice ? (
                <Field label={t("visits.items.unitPrice")}>
                  <MoneyInput value={price} onChange={(e) => setPrice(e.target.value)} />
                </Field>
              ) : (
                <Field label={t("visits.items.priceFromList")}>
                  <div className="flex h-9 items-center text-sm tabular">{formatMoney(item?.unitPrice ?? service.price)}</div>
                </Field>
              )}
              {canDiscount ? (
                <Field
                  label={t("visits.items.discountPct")}
                  hint={t("visits.items.discountLimit", { pct: limit })}
                  error={!discountValid ? t("errors.DISCOUNT_INVALID") : undefined}
                >
                  <Input inputMode="decimal" value={discount} onChange={(e) => setDiscount(e.target.value)} placeholder="0" aria-invalid={!discountValid} />
                </Field>
              ) : null}
            </div>
            {!item ? (
              <Button type="button" variant="link" size="sm" className="px-0" onClick={() => setService(null)}>
                {t("visits.items.searchService")}
              </Button>
            ) : null}
          </form>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t("common.cancel")}
          </Button>
          <Button type="submit" form="visit-item-form" loading={mutation.isPending} disabled={!valid}>
            {item ? t("common.save") : t("common.add")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
