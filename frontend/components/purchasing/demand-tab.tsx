"use client";

import * as React from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { AlertTriangle, ArrowRightLeft, Ban, MoreHorizontal, ShoppingCart } from "lucide-react";
import { EmptyState } from "@/components/ui/empty-state";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect, Textarea } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Card } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { TableSkeleton } from "@/components/ui/skeleton";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { parseNum, unitLabel } from "@/components/inventory/shared";
import { api, newIdempotencyKey } from "@/lib/api-client";
import { useAuth, useOrgHref } from "@/lib/auth";
import { formatMoney, formatNumber } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import { cn } from "@/lib/utils";
import type { Schemas } from "@/lib/types";
import { errText, type DemandRow, type NetworkDemand } from "./shared";

type Action = Schemas["DemandAction"];
type RowState = { qty: string; supplierId: string };
const rowKey = (r: { itemId: string; warehouseId: string }) => `${r.itemId}:${r.warehouseId}`;

export function DemandTab() {
  const qc = useQueryClient();
  const { can } = useAuth();
  const { push } = useOrgHref();
  const [centralId, setCentralId] = React.useState("");
  const [state, setState] = React.useState<Record<string, RowState>>({});
  const [selected, setSelected] = React.useState<Set<string>>(new Set());
  const [deliverTo, setDeliverTo] = React.useState<"branches" | "central">("branches");
  const [busy, setBusy] = React.useState(false);
  const [rejectKeys, setRejectKeys] = React.useState<string[] | null>(null);
  const [rejectComment, setRejectComment] = React.useState("");

  const query = useQuery({
    queryKey: ["network-demand", centralId],
    queryFn: () => api<NetworkDemand>("/purchase-requests/network-demand", { query: { central_warehouse_id: centralId || undefined } }),
  });
  const data = query.data;
  const canOrder = can(P.purchaseOrder);
  const canTransfer = can(P.transferCreate) && canOrder;

  const items = React.useMemo(() => new Map((data?.items ?? []).map((i) => [i.itemId, i])), [data]);

  // Состояние строк по умолчанию: вся потребность, лучший поставщик.
  React.useEffect(() => {
    if (!data) return;
    setState((prev) => {
      const next: Record<string, RowState> = {};
      for (const r of data.rows) {
        const k = rowKey(r);
        next[k] = prev[k] ?? { qty: String(r.qty), supplierId: items.get(r.itemId)?.bestSupplierId ?? "" };
      }
      return next;
    });
    setSelected((prev) => new Set([...prev].filter((k) => data.rows.some((r) => rowKey(r) === k))));
  }, [data, items]);

  const rowsByItem = React.useMemo(() => {
    const m = new Map<string, DemandRow[]>();
    for (const r of data?.rows ?? []) m.set(r.itemId, [...(m.get(r.itemId) ?? []), r]);
    return [...m.entries()];
  }, [data]);

  const run = async (action: Action, keys: string[], comment?: string) => {
    if (!data || keys.length === 0) return;
    const rows = data.rows
      .filter((r) => keys.includes(rowKey(r)))
      .map((r) => {
        const s = state[rowKey(r)];
        return { itemId: r.itemId, warehouseId: r.warehouseId, qty: parseNum(s?.qty ?? "") ?? r.qty, supplierId: s?.supplierId || null, unitPrice: null };
      });
    if (action === "Order") {
      const missing = rows.find((r) => !r.supplierId);
      if (missing) {
        toast.error(`${items.get(missing.itemId)?.itemName ?? ""}: ${t("purchasing.demand.chooseSupplier")}`);
        return;
      }
    }
    setBusy(true);
    try {
      const res = await api<Schemas["ProcessDemandResult"]>("/purchase-requests/process", {
        method: "POST",
        idempotencyKey: newIdempotencyKey(),
        body: {
          action,
          rows,
          requestIds: null,
          supplierId: null,
          fromWarehouseId: data.centralWarehouseId ?? null,
          deliverToWarehouseId: action === "Order" && deliverTo === "central" ? (data.centralWarehouseId ?? null) : null,
          comment: comment ?? null,
        } satisfies Schemas["ProcessDemandRequest"],
      });
      const numbers = res.numbers.join(", ");
      if (action === "Transfer") {
        const first = res.transferIds[0];
        toast.success(t("purchasing.demand.transferDone", { numbers }), {
          duration: 10000,
          action: first ? { label: t("purchasing.demand.openDoc"), onClick: () => push(`inventory/documents/${first}`) } : undefined,
        });
      } else if (action === "Order") {
        const first = res.orderIds[0];
        toast.success(t("purchasing.demand.orderDone", { numbers }), {
          duration: 10000,
          action: first ? { label: t("purchasing.demand.openDoc"), onClick: () => push(`purchasing/orders/${first}`) } : undefined,
        });
      } else toast.success(t("purchasing.demand.rejectDone"));
      setSelected(new Set());
      setRejectKeys(null);
      setRejectComment("");
      qc.invalidateQueries({ queryKey: ["network-demand"] });
      qc.invalidateQueries({ queryKey: ["purchase-requests"] });
      qc.invalidateQueries({ queryKey: ["purchase-orders"] });
      qc.invalidateQueries({ queryKey: ["stock-documents"] });
    } catch (e) {
      toast.error(errText(e));
    } finally {
      setBusy(false);
    }
  };

  const toggle = (k: string, on: boolean) =>
    setSelected((s) => {
      const n = new Set(s);
      if (on) n.add(k);
      else n.delete(k);
      return n;
    });

  if (query.isLoading) return <Card><TableSkeleton cols={8} /></Card>;
  if (query.isError)
    return (
      <EmptyState
        icon={<AlertTriangle className="h-8 w-8" />}
        title={errText(query.error)}
        action={<Button variant="outline" onClick={() => query.refetch()}>{t("common.retry")}</Button>}
      />
    );
  if (!data) return null;

  const selectedKeys = [...selected];
  const allKeys = data.rows.map(rowKey);

  return (
    <div>
      <Card className="mb-3 flex flex-wrap items-center gap-3 p-3 text-sm">
        <span className="text-muted-foreground">{t("purchasing.demand.hint")}</span>
        <div className="ml-auto flex flex-wrap items-center gap-3">
          {data.centralWarehouses.length > 1 ? (
            <NativeSelect className="w-52" value={centralId || data.centralWarehouseId || ""} onChange={(e) => setCentralId(e.target.value)} aria-label={t("purchasing.demand.central")}>
              {data.centralWarehouses.map((w) => (
                <option key={w.id} value={w.id}>
                  {w.name}
                </option>
              ))}
            </NativeSelect>
          ) : data.centralWarehouses.length === 1 ? (
            <span>
              {t("purchasing.demand.central")}: <b>{data.centralWarehouses[0].name}</b>
            </span>
          ) : (
            <span className="text-warning">{t("purchasing.demand.noCentral")}</span>
          )}
          <span className="text-muted-foreground">{t("purchasing.demand.requests", { count: data.requestsCount })}</span>
          {data.estimatedTotal > 0 ? <span className="font-medium">{t("purchasing.demand.estimated", { amount: formatMoney(data.estimatedTotal) })}</span> : null}
        </div>
      </Card>

      {selectedKeys.length > 0 && canOrder ? (
        <Card className="sticky top-2 z-20 mb-3 flex flex-wrap items-center gap-3 border-primary/40 p-3 text-sm shadow-md">
          <span className="font-medium">{t("purchasing.demand.selected", { count: selectedKeys.length })}</span>
          <div className="flex items-center gap-2">
            <span className="text-muted-foreground">{t("purchasing.demand.deliverTo")}</span>
            <NativeSelect className="h-8 w-52" value={deliverTo} onChange={(e) => setDeliverTo(e.target.value as "branches" | "central")}>
              <option value="branches">{t("purchasing.demand.deliverToBranches")}</option>
              {data.centralWarehouseId ? <option value="central">{t("purchasing.demand.deliverToCentral")}</option> : null}
            </NativeSelect>
          </div>
          <div className="ml-auto flex flex-wrap gap-2">
            {canTransfer && data.centralWarehouseId ? (
              <Button size="sm" variant="outline" disabled={busy} onClick={() => run("Transfer", selectedKeys)}>
                <ArrowRightLeft /> {t("purchasing.demand.bulkTransfer")}
              </Button>
            ) : null}
            <Button size="sm" disabled={busy} onClick={() => run("Order", selectedKeys)} data-testid="bulk-order">
              <ShoppingCart /> {t("purchasing.demand.bulkOrder")}
            </Button>
            <Button size="sm" variant="ghost" className="text-destructive" disabled={busy} onClick={() => setRejectKeys(selectedKeys)}>
              <Ban /> {t("purchasing.demand.bulkReject")}
            </Button>
          </div>
        </Card>
      ) : null}

      <Card>
        {data.rows.length === 0 ? (
          <EmptyState className="m-4" title={t("purchasing.demand.empty")} description={t("purchasing.demand.emptyHint")} />
        ) : (
          <Table>
            <THead>
              <TR>
                <TH className="w-8">
                  {canOrder ? (
                    <Checkbox
                      checked={selectedKeys.length > 0 && selectedKeys.length === allKeys.length}
                      onCheckedChange={(v) => setSelected(v ? new Set(allKeys) : new Set())}
                      aria-label={t("common.all")}
                    />
                  ) : null}
                </TH>
                <TH>{t("purchasing.demand.branch")}</TH>
                <TH className="text-right">{t("purchasing.demand.stock")}</TH>
                <TH className="text-right">{t("purchasing.demand.minOpt")}</TH>
                <TH className="text-right">{t("purchasing.demand.need")}</TH>
                <TH className="w-28 text-right">{t("purchasing.demand.qty")}</TH>
                <TH className="min-w-56">{t("purchasing.demand.supplier")}</TH>
                <TH className="w-10" />
              </TR>
            </THead>
            <TBody>
              {rowsByItem.map(([itemId, rows]) => {
                const item = items.get(itemId);
                const need = item?.totalQty ?? 0;
                const central = item?.centralQty ?? 0;
                return (
                  <React.Fragment key={itemId}>
                    <TR className="bg-muted/40 hover:bg-muted/40">
                      <TD />
                      <TD colSpan={3}>
                        <div className="font-medium">{item?.itemName}</div>
                        <div className="text-xs text-muted-foreground">
                          {item?.itemSku}
                          {item && item.prices.length > 0 ? (
                            <>
                              {" · "}
                              {t("purchasing.demand.prices")}:{" "}
                              {item.prices.map((p, i) => (
                                <span key={p.supplierId} className={cn(p.supplierId === item.bestSupplierId && "font-medium text-foreground")}>
                                  {i > 0 ? "; " : ""}
                                  {p.supplierName} — {p.lastPrice > 0 ? formatMoney(p.lastPrice) : t("purchasing.demand.noPrice")}
                                </span>
                              ))}
                            </>
                          ) : null}
                        </div>
                      </TD>
                      <TD className="tabular text-right font-medium">
                        {formatNumber(need)} {unitLabel(item?.baseUnit)}
                      </TD>
                      <TD colSpan={3} className={cn("text-sm", central < need ? "text-warning" : "text-muted-foreground")}>
                        {t("purchasing.demand.centralStock")}: <span className="tabular font-medium">{formatNumber(central)}</span>
                        {central > 0 && central < need ? ` · ${t("purchasing.demand.notEnoughCentral")}` : ""}
                      </TD>
                    </TR>
                    {rows.map((r) => {
                      const k = rowKey(r);
                      const s = state[k] ?? { qty: String(r.qty), supplierId: "" };
                      const price = item?.prices.find((p) => p.supplierId === s.supplierId);
                      return (
                        <TR key={k}>
                          <TD>
                            {canOrder ? <Checkbox checked={selected.has(k)} onCheckedChange={(v) => toggle(k, !!v)} aria-label={r.warehouseName} /> : null}
                          </TD>
                          <TD>
                            <div>{r.branchName ?? r.warehouseName}</div>
                            {r.branchName ? <div className="text-xs text-muted-foreground">{r.warehouseName}</div> : null}
                          </TD>
                          <TD className="tabular text-right">{formatNumber(r.currentQty)}</TD>
                          <TD className="tabular text-right text-muted-foreground">
                            {formatNumber(r.minQty)} / {formatNumber(r.optimalQty)}
                          </TD>
                          <TD className="tabular text-right">{formatNumber(r.qty)}</TD>
                          <TD className="text-right">
                            <Input
                              className="h-8 w-24 text-right"
                              inputMode="decimal"
                              value={s.qty}
                              disabled={!canOrder}
                              onChange={(e) => setState((st) => ({ ...st, [k]: { ...s, qty: e.target.value } }))}
                            />
                          </TD>
                          <TD>
                            <NativeSelect
                              className="h-8"
                              value={s.supplierId}
                              disabled={!canOrder}
                              onChange={(e) => setState((st) => ({ ...st, [k]: { ...s, supplierId: e.target.value } }))}
                            >
                              <option value="">{t("purchasing.demand.chooseSupplier")}</option>
                              {(item?.prices ?? []).map((p) => (
                                <option key={p.supplierId} value={p.supplierId}>
                                  {p.supplierName} — {p.lastPrice > 0 ? formatMoney(p.lastPrice) : t("purchasing.demand.noPrice")}
                                </option>
                              ))}
                            </NativeSelect>
                            {price && price.lastPrice > 0 ? (
                              <div className="mt-0.5 text-right text-xs text-muted-foreground">
                                ≈ {formatMoney(Math.round((parseNum(s.qty) ?? 0) * price.lastPrice))}
                              </div>
                            ) : null}
                          </TD>
                          <TD>
                            {canOrder ? (
                              <DropdownMenu>
                                <DropdownMenuTrigger asChild>
                                  <Button variant="ghost" size="icon-sm" disabled={busy} aria-label={t("common.actions")} data-testid="demand-row-actions">
                                    <MoreHorizontal />
                                  </Button>
                                </DropdownMenuTrigger>
                                <DropdownMenuContent align="end">
                                  {canTransfer && data.centralWarehouseId && r.warehouseId !== data.centralWarehouseId ? (
                                    <DropdownMenuItem onSelect={() => run("Transfer", [k])}>
                                      <ArrowRightLeft /> {t("purchasing.demand.rowTransfer")}
                                    </DropdownMenuItem>
                                  ) : null}
                                  <DropdownMenuItem onSelect={() => run("Order", [k])}>
                                    <ShoppingCart /> {t("purchasing.demand.rowOrder")}
                                  </DropdownMenuItem>
                                  <DropdownMenuSeparator />
                                  <DropdownMenuItem className="text-destructive" onSelect={() => setRejectKeys([k])}>
                                    <Ban /> {t("purchasing.demand.rowReject")}
                                  </DropdownMenuItem>
                                </DropdownMenuContent>
                              </DropdownMenu>
                            ) : null}
                          </TD>
                        </TR>
                      );
                    })}
                  </React.Fragment>
                );
              })}
            </TBody>
          </Table>
        )}
      </Card>

      <Dialog open={rejectKeys !== null} onOpenChange={(o) => !o && setRejectKeys(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t("purchasing.demand.rejectTitle")}</DialogTitle>
          </DialogHeader>
          <Field label={t("purchasing.requests.rejectReason")}>
            <Textarea rows={3} autoFocus value={rejectComment} onChange={(e) => setRejectComment(e.target.value)} />
          </Field>
          <DialogFooter>
            <Button variant="outline" onClick={() => setRejectKeys(null)}>
              {t("common.cancel")}
            </Button>
            <Button
              variant="destructive"
              loading={busy}
              onClick={() => {
                if (!rejectComment.trim()) return toast.error(t("purchasing.requests.commentRequired"));
                void run("Reject", rejectKeys ?? [], rejectComment.trim());
              }}
            >
              <Ban /> {t("purchasing.requests.reject")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
