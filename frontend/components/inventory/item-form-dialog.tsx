"use client";

import * as React from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { TableSkeleton } from "@/components/ui/skeleton";
import { api, ApiError } from "@/lib/api-client";
import { t } from "@/lib/i18n";
import type { Schemas } from "@/lib/types";
import { BASE_UNITS, categoryOptions, parseNum, useItemCategories, useWarehouses, type BaseUnit, type Item } from "./shared";

type UnitRow = { key: string; id: string | null; unitName: string; factor: string };

const errText = (e: unknown) => (e instanceof ApiError ? e.userMessage : t("errors.INTERNAL_ERROR"));

/** Создание / редактирование товара. item = null — новый. */
export function ItemFormDialog({ open, onOpenChange, item }: { open: boolean; onOpenChange: (o: boolean) => void; item: Item | null }) {
  const qc = useQueryClient();
  const categories = useItemCategories();
  const [name, setName] = React.useState("");
  const [sku, setSku] = React.useState("");
  const [categoryId, setCategoryId] = React.useState("");
  const [manufacturer, setManufacturer] = React.useState("");
  const [barcode, setBarcode] = React.useState("");
  const [baseUnit, setBaseUnit] = React.useState<BaseUnit>("Pcs");
  const [trackBatches, setTrackBatches] = React.useState(false);
  const [trackExpiry, setTrackExpiry] = React.useState(false);
  const [trackSerials, setTrackSerials] = React.useState(false);
  const [isActive, setIsActive] = React.useState(true);
  const [units, setUnits] = React.useState<UnitRow[]>([]);
  const [errors, setErrors] = React.useState<Record<string, string>>({});
  const [busy, setBusy] = React.useState(false);

  React.useEffect(() => {
    if (!open) return;
    setName(item?.name ?? "");
    setSku(item?.sku ?? "");
    setCategoryId(item?.categoryId ?? "");
    setManufacturer(item?.manufacturer ?? "");
    setBarcode(item?.barcode ?? "");
    setBaseUnit(item?.baseUnit ?? "Pcs");
    setTrackBatches(item?.trackBatches ?? false);
    setTrackExpiry(item?.trackExpiry ?? false);
    setTrackSerials(item?.trackSerials ?? false);
    setIsActive(item?.isActive ?? true);
    setUnits((item?.units ?? []).map((u) => ({ key: u.id, id: u.id, unitName: u.unitName, factor: String(u.factorToBase) })));
    setErrors({});
  }, [open, item]);

  const submit = async () => {
    const e: Record<string, string> = {};
    if (!name.trim()) e.name = t("common.required");
    if (!sku.trim()) e.sku = t("common.required");
    units.forEach((u) => {
      const f = parseNum(u.factor);
      if (!u.unitName.trim() || f === null || f <= 0) e[`unit:${u.key}`] = t("common.required");
    });
    setErrors(e);
    if (Object.keys(e).length) return;
    const body: Schemas["ItemRequest"] = {
      name: name.trim(),
      sku: sku.trim(),
      categoryId: categoryId || null,
      manufacturer: manufacturer.trim() || null,
      barcode: barcode.trim() || null,
      baseUnit,
      trackBatches: trackBatches || trackExpiry,
      trackExpiry,
      trackSerials,
      isActive,
      units: units.map((u) => ({ id: u.id, unitName: u.unitName.trim(), factorToBase: parseNum(u.factor) ?? 1 })),
    };
    setBusy(true);
    try {
      await api<Item>(item ? `/items/${item.id}` : "/items", { method: item ? "PATCH" : "POST", body });
      toast.success(item ? t("inventory.items.updated") : t("inventory.items.created"));
      qc.invalidateQueries({ queryKey: ["items"] });
      qc.invalidateQueries({ queryKey: ["stock"] });
      onOpenChange(false);
    } catch (err) {
      toast.error(errText(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent wide>
        <DialogHeader>
          <DialogTitle>{item ? t("inventory.items.edit") : t("inventory.items.new")}</DialogTitle>
        </DialogHeader>
        <form
          className="grid gap-3 sm:grid-cols-2"
          onSubmit={(ev) => {
            ev.preventDefault();
            submit();
          }}
        >
          <Field label={t("inventory.items.name")} error={errors.name} className="sm:col-span-2">
            <Input value={name} autoFocus aria-invalid={!!errors.name} onChange={(e) => setName(e.target.value)} />
          </Field>
          <Field label={t("inventory.items.sku")} error={errors.sku}>
            <Input value={sku} aria-invalid={!!errors.sku} onChange={(e) => setSku(e.target.value)} />
          </Field>
          <Field label={t("inventory.items.category")}>
            <NativeSelect value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
              <option value="">{t("inventory.items.noCategory")}</option>
              {categoryOptions(categories.data).map((c) => (
                <option key={c.id} value={c.id}>
                  {c.label}
                </option>
              ))}
            </NativeSelect>
          </Field>
          <Field label={t("inventory.items.manufacturer")}>
            <Input value={manufacturer} onChange={(e) => setManufacturer(e.target.value)} />
          </Field>
          <Field label={t("inventory.items.barcode")}>
            <Input value={barcode} onChange={(e) => setBarcode(e.target.value)} />
          </Field>
          <Field label={t("inventory.items.baseUnit")}>
            <NativeSelect value={baseUnit} onChange={(e) => setBaseUnit(e.target.value as BaseUnit)}>
              {BASE_UNITS.map((u) => (
                <option key={u} value={u}>
                  {t(`inventory.unitsFull.${u}`)}
                </option>
              ))}
            </NativeSelect>
          </Field>
          <Field label={t("inventory.items.tracking")}>
            <div className="flex h-9 flex-wrap items-center gap-4 text-sm">
              <label className="flex items-center gap-2">
                <Checkbox checked={trackBatches || trackExpiry} disabled={trackExpiry} onCheckedChange={(v) => setTrackBatches(v === true)} />
                {t("inventory.items.trackBatches")}
              </label>
              <label className="flex items-center gap-2">
                <Checkbox checked={trackExpiry} onCheckedChange={(v) => setTrackExpiry(v === true)} />
                {t("inventory.items.trackExpiry")}
              </label>
              <label className="flex items-center gap-2">
                <Checkbox checked={trackSerials} onCheckedChange={(v) => setTrackSerials(v === true)} />
                {t("inventory.items.trackSerials")}
              </label>
            </div>
          </Field>

          <div className="space-y-2 sm:col-span-2">
            <div>
              <div className="text-sm font-medium">{t("inventory.items.altUnits")}</div>
              <p className="text-xs text-muted-foreground">{t("inventory.items.altUnitsHint")}</p>
            </div>
            {units.map((u) => (
              <div key={u.key} className="flex items-center gap-2">
                <Input
                  className="flex-1"
                  placeholder={t("inventory.items.unitName")}
                  aria-invalid={!!errors[`unit:${u.key}`] && !u.unitName.trim()}
                  value={u.unitName}
                  onChange={(e) => setUnits((s) => s.map((x) => (x.key === u.key ? { ...x, unitName: e.target.value } : x)))}
                />
                <span className="text-sm text-muted-foreground">=</span>
                <Input
                  className="w-28 text-right"
                  inputMode="decimal"
                  aria-invalid={!!errors[`unit:${u.key}`]}
                  value={u.factor}
                  onChange={(e) => setUnits((s) => s.map((x) => (x.key === u.key ? { ...x, factor: e.target.value } : x)))}
                />
                <span className="w-8 text-sm text-muted-foreground">{t(`inventory.units.${baseUnit}`)}</span>
                <Button type="button" variant="ghost" size="icon-sm" aria-label={t("common.delete")} onClick={() => setUnits((s) => s.filter((x) => x.key !== u.key))}>
                  <Trash2 />
                </Button>
              </div>
            ))}
            <Button type="button" variant="outline" size="sm" onClick={() => setUnits((s) => [...s, { key: `n${Date.now()}`, id: null, unitName: "", factor: "" }])}>
              <Plus /> {t("inventory.items.addUnit")}
            </Button>
          </div>

          {item ? (
            <label className="flex items-center gap-2 text-sm sm:col-span-2">
              <Checkbox checked={isActive} onCheckedChange={(v) => setIsActive(v === true)} />
              {t("inventory.items.isActive")}
            </label>
          ) : null}

          <DialogFooter className="sm:col-span-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" loading={busy} disabled={busy}>
              {t("common.save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/** Минимальные / оптимальные остатки товара по складам. */
export function StockLevelsDialog({ item, onOpenChange }: { item: Item | null; onOpenChange: (o: boolean) => void }) {
  const qc = useQueryClient();
  const warehouses = useWarehouses();
  const levels = useQuery({
    queryKey: ["items", item?.id, "stock-levels"],
    queryFn: () => api<Schemas["StockLevelDto"][]>(`/items/${item!.id}/stock-levels`),
    enabled: !!item,
  });
  const [values, setValues] = React.useState<Record<string, { min: string; opt: string }>>({});
  const [busy, setBusy] = React.useState(false);

  React.useEffect(() => {
    if (!levels.data) return;
    setValues(Object.fromEntries(levels.data.map((l) => [l.warehouseId, { min: String(l.minQty), opt: String(l.optimalQty) }])));
  }, [levels.data]);

  const set = (wid: string, p: Partial<{ min: string; opt: string }>) =>
    setValues((s) => ({ ...s, [wid]: { min: s[wid]?.min ?? "", opt: s[wid]?.opt ?? "", ...p } }));

  const save = async () => {
    if (!item) return;
    const body: Schemas["StockLevelInput"][] = Object.entries(values)
      .map(([warehouseId, v]) => ({ warehouseId, minQty: parseNum(v.min) ?? 0, optimalQty: parseNum(v.opt) ?? 0 }))
      .filter((l) => l.minQty > 0 || l.optimalQty > 0);
    setBusy(true);
    try {
      await api(`/items/${item.id}/stock-levels`, { method: "PUT", body });
      toast.success(t("inventory.items.levelsSaved"));
      qc.invalidateQueries({ queryKey: ["items", item.id, "stock-levels"] });
      qc.invalidateQueries({ queryKey: ["stock"] });
      onOpenChange(false);
    } catch (e) {
      toast.error(errText(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={!!item} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("inventory.items.stockLevelsTitle", { name: item?.name ?? "" })}</DialogTitle>
          <DialogDescription>{t("inventory.items.stockLevelsHint")}</DialogDescription>
        </DialogHeader>
        {levels.isLoading || warehouses.isLoading ? (
          <TableSkeleton rows={4} cols={3} />
        ) : (
          <div className="rounded-lg border">
            <Table>
              <THead>
                <TR>
                  <TH>{t("inventory.balances.warehouse")}</TH>
                  <TH className="w-28 text-right">{t("inventory.items.minQty")}</TH>
                  <TH className="w-28 text-right">{t("inventory.items.optimalQty")}</TH>
                </TR>
              </THead>
              <TBody>
                {(warehouses.data ?? []).map((w) => (
                  <TR key={w.id}>
                    <TD>{w.name}</TD>
                    <TD>
                      <Input className="h-8 text-right" inputMode="decimal" placeholder="0" value={values[w.id]?.min ?? ""} onChange={(e) => set(w.id, { min: e.target.value })} />
                    </TD>
                    <TD>
                      <Input className="h-8 text-right" inputMode="decimal" placeholder="0" value={values[w.id]?.opt ?? ""} onChange={(e) => set(w.id, { opt: e.target.value })} />
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          </div>
        )}
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t("common.cancel")}
          </Button>
          <Button loading={busy} disabled={busy || levels.isLoading} onClick={save}>
            {t("common.save")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
