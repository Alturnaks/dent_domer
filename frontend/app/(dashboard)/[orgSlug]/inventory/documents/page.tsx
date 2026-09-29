"use client";

import * as React from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { AlertTriangle, ArrowRightLeft, ChevronDown, ChevronLeft, ChevronRight, ClipboardList, PackageMinus, PackagePlus, Plus } from "lucide-react";
import { EmptyState, PageHeader } from "@/components/ui/empty-state";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect } from "@/components/ui/input";
import { Card } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { TableSkeleton } from "@/components/ui/skeleton";
import { DOC_STATUSES, DOC_TYPES, DocStatusBadge, docTypeLabel, filterByBranch, useWarehouses, type DocType } from "@/components/inventory/shared";
import { api, ApiError } from "@/lib/api-client";
import { useAuth, useBranch, useOrgHref } from "@/lib/auth";
import { formatDateTime, formatMoney } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import type { Schemas } from "@/lib/types";

const PAGE_SIZE = 50;

export default function StockDocumentsPage() {
  const { can } = useAuth();
  const { branchId } = useBranch();
  const { push } = useOrgHref();
  const warehouses = useWarehouses();
  const [type, setType] = React.useState("");
  const [status, setStatus] = React.useState("");
  const [warehouseId, setWarehouseId] = React.useState("");
  const [from, setFrom] = React.useState("");
  const [to, setTo] = React.useState("");
  const [page, setPage] = React.useState(1);

  React.useEffect(() => setPage(1), [type, status, warehouseId, from, to]);

  const query = useQuery({
    queryKey: ["stock-documents", type, status, warehouseId, from, to, page],
    queryFn: () =>
      api<Schemas["PagedResultOfStockDocumentListItem"]>("/stock-documents", {
        query: { type: type || undefined, status: status || undefined, warehouse_id: warehouseId || undefined, from: from || undefined, to: to || undefined, page, page_size: PAGE_SIZE },
      }),
    placeholderData: keepPreviousData,
  });
  const data = query.data;
  const rows = data?.items ?? [];
  const totalPages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  const createOptions: { type: DocType; icon: React.ReactNode; allowed: boolean }[] = [
    { type: "Receipt", icon: <PackagePlus />, allowed: can(P.inventoryReceive) },
    { type: "Writeoff", icon: <PackageMinus />, allowed: can(P.writeoff) },
    { type: "Transfer", icon: <ArrowRightLeft />, allowed: can(P.transferCreate) },
    { type: "Inventory", icon: <ClipboardList />, allowed: can(P.inventoryCount) },
  ];
  const allowedCreate = createOptions.filter((o) => o.allowed);
  const filtered = !!(type || status || warehouseId || from || to);

  return (
    <div>
      <PageHeader
        title={t("inventory.documents.title")}
        description={t("inventory.documents.description")}
        actions={
          allowedCreate.length ? (
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button data-testid="new-stock-document">
                  <Plus /> {t("inventory.documents.create")} <ChevronDown />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                {allowedCreate.map((o) => (
                  <DropdownMenuItem key={o.type} onSelect={() => push(`inventory/documents/new?type=${o.type}`)}>
                    {o.icon} {docTypeLabel(o.type)}
                  </DropdownMenuItem>
                ))}
              </DropdownMenuContent>
            </DropdownMenu>
          ) : null
        }
      />

      <Card className="mb-3 flex flex-wrap items-center gap-3 p-3">
        <NativeSelect className="w-48" value={type} onChange={(e) => setType(e.target.value)} aria-label={t("inventory.documents.type")}>
          <option value="">{t("inventory.documents.allTypes")}</option>
          {DOC_TYPES.map((d) => (
            <option key={d} value={d}>
              {docTypeLabel(d)}
            </option>
          ))}
        </NativeSelect>
        <NativeSelect className="w-44" value={status} onChange={(e) => setStatus(e.target.value)} aria-label={t("inventory.documents.status")}>
          <option value="">{t("inventory.documents.allStatuses")}</option>
          {DOC_STATUSES.map((s) => (
            <option key={s} value={s}>
              {t(`inventory.docStatus.${s}`)}
            </option>
          ))}
        </NativeSelect>
        <NativeSelect className="w-52" value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} aria-label={t("inventory.documents.warehouses")}>
          <option value="">{t("inventory.documents.allWarehouses")}</option>
          {filterByBranch(warehouses.data, branchId).map((w) => (
            <option key={w.id} value={w.id}>
              {w.name}
            </option>
          ))}
        </NativeSelect>
        <div className="flex items-center gap-2 text-sm">
          <span className="text-muted-foreground">{t("common.from")}</span>
          <Input type="date" className="w-40" value={from} onChange={(e) => setFrom(e.target.value)} />
          <span className="text-muted-foreground">{t("common.to")}</span>
          <Input type="date" className="w-40" value={to} onChange={(e) => setTo(e.target.value)} />
        </div>
        {filtered ? (
          <Button
            variant="ghost"
            size="sm"
            onClick={() => {
              setType("");
              setStatus("");
              setWarehouseId("");
              setFrom("");
              setTo("");
            }}
          >
            {t("common.reset")}
          </Button>
        ) : null}
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
          <EmptyState className="m-4" title={t("inventory.documents.empty")} description={filtered ? undefined : t("inventory.documents.emptyHint")} />
        ) : (
          <Table>
            <THead>
              <TR>
                <TH>{t("inventory.documents.number")}</TH>
                <TH>{t("inventory.documents.type")}</TH>
                <TH>{t("inventory.documents.date")}</TH>
                <TH>{t("inventory.documents.warehouses")}</TH>
                <TH>{t("inventory.documents.counterparty")}</TH>
                <TH className="text-right">{t("inventory.documents.lines")}</TH>
                <TH className="text-right">{t("inventory.documents.total")}</TH>
                <TH>{t("inventory.documents.status")}</TH>
              </TR>
            </THead>
            <TBody className={query.isFetching ? "opacity-60" : ""}>
              {rows.map((d) => (
                <TR key={d.id} className="cursor-pointer" onClick={() => push(`inventory/documents/${d.id}`)}>
                  <TD className="whitespace-nowrap font-medium">{d.number}</TD>
                  <TD>
                    <Badge variant="outline">{docTypeLabel(d.type)}</Badge>
                  </TD>
                  <TD className="whitespace-nowrap">{formatDateTime(d.postedAt ?? d.createdAt)}</TD>
                  <TD className="text-sm">
                    {d.warehouseFromName && d.warehouseToName && d.type === "Transfer" ? (
                      <span className="whitespace-nowrap">
                        {d.warehouseFromName} → {d.warehouseToName}
                      </span>
                    ) : (
                      (d.warehouseFromName ?? d.warehouseToName ?? "—")
                    )}
                  </TD>
                  <TD className="max-w-64 truncate text-sm text-muted-foreground" title={d.comment ?? undefined}>
                    {d.supplierName ?? d.reasonName ?? d.comment ?? "—"}
                  </TD>
                  <TD className="tabular text-right">{d.linesCount}</TD>
                  <TD className={`tabular whitespace-nowrap text-right ${d.totalCost < 0 ? "text-destructive" : ""}`}>{formatMoney(d.totalCost)}</TD>
                  <TD>
                    <DocStatusBadge status={d.status} />
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
        )}
        {data && data.total > data.pageSize ? (
          <div className="flex items-center justify-end gap-2 border-t p-3 text-sm">
            <span className="text-muted-foreground">
              {t("inventory.documents.pageInfo", {
                from: (data.page - 1) * data.pageSize + 1,
                to: Math.min(data.page * data.pageSize, data.total),
                total: data.total,
              })}
            </span>
            <Button variant="outline" size="icon-sm" disabled={page <= 1} onClick={() => setPage((p) => p - 1)} aria-label={t("common.prev")}>
              <ChevronLeft />
            </Button>
            <Button variant="outline" size="icon-sm" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)} aria-label={t("common.next")}>
              <ChevronRight />
            </Button>
          </div>
        ) : null}
      </Card>
    </div>
  );
}
