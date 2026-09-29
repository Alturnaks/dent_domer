"use client";

import * as React from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, Boxes, FileStack, Search } from "lucide-react";
import { EmptyState, PageHeader } from "@/components/ui/empty-state";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect } from "@/components/ui/input";
import { Card } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Checkbox } from "@/components/ui/checkbox";
import { Table, TBody, TD, TFoot, TH, THead, TR } from "@/components/ui/table";
import { TableSkeleton } from "@/components/ui/skeleton";
import { ItemCardDialog } from "@/components/inventory/item-card-dialog";
import { categoryOptions, filterByBranch, unitLabel, useDebounced, useItemCategories, useWarehouses } from "@/components/inventory/shared";
import { api, ApiError } from "@/lib/api-client";
import { useAuth, useBranch, useOrgHref } from "@/lib/auth";
import { formatDate, formatMoney, formatNumber } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import type { Schemas } from "@/lib/types";

type Row = Schemas["StockBalanceRow"];

export default function StockBalancesPage() {
  const { can } = useAuth();
  const { branchId } = useBranch();
  const { href } = useOrgHref();
  const warehouses = useWarehouses();
  const categories = useItemCategories();
  const [warehouseId, setWarehouseId] = React.useState("");
  const [categoryId, setCategoryId] = React.useState("");
  const [q, setQ] = React.useState("");
  const [belowMin, setBelowMin] = React.useState(false);
  const [expiring, setExpiring] = React.useState("");
  const [cardItem, setCardItem] = React.useState<string | null>(null);
  const search = useDebounced(q.trim());

  const whOptions = filterByBranch(warehouses.data, branchId);
  // При смене филиала сбрасываем склад, если он больше недоступен.
  React.useEffect(() => {
    if (warehouseId && warehouses.data && !whOptions.some((w) => w.id === warehouseId)) setWarehouseId("");
  }, [warehouseId, whOptions, warehouses.data]);

  const query = useQuery({
    queryKey: ["stock", "balances", warehouseId, categoryId, search, belowMin, expiring],
    queryFn: () =>
      api<Row[]>("/stock/balances", {
        query: { warehouse_id: warehouseId || undefined, category_id: categoryId || undefined, q: search || undefined, below_min: belowMin || undefined, expiring_days: expiring || undefined },
      }),
    enabled: can(P.inventoryView),
  });

  // Без выбранного склада показываем только склады текущего филиала (+ центральные).
  const allowedIds = React.useMemo(() => new Set(whOptions.map((w) => w.id)), [whOptions]);
  const rows = (query.data ?? []).filter((r) => warehouseId || !branchId || allowedIds.has(r.warehouseId));
  const total = rows.reduce((s, r) => s + r.amount, 0);
  const multiWarehouse = !warehouseId && new Set(rows.map((r) => r.warehouseId)).size > 1;
  const filtered = !!(categoryId || search || belowMin || expiring);

  return (
    <div>
      <PageHeader
        title={t("inventory.balances.title")}
        description={t("inventory.balances.description")}
        actions={
          <>
            <Button variant="outline" asChild>
              <Link href={href("inventory/items")}>
                <Boxes /> {t("inventory.balances.toItems")}
              </Link>
            </Button>
            <Button variant="outline" asChild>
              <Link href={href("inventory/documents")}>
                <FileStack /> {t("inventory.balances.toDocuments")}
              </Link>
            </Button>
          </>
        }
      />

      <Card className="mb-3 flex flex-wrap items-center gap-3 p-3">
        <NativeSelect className="w-52" value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} aria-label={t("inventory.balances.warehouse")}>
          <option value="">{t("inventory.balances.allWarehouses")}</option>
          {whOptions.map((w) => (
            <option key={w.id} value={w.id}>
              {w.name}
            </option>
          ))}
        </NativeSelect>
        <NativeSelect className="w-52" value={categoryId} onChange={(e) => setCategoryId(e.target.value)} aria-label={t("inventory.items.category")}>
          <option value="">{t("inventory.balances.allCategories")}</option>
          {categoryOptions(categories.data).map((c) => (
            <option key={c.id} value={c.id}>
              {c.label}
            </option>
          ))}
        </NativeSelect>
        <div className="relative min-w-56 flex-1">
          <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input className="pl-8" placeholder={t("inventory.balances.searchPlaceholder")} value={q} onChange={(e) => setQ(e.target.value)} />
        </div>
        <NativeSelect className="w-48" value={expiring} onChange={(e) => setExpiring(e.target.value)} aria-label={t("inventory.balances.expiring")}>
          <option value="">{t("inventory.balances.expiringAny")}</option>
          {[30, 60, 90, 180].map((n) => (
            <option key={n} value={n}>
              {t("inventory.balances.expiringDays", { n })}
            </option>
          ))}
        </NativeSelect>
        <label className="flex items-center gap-2 text-sm">
          <Checkbox checked={belowMin} onCheckedChange={(v) => setBelowMin(v === true)} />
          {t("inventory.balances.belowMin")}
        </label>
      </Card>

      <Card>
        {query.isLoading ? (
          <TableSkeleton cols={7} />
        ) : query.isError ? (
          <EmptyState
            className="m-4"
            icon={<AlertTriangle className="h-8 w-8" />}
            title={query.error instanceof ApiError ? query.error.userMessage : t("errors.INTERNAL_ERROR")}
            action={<Button variant="outline" onClick={() => query.refetch()}>{t("common.retry")}</Button>}
          />
        ) : rows.length === 0 ? (
          <EmptyState
            className="m-4"
            title={filtered ? t("inventory.balances.emptyFiltered") : t("inventory.balances.empty")}
            description={filtered ? undefined : t("inventory.balances.emptyHint")}
            action={
              !filtered && can(P.inventoryReceive) ? (
                <Button asChild>
                  <Link href={href("inventory/documents/new?type=Receipt")}>{t("inventory.docTypes.Receipt")}</Link>
                </Button>
              ) : null
            }
          />
        ) : (
          <Table>
            <THead>
              <TR>
                <TH>{t("inventory.balances.item")}</TH>
                {multiWarehouse ? <TH>{t("inventory.balances.warehouse")}</TH> : null}
                <TH>{t("inventory.balances.nearestExpiry")}</TH>
                <TH className="text-right">{t("inventory.balances.qty")}</TH>
                <TH>{t("inventory.balances.unit")}</TH>
                <TH className="text-right">{t("inventory.balances.min")}</TH>
                <TH className="text-right">{t("inventory.balances.cost")}</TH>
                <TH className="text-right">{t("inventory.balances.amount")}</TH>
              </TR>
            </THead>
            <TBody>
              {rows.map((r) => (
                <TR key={`${r.warehouseId}-${r.itemId}`} className={`cursor-pointer ${r.belowMin ? "bg-warning/5" : ""}`} onClick={() => setCardItem(r.itemId)}>
                  <TD>
                    <div className="font-medium">{r.itemName}</div>
                    <div className="text-xs text-muted-foreground">{r.itemSku}</div>
                  </TD>
                  {multiWarehouse ? <TD className="whitespace-nowrap">{r.warehouseName}</TD> : null}
                  <TD className="whitespace-nowrap">
                    {r.nearestExpiry ? (
                      <span className={r.expired ? "font-medium text-destructive" : r.expiring ? "font-medium text-[oklch(0.5_0.12_70)]" : ""}>{formatDate(r.nearestExpiry)}</span>
                    ) : (
                      "—"
                    )}
                    {r.expired ? (
                      <Badge variant="destructive" className="ml-1.5">
                        {t("inventory.balances.expired")}
                      </Badge>
                    ) : r.expiring ? (
                      <Badge variant="warning" className="ml-1.5">
                        {t("inventory.balances.expiringSoon")}
                      </Badge>
                    ) : null}
                  </TD>
                  <TD className={`tabular whitespace-nowrap text-right font-medium ${r.qty < 0 ? "text-destructive" : r.belowMin ? "text-[oklch(0.5_0.12_70)]" : ""}`}>
                    {formatNumber(r.qty)}
                    {r.belowMin ? (
                      <Badge variant="warning" className="ml-1.5">
                        {t("inventory.balances.belowMinBadge")}
                      </Badge>
                    ) : null}
                  </TD>
                  <TD className="text-muted-foreground">{unitLabel(r.baseUnit)}</TD>
                  <TD className="tabular whitespace-nowrap text-right text-muted-foreground">
                    {r.minQty !== null ? `${formatNumber(r.minQty)} / ${formatNumber(r.optimalQty)}` : "—"}
                  </TD>
                  <TD className="tabular whitespace-nowrap text-right">{formatMoney(Math.round(r.avgCost))}</TD>
                  <TD className="tabular whitespace-nowrap text-right">{formatMoney(r.amount)}</TD>
                </TR>
              ))}
            </TBody>
            <TFoot>
              <TR>
                <TD colSpan={multiWarehouse ? 7 : 6} className="text-sm text-muted-foreground">
                  {t("inventory.balances.positions", { n: rows.length })}
                </TD>
                <TD className="tabular whitespace-nowrap text-right font-semibold">{formatMoney(total)}</TD>
              </TR>
            </TFoot>
          </Table>
        )}
      </Card>

      <ItemCardDialog itemId={cardItem} onOpenChange={(o) => !o && setCardItem(null)} />
    </div>
  );
}
