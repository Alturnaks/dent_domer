"use client";

import * as React from "react";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { AlertTriangle, Ban, CheckCheck, ChevronLeft, ChevronRight, Plus, Save, Send, Trash2, Wand2 } from "lucide-react";
import { EmptyState } from "@/components/ui/empty-state";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect, Textarea } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Card } from "@/components/ui/card";
import { Skeleton, TableSkeleton } from "@/components/ui/skeleton";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { ItemPicker } from "@/components/inventory/item-picker";
import { filterByBranch, parseNum, unitLabel, useWarehouses } from "@/components/inventory/shared";
import { api, ApiError } from "@/lib/api-client";
import { useAuth, useBranch } from "@/lib/auth";
import { formatDateTime, formatNumber } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import type { Schemas } from "@/lib/types";
import {
  REQUEST_STATUSES,
  RequestStatusBadge,
  SourceBadge,
  errText,
  type PurchaseRequest,
  type RequestSource,
} from "./shared";

const PAGE_SIZE = 50;

export function RequestsTab() {
  const { can } = useAuth();
  const { branchId } = useBranch();
  const qc = useQueryClient();
  const warehouses = useWarehouses();
  const [status, setStatus] = React.useState("");
  const [warehouseId, setWarehouseId] = React.useState("");
  const [source, setSource] = React.useState("");
  const [page, setPage] = React.useState(1);
  const [openId, setOpenId] = React.useState<string | null>(null);
  const [creating, setCreating] = React.useState(false);
  const [generating, setGenerating] = React.useState(false);
  const canCreate = can(P.purchaseRequest);

  React.useEffect(() => setPage(1), [status, warehouseId, source, branchId]);

  const query = useQuery({
    queryKey: ["purchase-requests", status, warehouseId, source, branchId, page],
    queryFn: () =>
      api<Schemas["PagedResultOfPurchaseRequestListItem"]>("/purchase-requests", {
        query: {
          status: status || undefined,
          warehouse_id: warehouseId || undefined,
          source: source || undefined,
          branch_id: warehouseId ? undefined : (branchId ?? undefined),
          page,
          page_size: PAGE_SIZE,
        },
      }),
    placeholderData: keepPreviousData,
  });
  const data = query.data;
  const rows = data?.items ?? [];
  const totalPages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  const generate = async () => {
    setGenerating(true);
    try {
      const r = await api<Schemas["GenerateRequestsResult"]>("/purchase-requests/generate", { method: "POST", query: { warehouse_id: warehouseId || undefined } });
      if (r.created + r.updated + r.closed === 0 && r.lines === 0) toast.info(t("purchasing.requests.generatedNothing"));
      else toast.success(t("purchasing.requests.generated", { created: r.created, updated: r.updated, closed: r.closed }));
      qc.invalidateQueries({ queryKey: ["purchase-requests"] });
    } catch (e) {
      toast.error(errText(e));
    } finally {
      setGenerating(false);
    }
  };

  const filtered = !!(status || warehouseId || source);

  return (
    <div>
      <Card className="mb-3 flex flex-wrap items-center gap-3 p-3">
        <NativeSelect className="w-44" value={status} onChange={(e) => setStatus(e.target.value)} aria-label={t("purchasing.requests.status")}>
          <option value="">{t("purchasing.requests.allStatuses")}</option>
          {REQUEST_STATUSES.map((s) => (
            <option key={s} value={s}>
              {t(`purchasing.requestStatus.${s}`)}
            </option>
          ))}
        </NativeSelect>
        <NativeSelect className="w-52" value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} aria-label={t("purchasing.requests.warehouse")}>
          <option value="">{t("purchasing.requests.allWarehouses")}</option>
          {filterByBranch(warehouses.data, branchId).map((w) => (
            <option key={w.id} value={w.id}>
              {w.name}
            </option>
          ))}
        </NativeSelect>
        <NativeSelect className="w-40" value={source} onChange={(e) => setSource(e.target.value)} aria-label={t("purchasing.requests.source")}>
          <option value="">{t("purchasing.requests.allSources")}</option>
          {(["Auto", "Manual"] as RequestSource[]).map((s) => (
            <option key={s} value={s}>
              {t(`purchasing.source.${s}`)}
            </option>
          ))}
        </NativeSelect>
        {filtered ? (
          <Button
            variant="ghost"
            size="sm"
            onClick={() => {
              setStatus("");
              setWarehouseId("");
              setSource("");
            }}
          >
            {t("common.reset")}
          </Button>
        ) : null}
        {canCreate ? (
          <div className="ml-auto flex flex-wrap gap-2">
            <Button variant="outline" onClick={generate} loading={generating} title={t("purchasing.requests.generateHint")} data-testid="generate-requests">
              <Wand2 /> {t("purchasing.requests.generate")}
            </Button>
            <Button onClick={() => setCreating(true)} data-testid="new-request">
              <Plus /> {t("purchasing.requests.new")}
            </Button>
          </div>
        ) : null}
      </Card>

      <Card>
        {query.isLoading ? (
          <TableSkeleton cols={7} />
        ) : query.isError ? (
          <EmptyState
            className="m-4"
            icon={<AlertTriangle className="h-8 w-8" />}
            title={errText(query.error)}
            action={<Button variant="outline" onClick={() => query.refetch()}>{t("common.retry")}</Button>}
          />
        ) : rows.length === 0 ? (
          <EmptyState className="m-4" title={t("purchasing.requests.empty")} description={filtered ? undefined : t("purchasing.requests.emptyHint")} />
        ) : (
          <Table>
            <THead>
              <TR>
                <TH>{t("purchasing.requests.date")}</TH>
                <TH>{t("purchasing.requests.warehouse")}</TH>
                <TH>{t("purchasing.requests.source")}</TH>
                <TH className="text-right">{t("purchasing.requests.lines")}</TH>
                <TH>{t("purchasing.requests.author")}</TH>
                <TH>{t("purchasing.requests.comment")}</TH>
                <TH>{t("purchasing.requests.status")}</TH>
              </TR>
            </THead>
            <TBody className={query.isFetching ? "opacity-60" : ""}>
              {rows.map((r) => (
                <TR key={r.id} className="cursor-pointer" onClick={() => setOpenId(r.id)}>
                  <TD className="whitespace-nowrap">{formatDateTime(r.createdAt)}</TD>
                  <TD>
                    <div className="font-medium">{r.warehouseName}</div>
                    {r.branchName ? <div className="text-xs text-muted-foreground">{r.branchName}</div> : null}
                  </TD>
                  <TD>
                    <SourceBadge source={r.source} />
                  </TD>
                  <TD className="tabular text-right">{r.linesCount}</TD>
                  <TD className="text-sm">{r.createdByName ?? "—"}</TD>
                  <TD className="max-w-72 truncate text-sm text-muted-foreground" title={r.comment ?? undefined}>
                    {r.comment ?? "—"}
                  </TD>
                  <TD>
                    <RequestStatusBadge status={r.status} />
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

      {openId ? <RequestDialog id={openId} onClose={() => setOpenId(null)} /> : null}
      {creating ? <RequestDialog id={null} onClose={() => setCreating(false)} onCreated={(id) => setOpenId(id)} /> : null}
    </div>
  );
}

type Line = { itemId: string; itemName: string; itemSku: string; baseUnit: string; qty: string; current: number | null; min: number | null; optimal: number | null };

function linesFrom(r: PurchaseRequest): Line[] {
  return r.lines.map((l) => ({
    itemId: l.itemId, itemName: l.itemName, itemSku: l.itemSku, baseUnit: l.baseUnit, qty: String(l.qty), current: l.currentQty, min: l.minQty, optimal: l.optimalQty,
  }));
}

/** Просмотр/редактирование заявки. id = null — новая ручная заявка. */
function RequestDialog({ id, onClose, onCreated }: { id: string | null; onClose: () => void; onCreated?: (id: string) => void }) {
  const qc = useQueryClient();
  const { can } = useAuth();
  const { branchId } = useBranch();
  const warehouses = useWarehouses();
  const query = useQuery({ queryKey: ["purchase-request", id], queryFn: () => api<PurchaseRequest>(`/purchase-requests/${id}`), enabled: !!id });
  const req = query.data;

  const [warehouseId, setWarehouseId] = React.useState("");
  const [comment, setComment] = React.useState("");
  const [lines, setLines] = React.useState<Line[]>([]);
  const [dirty, setDirty] = React.useState(false);
  const [busy, setBusy] = React.useState<string | null>(null);
  const [rejecting, setRejecting] = React.useState(false);
  const [reason, setReason] = React.useState("");

  React.useEffect(() => {
    if (!req) return;
    setWarehouseId(req.warehouseId);
    setComment(req.comment ?? "");
    setLines(linesFrom(req));
    setDirty(false);
  }, [req]);

  React.useEffect(() => {
    if (id || warehouseId || !warehouses.data) return;
    const own = filterByBranch(warehouses.data, branchId);
    const def = own.find((w) => w.branchId && w.branchId === branchId) ?? own.find((w) => w.branchId) ?? own[0];
    if (def) setWarehouseId(def.id);
  }, [id, warehouseId, warehouses.data, branchId]);

  const isNew = !id;
  const status = req?.status ?? "Draft";
  const editable = status === "Draft" && can(P.purchaseRequest);
  const canReject = !!req && ((status === "Draft" && can(P.purchaseRequest)) || (status === "Submitted" && can(P.purchaseOrder)));
  const canMarkProcessed = !!req && status === "Submitted" && can(P.purchaseOrder);

  const refresh = (r?: PurchaseRequest) => {
    if (r) qc.setQueryData(["purchase-request", r.id], r);
    qc.invalidateQueries({ queryKey: ["purchase-requests"] });
    qc.invalidateQueries({ queryKey: ["network-demand"] });
  };

  const validate = (): boolean => {
    const error = !warehouseId
      ? t("purchasing.requests.warehouseRequired")
      : lines.length === 0
        ? t("purchasing.requests.noLines")
        : lines.some((l) => (parseNum(l.qty) ?? 0) <= 0)
          ? t("purchasing.requests.qtyInvalid")
          : null;
    if (error) toast.error(error);
    return !error;
  };

  const body = () => ({ comment, lines: lines.map((l) => ({ itemId: l.itemId, qty: parseNum(l.qty) ?? 0 })) });

  const save = async (submit: boolean) => {
    if (!validate()) return;
    setBusy(submit ? "submit" : "save");
    try {
      let r: PurchaseRequest;
      if (isNew) {
        r = await api<PurchaseRequest>("/purchase-requests", {
          method: "POST",
          body: { warehouseId, ...body(), submit } satisfies Schemas["CreatePurchaseRequestRequest"],
        });
        refresh(r);
        toast.success(submit ? t("purchasing.requests.submitted") : t("purchasing.requests.saved"));
        onClose();
        if (!submit) onCreated?.(r.id);
        return;
      }
      r = req!;
      if (dirty) r = await api<PurchaseRequest>(`/purchase-requests/${id}`, { method: "PATCH", body: body() satisfies Schemas["UpdatePurchaseRequestRequest"] });
      if (submit) r = await api<PurchaseRequest>(`/purchase-requests/${id}/submit`, { method: "POST" });
      refresh(r);
      setDirty(false);
      toast.success(submit ? t("purchasing.requests.submitted") : t("purchasing.requests.saved"));
      if (submit) onClose();
    } catch (e) {
      toast.error(errText(e));
    } finally {
      setBusy(null);
    }
  };

  const reject = async () => {
    if (!reason.trim()) return toast.error(t("purchasing.requests.commentRequired"));
    setBusy("reject");
    try {
      const r = await api<PurchaseRequest>(`/purchase-requests/${id}/reject`, { method: "POST", body: { comment: reason } });
      refresh(r);
      toast.success(t("purchasing.requests.rejected"));
      onClose();
    } catch (e) {
      toast.error(errText(e));
    } finally {
      setBusy(null);
    }
  };

  const markProcessed = async () => {
    setBusy("processed");
    try {
      const r = await api<PurchaseRequest>(`/purchase-requests/${id}/mark-processed`, { method: "POST", body: { comment: null } });
      refresh(r);
      toast.success(t("purchasing.requests.markProcessedDone"));
      onClose();
    } catch (e) {
      toast.error(e instanceof ApiError ? errText(e) : t("errors.INTERNAL_ERROR"));
    } finally {
      setBusy(null);
    }
  };

  const whList = filterByBranch(warehouses.data, branchId);

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent wide>
        <DialogHeader>
          <DialogTitle className="flex flex-wrap items-center gap-2">
            {isNew ? t("purchasing.requests.new") : t("purchasing.requests.detailTitle")}
            {req ? <RequestStatusBadge status={req.status} /> : null}
            {req ? <SourceBadge source={req.source} /> : null}
          </DialogTitle>
          {req ? (
            <DialogDescription>
              {req.warehouseName}
              {req.branchName ? ` · ${req.branchName}` : ""} · {t("purchasing.requests.createdAt")} {formatDateTime(req.createdAt)}
              {req.createdByName ? ` · ${req.createdByName}` : ""}
            </DialogDescription>
          ) : null}
        </DialogHeader>

        {id && query.isLoading ? (
          <Skeleton className="h-48 w-full" />
        ) : (
          <div className="space-y-3">
            {isNew ? (
              <Field label={t("purchasing.requests.warehouse")}>
                <NativeSelect value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)}>
                  <option value="">—</option>
                  {whList.map((w) => (
                    <option key={w.id} value={w.id}>
                      {w.name}
                    </option>
                  ))}
                </NativeSelect>
              </Field>
            ) : null}
            {editable ? (
              <ItemPicker
                onSelect={(it) => {
                  setLines((ls) =>
                    ls.some((l) => l.itemId === it.id)
                      ? ls
                      : [...ls, { itemId: it.id, itemName: it.name, itemSku: it.sku, baseUnit: it.baseUnit, qty: "1", current: null, min: null, optimal: null }],
                  );
                  setDirty(true);
                }}
              />
            ) : null}
            {lines.length === 0 ? (
              <p className="rounded-md border border-dashed p-4 text-center text-sm text-muted-foreground">{t("purchasing.requests.addItem")}</p>
            ) : (
              <div className="overflow-x-auto rounded-md border">
                <Table>
                  <THead>
                    <TR>
                      <TH>{t("purchasing.requests.item")}</TH>
                      <TH className="text-right">{t("purchasing.requests.current")}</TH>
                      <TH className="text-right">{t("purchasing.requests.min")}</TH>
                      <TH className="text-right">{t("purchasing.requests.optimal")}</TH>
                      <TH className="w-32 text-right">{t("purchasing.requests.qty")}</TH>
                      {editable ? <TH className="w-10" /> : null}
                    </TR>
                  </THead>
                  <TBody>
                    {lines.map((l) => (
                      <TR key={l.itemId}>
                        <TD>
                          <div className="font-medium">{l.itemName}</div>
                          <div className="text-xs text-muted-foreground">{l.itemSku}</div>
                        </TD>
                        <TD className="tabular text-right">{formatNumber(l.current)}</TD>
                        <TD className="tabular text-right">{formatNumber(l.min)}</TD>
                        <TD className="tabular text-right">{formatNumber(l.optimal)}</TD>
                        <TD className="text-right">
                          {editable ? (
                            <div className="flex items-center justify-end gap-1">
                              <Input
                                className="h-8 w-20 text-right"
                                inputMode="decimal"
                                value={l.qty}
                                onChange={(e) => {
                                  const v = e.target.value;
                                  setLines((ls) => ls.map((x) => (x.itemId === l.itemId ? { ...x, qty: v } : x)));
                                  setDirty(true);
                                }}
                              />
                              <span className="w-8 text-left text-xs text-muted-foreground">{unitLabel(l.baseUnit)}</span>
                            </div>
                          ) : (
                            <span className="tabular font-medium">
                              {formatNumber(parseNum(l.qty))} {unitLabel(l.baseUnit)}
                            </span>
                          )}
                        </TD>
                        {editable ? (
                          <TD>
                            <Button
                              variant="ghost"
                              size="icon-sm"
                              aria-label={t("common.delete")}
                              onClick={() => {
                                setLines((ls) => ls.filter((x) => x.itemId !== l.itemId));
                                setDirty(true);
                              }}
                            >
                              <Trash2 />
                            </Button>
                          </TD>
                        ) : null}
                      </TR>
                    ))}
                  </TBody>
                </Table>
              </div>
            )}
            <Field label={t("purchasing.requests.comment")}>
              {editable ? (
                <Textarea
                  rows={2}
                  value={comment}
                  onChange={(e) => {
                    setComment(e.target.value);
                    setDirty(true);
                  }}
                />
              ) : (
                <p className="whitespace-pre-line text-sm text-muted-foreground">{req?.comment || "—"}</p>
              )}
            </Field>
            {rejecting ? (
              <Field label={t("purchasing.requests.rejectReason")}>
                <Textarea rows={2} autoFocus value={reason} onChange={(e) => setReason(e.target.value)} />
              </Field>
            ) : null}
          </div>
        )}

        <DialogFooter className="flex-wrap">
          {rejecting ? (
            <>
              <Button variant="outline" onClick={() => setRejecting(false)}>
                {t("common.cancel")}
              </Button>
              <Button variant="destructive" onClick={reject} loading={busy === "reject"}>
                <Ban /> {t("purchasing.requests.reject")}
              </Button>
            </>
          ) : (
            <>
              {canReject ? (
                <Button variant="outline" onClick={() => setRejecting(true)} disabled={!!busy}>
                  <Ban /> {t("purchasing.requests.reject")}
                </Button>
              ) : null}
              {canMarkProcessed ? (
                <Button variant="outline" onClick={markProcessed} loading={busy === "processed"} disabled={!!busy}>
                  <CheckCheck /> {t("purchasing.requests.markProcessed")}
                </Button>
              ) : null}
              {editable ? (
                <>
                  <Button variant="outline" onClick={() => save(false)} loading={busy === "save"} disabled={!!busy || (!isNew && !dirty)}>
                    <Save /> {t("purchasing.requests.saveDraft")}
                  </Button>
                  <Button onClick={() => save(true)} loading={busy === "submit"} disabled={!!busy} data-testid="submit-request">
                    <Send /> {isNew ? t("purchasing.requests.saveAndSubmit") : t("purchasing.requests.submit")}
                  </Button>
                </>
              ) : (
                <Button variant="outline" onClick={onClose}>
                  {t("common.close")}
                </Button>
              )}
            </>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
