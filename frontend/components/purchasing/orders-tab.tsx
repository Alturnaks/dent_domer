"use client";

import * as React from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { AlertTriangle, ChevronLeft, ChevronRight, Plus, Search } from "lucide-react";
import { EmptyState } from "@/components/ui/empty-state";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect } from "@/components/ui/input";
import { Card } from "@/components/ui/card";
import { TableSkeleton } from "@/components/ui/skeleton";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { useDebounced, useSupplierOptions } from "@/components/inventory/shared";
import { api } from "@/lib/api-client";
import { useAuth, useOrgHref } from "@/lib/auth";
import { formatDate, formatDateTime, formatMoney, formatPercent } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import type { Schemas } from "@/lib/types";
import { ORDER_STATUSES, OrderStatusBadge, errText } from "./shared";

const PAGE_SIZE = 50;

export function OrdersTab() {
  const { can } = useAuth();
  const { push } = useOrgHref();
  const suppliers = useSupplierOptions();
  const [status, setStatus] = React.useState("");
  const [supplierId, setSupplierId] = React.useState("");
  const [q, setQ] = React.useState("");
  const debounced = useDebounced(q.trim(), 300);
  const [page, setPage] = React.useState(1);

  React.useEffect(() => setPage(1), [status, supplierId, debounced]);

  const query = useQuery({
    queryKey: ["purchase-orders", status, supplierId, debounced, page],
    queryFn: () =>
      api<Schemas["PagedResultOfPurchaseOrderListItem"]>("/purchase-orders", {
        query: { status: status || undefined, supplier_id: supplierId || undefined, q: debounced || undefined, page, page_size: PAGE_SIZE },
      }),
    placeholderData: keepPreviousData,
  });
  const data = query.data;
  const rows = data?.items ?? [];
  const totalPages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;
  const filtered = !!(status || supplierId || debounced);

  return (
    <div>
      <Card className="mb-3 flex flex-wrap items-center gap-3 p-3">
        <div className="relative w-44">
          <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input className="pl-8" value={q} placeholder={t("purchasing.orders.search")} onChange={(e) => setQ(e.target.value)} />
        </div>
        <NativeSelect className="w-48" value={status} onChange={(e) => setStatus(e.target.value)} aria-label={t("purchasing.orders.status")}>
          <option value="">{t("purchasing.orders.allStatuses")}</option>
          {ORDER_STATUSES.map((s) => (
            <option key={s} value={s}>
              {t(`purchasing.orderStatus.${s}`)}
            </option>
          ))}
        </NativeSelect>
        <NativeSelect className="w-56" value={supplierId} onChange={(e) => setSupplierId(e.target.value)} aria-label={t("purchasing.orders.supplier")}>
          <option value="">{t("purchasing.orders.allSuppliers")}</option>
          {(suppliers.data ?? []).map((s) => (
            <option key={s.id} value={s.id}>
              {s.name}
            </option>
          ))}
        </NativeSelect>
        {filtered ? (
          <Button
            variant="ghost"
            size="sm"
            onClick={() => {
              setStatus("");
              setSupplierId("");
              setQ("");
            }}
          >
            {t("common.reset")}
          </Button>
        ) : null}
        {can(P.purchaseOrder) ? (
          <Button className="ml-auto" onClick={() => push("purchasing/orders/new")} data-testid="new-order">
            <Plus /> {t("purchasing.orders.new")}
          </Button>
        ) : null}
      </Card>

      <Card>
        {query.isLoading ? (
          <TableSkeleton cols={8} />
        ) : query.isError ? (
          <EmptyState
            className="m-4"
            icon={<AlertTriangle className="h-8 w-8" />}
            title={errText(query.error)}
            action={<Button variant="outline" onClick={() => query.refetch()}>{t("common.retry")}</Button>}
          />
        ) : rows.length === 0 ? (
          <EmptyState className="m-4" title={t("purchasing.orders.empty")} description={filtered ? undefined : t("purchasing.orders.emptyHint")} />
        ) : (
          <Table>
            <THead>
              <TR>
                <TH>{t("purchasing.orders.number")}</TH>
                <TH>{t("purchasing.orders.date")}</TH>
                <TH>{t("purchasing.orders.supplier")}</TH>
                <TH>{t("purchasing.orders.warehouse")}</TH>
                <TH>{t("purchasing.orders.expected")}</TH>
                <TH className="text-right">{t("purchasing.orders.total")}</TH>
                <TH className="text-right">{t("purchasing.orders.received")}</TH>
                <TH>{t("purchasing.orders.status")}</TH>
              </TR>
            </THead>
            <TBody className={query.isFetching ? "opacity-60" : ""}>
              {rows.map((o) => (
                <TR key={o.id} className="cursor-pointer" onClick={() => push(`purchasing/orders/${o.id}`)}>
                  <TD className="whitespace-nowrap font-medium">{o.number}</TD>
                  <TD className="whitespace-nowrap">{formatDateTime(o.createdAt)}</TD>
                  <TD>{o.supplierName}</TD>
                  <TD className="text-sm">{o.warehouseName}</TD>
                  <TD className="whitespace-nowrap">{formatDate(o.expectedAt)}</TD>
                  <TD className="tabular whitespace-nowrap text-right">{formatMoney(o.total)}</TD>
                  <TD className="tabular text-right">{o.receivedPct > 0 ? formatPercent(o.receivedPct, 0) : "—"}</TD>
                  <TD>
                    <OrderStatusBadge status={o.status} />
                    {o.sentVia ? <div className="mt-0.5 text-xs text-muted-foreground">{t(`purchasing.sentVia.${o.sentVia}`)}</div> : null}
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
        )}
        {data && data.total > data.pageSize ? (
          <div className="flex items-center justify-end gap-2 border-t p-3 text-sm">
            <Button variant="outline" size="icon-sm" disabled={page <= 1} onClick={() => setPage((p) => p - 1)} aria-label={t("common.prev")}>
              <ChevronLeft />
            </Button>
            <span className="text-muted-foreground">{t("common.page", { page })}</span>
            <Button variant="outline" size="icon-sm" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)} aria-label={t("common.next")}>
              <ChevronRight />
            </Button>
          </div>
        ) : null}
      </Card>
    </div>
  );
}
