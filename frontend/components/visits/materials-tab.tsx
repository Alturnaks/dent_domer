"use client";

import * as React from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Plus, Search, Trash2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { TableSkeleton } from "@/components/ui/skeleton";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { apiErrorText } from "@/components/cash/shared";
import { api } from "@/lib/api-client";
import { formatMoney, formatNumber } from "@/lib/format";
import { t } from "@/lib/i18n";
import type { Schemas } from "@/lib/types";

type Visit = Schemas["VisitDto"];
type Material = Schemas["VisitMaterialDto"];
type Row = { key: string; visitItemId: string | null; itemId: string; itemName: string; baseUnit: string; quantity: string; normQuantity: number; cost: number };

const unitLabel = (u: string) => {
  const k = `visits.units.${u}`;
  const s = t(k);
  return s === k ? u : s;
};

function toRows(list: Material[]): Row[] {
  return list.map((m) => ({
    key: m.id,
    visitItemId: m.visitItemId,
    itemId: m.itemId,
    itemName: m.itemName,
    baseUnit: m.baseUnit,
    quantity: String(m.quantity),
    normQuantity: m.normQuantity,
    cost: m.cost,
  }));
}

/** Поиск материала из номенклатуры склада. */
function ItemSearch({ onPick }: { onPick: (i: Schemas["ItemDto"]) => void }) {
  const [q, setQ] = React.useState("");
  const [debounced, setDebounced] = React.useState("");
  React.useEffect(() => {
    const id = setTimeout(() => setDebounced(q.trim()), 250);
    return () => clearTimeout(id);
  }, [q]);
  const items = useQuery({
    queryKey: ["items-search", debounced],
    queryFn: () => api<Schemas["PagedResultOfItemDto"]>("/items", { query: { q: debounced, page_size: 20 } }),
    enabled: debounced.length >= 2,
    retry: false,
  });
  return (
    <div className="rounded-md border">
      <div className="flex items-center gap-2 border-b px-3">
        <Search className="h-4 w-4 text-muted-foreground" />
        <input autoFocus value={q} onChange={(e) => setQ(e.target.value)} placeholder={t("visits.materials.searchItem")} className="h-9 flex-1 bg-transparent text-sm outline-none" />
      </div>
      {debounced.length >= 2 ? (
        <div className="max-h-56 overflow-y-auto p-1">
          {items.isFetching ? <p className="p-2 text-sm text-muted-foreground">{t("common.loading")}</p> : null}
          {(items.data?.items ?? []).map((i) => (
            <button key={i.id} type="button" className="flex w-full justify-between rounded-md px-3 py-1.5 text-left text-sm hover:bg-muted" onClick={() => onPick(i)}>
              <span className="truncate">{i.name}</span>
              <span className="text-xs text-muted-foreground">{i.sku}</span>
            </button>
          ))}
          {!items.isFetching && items.data && items.data.items.length === 0 ? <p className="p-2 text-sm text-muted-foreground">{t("common.notFound")}</p> : null}
        </div>
      ) : null}
    </div>
  );
}

/** Вкладка материалов визита: норма из техкарт, фактический расход редактируется до закрытия. */
export function VisitMaterialsTab({ visit, editable }: { visit: Visit; editable: boolean }) {
  const qc = useQueryClient();
  const materials = useQuery({
    queryKey: ["visit", visit.id, "materials", visit.version, visit.items.length],
    queryFn: () => api<Material[]>(`/visits/${visit.id}/materials`),
    initialData: visit.materials,
  });
  const [rows, setRows] = React.useState<Row[]>(() => toRows(visit.materials));
  const [dirty, setDirty] = React.useState(false);
  const [adding, setAdding] = React.useState(false);

  React.useEffect(() => {
    if (!dirty && materials.data) setRows(toRows(materials.data));
  }, [materials.data, dirty]);

  const serviceName = (id: string | null) => (id ? visit.items.find((i) => i.id === id)?.serviceName : null) ?? t("visits.materials.general");

  const save = useMutation({
    mutationFn: () =>
      api<Visit>(`/visits/${visit.id}/materials`, {
        method: "PUT",
        body: rows.map((r) => ({ visitItemId: r.visitItemId, itemId: r.itemId, quantity: Number(r.quantity.replace(",", ".")) || 0 })),
      }),
    onSuccess: (v) => {
      qc.setQueryData(["visit", visit.id], v);
      setDirty(false);
      setRows(toRows(v.materials));
      toast.success(t("visits.materials.saved"));
    },
    onError: (e) => toast.error(apiErrorText(e)),
  });

  const update = (key: string, quantity: string) => {
    setRows((rs) => rs.map((r) => (r.key === key ? { ...r, quantity } : r)));
    setDirty(true);
  };
  const remove = (key: string) => {
    setRows((rs) => rs.filter((r) => r.key !== key));
    setDirty(true);
  };
  const invalid = rows.some((r) => {
    const n = Number(r.quantity.replace(",", "."));
    return !Number.isFinite(n) || n < 0;
  });

  if (materials.isLoading) return <Card><TableSkeleton rows={3} cols={4} /></Card>;

  return (
    <Card>
      {!editable && visit.status === "Closed" ? <p className="border-b px-4 py-2 text-xs text-muted-foreground">{t("visits.materials.readOnly")}</p> : null}
      {rows.length === 0 ? (
        <div className="p-4 text-sm">
          <p className="font-medium">{t("visits.materials.empty")}</p>
          {editable ? <p className="text-muted-foreground">{t("visits.materials.emptyHint")}</p> : null}
        </div>
      ) : (
        <Table>
          <THead>
            <TR>
              <TH>{t("visits.materials.item")}</TH>
              <TH>{t("visits.materials.forService")}</TH>
              <TH className="text-right">{t("visits.materials.norm")}</TH>
              <TH className="text-right">{t("visits.materials.qty")}</TH>
              <TH>{t("visits.materials.unit")}</TH>
              {!editable ? <TH className="text-right">{t("visits.materials.cost")}</TH> : null}
              {editable ? <TH /> : null}
            </TR>
          </THead>
          <TBody>
            {rows.map((r) => {
              const q = Number(r.quantity.replace(",", "."));
              const over = r.normQuantity > 0 && q > r.normQuantity;
              return (
                <TR key={r.key}>
                  <TD>{r.itemName}</TD>
                  <TD className="text-muted-foreground">{serviceName(r.visitItemId)}</TD>
                  <TD className="tabular text-right text-muted-foreground">{r.normQuantity ? formatNumber(r.normQuantity) : "—"}</TD>
                  <TD className="text-right">
                    {editable ? (
                      <div className="flex items-center justify-end gap-2">
                        {over ? <Badge variant="warning">{t("visits.materials.overNorm")}</Badge> : null}
                        <Input className="h-8 w-24 text-right tabular" inputMode="decimal" value={r.quantity} onChange={(e) => update(r.key, e.target.value)} />
                      </div>
                    ) : (
                      <span className="tabular">{formatNumber(q)}</span>
                    )}
                  </TD>
                  <TD>{unitLabel(r.baseUnit)}</TD>
                  {!editable ? <TD className="tabular text-right">{formatMoney(r.cost)}</TD> : null}
                  {editable ? (
                    <TD className="w-10 text-right">
                      <Button size="icon-sm" variant="ghost" onClick={() => remove(r.key)} title={t("common.delete")}>
                        <Trash2 />
                      </Button>
                    </TD>
                  ) : null}
                </TR>
              );
            })}
          </TBody>
        </Table>
      )}
      {editable ? (
        <div className="space-y-2 border-t p-3">
          {adding ? (
            <ItemSearch
              onPick={(i) => {
                setRows((rs) => [
                  ...rs,
                  { key: `new-${i.id}-${Date.now()}`, visitItemId: null, itemId: i.id, itemName: i.name, baseUnit: i.baseUnit, quantity: "1", normQuantity: 0, cost: 0 },
                ]);
                setDirty(true);
                setAdding(false);
              }}
            />
          ) : null}
          <div className="flex flex-wrap justify-between gap-2">
            <Button size="sm" variant="outline" onClick={() => setAdding((a) => !a)}>
              <Plus /> {t("visits.materials.add")}
            </Button>
            {dirty ? (
              <div className="flex gap-2">
                <Button
                  size="sm"
                  variant="ghost"
                  onClick={() => {
                    setDirty(false);
                    setRows(toRows(materials.data ?? []));
                  }}
                >
                  {t("common.reset")}
                </Button>
                <Button size="sm" loading={save.isPending} disabled={invalid} onClick={() => save.mutate()}>
                  {t("common.save")}
                </Button>
              </div>
            ) : null}
          </div>
        </div>
      ) : null}
    </Card>
  );
}
