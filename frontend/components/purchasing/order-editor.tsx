"use client";

import * as React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { AlertTriangle, ArrowLeft, Ban, ChevronDown, Copy, Download, MessageCircle, PackageCheck, Save, Send, Trash2 } from "lucide-react";
import { PageHeader } from "@/components/ui/empty-state";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect, Textarea } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Card } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TBody, TD, TFoot, TH, THead, TR } from "@/components/ui/table";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { ItemPicker } from "@/components/inventory/item-picker";
import { DocStatusBadge, filterByBranch, parseNum, unitLabel, useSupplierOptions, useWarehouses } from "@/components/inventory/shared";
import { api, download } from "@/lib/api-client";
import { useAuth, useBranch, useOrgHref } from "@/lib/auth";
import { formatDate, formatDateTime, formatMoney, formatNumber, formatPhone, toMinor, whatsappLink } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import type { Schemas } from "@/lib/types";
import { Banner, InvoiceStatusBadge, OrderStatusBadge, SENT_VIA, errText, type PurchaseOrder } from "./shared";

type Line = {
  key: string;
  itemId: string;
  itemName: string;
  itemSku: string;
  supplierSku: string | null;
  baseUnit: string;
  qty: string;
  price: string; // тенге
  receivedQty: number;
  remainingQty: number;
};

type Form = { supplierId: string; warehouseId: string; expectedAt: string; comment: string; lines: Line[] };

let seq = 0;
const nextKey = () => `n${++seq}`;

function formFrom(o: PurchaseOrder): Form {
  return {
    supplierId: o.supplierId,
    warehouseId: o.warehouseId,
    expectedAt: o.expectedAt ?? "",
    comment: o.comment ?? "",
    lines: o.lines.map((l) => ({
      key: l.id,
      itemId: l.itemId,
      itemName: l.itemName,
      itemSku: l.itemSku,
      supplierSku: l.supplierSku,
      baseUnit: l.baseUnit,
      qty: String(l.qty),
      price: l.unitPrice ? String(l.unitPrice / 100) : "",
      receivedQty: l.receivedQty,
      remainingQty: l.remainingQty,
    })),
  };
}

const emptyForm = (): Form => ({ supplierId: "", warehouseId: "", expectedAt: "", comment: "", lines: [] });

export function OrderEditor({ id }: { id?: string }) {
  const router = useRouter();
  const qc = useQueryClient();
  const { can } = useAuth();
  const { branchId } = useBranch();
  const { href, push } = useOrgHref();
  const warehouses = useWarehouses();
  const suppliers = useSupplierOptions();

  const orderQuery = useQuery({ queryKey: ["purchase-order", id], queryFn: () => api<PurchaseOrder>(`/purchase-orders/${id}`), enabled: !!id });
  const order = orderQuery.data;
  const [form, setForm] = React.useState<Form>(emptyForm);
  const [dirty, setDirty] = React.useState(false);
  const [busy, setBusy] = React.useState<string | null>(null);
  const [sendOpen, setSendOpen] = React.useState(false);
  const [cancelOpen, setCancelOpen] = React.useState(false);

  const supplierItems = useQuery({
    queryKey: ["supplier-items", form.supplierId],
    queryFn: () => api<Schemas["SupplierItemDto"][]>(`/suppliers/${form.supplierId}/items`),
    enabled: !!form.supplierId,
    staleTime: 60_000,
  });

  const loaded = React.useRef<string | null>(null);
  React.useEffect(() => {
    if (!order) return;
    const sig = `${order.id}:${order.version}:${order.status}`;
    if (loaded.current === sig) return;
    loaded.current = sig;
    setForm(formFrom(order));
    setDirty(false);
  }, [order]);

  // Склад по умолчанию для нового заказа — склад текущего филиала.
  React.useEffect(() => {
    if (id || !warehouses.data) return;
    const own = filterByBranch(warehouses.data, branchId);
    const def = own.find((w) => w.branchId && w.branchId === branchId) ?? own[0];
    if (def) setForm((f) => (f.warehouseId ? f : { ...f, warehouseId: def.id }));
  }, [id, warehouses.data, branchId]);

  const patch = (p: Partial<Form>) => {
    setForm((f) => ({ ...f, ...p }));
    setDirty(true);
  };
  const patchLine = (key: string, p: Partial<Line>) => {
    setForm((f) => ({ ...f, lines: f.lines.map((l) => (l.key === key ? { ...l, ...p } : l)) }));
    setDirty(true);
  };

  if (id && orderQuery.isLoading) {
    return (
      <div className="space-y-3">
        <Skeleton className="h-8 w-80" />
        <Skeleton className="h-32 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }
  if (id && orderQuery.isError) {
    return (
      <Card className="flex flex-col items-center gap-3 p-10 text-center">
        <AlertTriangle className="h-8 w-8 text-destructive" />
        <p>{errText(orderQuery.error)}</p>
        <Button variant="outline" asChild>
          <Link href={href("purchasing?tab=orders")}>{t("purchasing.orders.backToList")}</Link>
        </Button>
      </Card>
    );
  }

  const status = order?.status ?? "Draft";
  const canEdit = status === "Draft" && can(P.purchaseOrder);
  const canSend = can(P.purchaseOrder) && (status === "Draft" || status === "Sent" || status === "PartiallyReceived");
  const canCancel = !!order && can(P.purchaseOrder) && (status === "Draft" || status === "PendingApproval" || status === "Sent") && order.lines.every((l) => l.receivedQty === 0);
  const canReceive = !!order && can(P.inventoryReceive) && (status === "Sent" || status === "PartiallyReceived");
  const threshold = order?.approvalThreshold ?? null;

  const lineTotal = (l: Line) => {
    const q = parseNum(l.qty);
    const p = parseNum(l.price);
    return q !== null && p !== null ? Math.round(q * toMinor(p)) : 0;
  };
  const total = form.lines.reduce((s, l) => s + lineTotal(l), 0);

  const validate = (): boolean => {
    const error = !form.supplierId
      ? t("purchasing.orders.supplierRequired")
      : !form.warehouseId
        ? t("purchasing.orders.warehouseRequired")
        : form.lines.length === 0
          ? t("purchasing.orders.noLines")
          : form.lines.some((l) => (parseNum(l.qty) ?? 0) <= 0)
            ? t("purchasing.orders.qtyInvalid")
            : null;
    if (error) toast.error(error);
    return !error;
  };

  const linesInput = (): Schemas["PurchaseOrderLineInput"][] =>
    form.lines.map((l) => {
      const p = parseNum(l.price);
      return { itemId: l.itemId, qty: parseNum(l.qty) ?? 0, unitPrice: p === null ? null : toMinor(p) };
    });

  const apply = (o: PurchaseOrder) => {
    qc.setQueryData(["purchase-order", o.id], o);
    qc.invalidateQueries({ queryKey: ["purchase-orders"] });
  };

  const persist = async (): Promise<PurchaseOrder> => {
    if (!order) {
      return api<PurchaseOrder>("/purchase-orders", {
        method: "POST",
        body: {
          supplierId: form.supplierId,
          warehouseId: form.warehouseId,
          expectedAt: form.expectedAt || null,
          comment: form.comment,
          lines: linesInput(),
        } satisfies Schemas["CreatePurchaseOrderRequest"],
      });
    }
    return api<PurchaseOrder>(`/purchase-orders/${order.id}`, {
      method: "PATCH",
      body: {
        supplierId: form.supplierId,
        warehouseId: form.warehouseId,
        expectedAt: form.expectedAt || null,
        comment: form.comment,
        lines: linesInput(),
        version: order.version,
      } satisfies Schemas["UpdatePurchaseOrderRequest"],
    });
  };

  const onSave = async () => {
    if (!validate()) return;
    setBusy("save");
    try {
      const o = await persist();
      apply(o);
      toast.success(order ? t("purchasing.orders.saved") : t("purchasing.orders.created"));
      if (!order) router.replace(href(`purchasing/orders/${o.id}`));
    } catch (e) {
      toast.error(errText(e));
    } finally {
      setBusy(null);
    }
  };

  /** Перед отправкой/выгрузкой несохранённые изменения черновика сохраняются. */
  const ensureSaved = async (): Promise<PurchaseOrder | null> => {
    if (order && !dirty) return order;
    if (!validate()) return null;
    const o = await persist();
    apply(o);
    setDirty(false);
    if (!order) router.replace(href(`purchasing/orders/${o.id}`));
    return o;
  };

  const onReceive = async () => {
    if (!order) return;
    setBusy("receive");
    try {
      const doc = await api<Schemas["StockDocumentDto"]>(`/purchase-orders/${order.id}/receive`, { method: "POST" });
      qc.invalidateQueries({ queryKey: ["stock-documents"] });
      push(`inventory/documents/${doc.id}`);
    } catch (e) {
      toast.error(errText(e));
      setBusy(null);
    }
  };

  const exportFile = async (format: "pdf" | "xlsx" | "csv") => {
    try {
      const o = await ensureSaved();
      if (!o) return;
      await download(`/purchase-orders/${o.id}/export`, { format }, `${o.number}.${format}`);
    } catch (e) {
      toast.error(errText(e));
    }
  };

  const supplierName = order?.supplierName ?? suppliers.data?.find((s) => s.id === form.supplierId)?.name;
  const title = order ? t("purchasing.orders.title", { number: order.number }) : t("purchasing.orders.newTitle");
  const whList = (() => {
    const own = filterByBranch(warehouses.data, branchId);
    const sel = warehouses.data?.find((w) => w.id === form.warehouseId);
    return sel && !own.some((w) => w.id === sel.id) ? [sel, ...own] : own;
  })();

  return (
    <div className="pb-16">
      <PageHeader
        title={title}
        description={
          <Link href={href("purchasing?tab=orders")} className="inline-flex items-center gap-1 hover:underline">
            <ArrowLeft className="h-3.5 w-3.5" /> {t("purchasing.orders.backToList")}
          </Link>
        }
        actions={
          <>
            {order ? <OrderStatusBadge status={order.status} /> : null}
            {canCancel ? (
              <Button variant="outline" onClick={() => setCancelOpen(true)} disabled={!!busy}>
                <Ban /> {t("purchasing.orders.cancel")}
              </Button>
            ) : null}
            {order || canEdit ? (
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button variant="outline" disabled={!!busy || (!order && form.lines.length === 0)}>
                    <Download /> {t("purchasing.orders.export")} <ChevronDown />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuItem onSelect={() => exportFile("pdf")}>{t("purchasing.orders.downloadPdf")}</DropdownMenuItem>
                  <DropdownMenuItem onSelect={() => exportFile("xlsx")}>{t("purchasing.orders.downloadXlsx")}</DropdownMenuItem>
                  <DropdownMenuItem onSelect={() => exportFile("csv")}>{t("purchasing.orders.downloadCsv")}</DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            ) : null}
            {canEdit ? (
              <Button variant="outline" onClick={onSave} loading={busy === "save"} disabled={!!busy || (!!order && !dirty)}>
                <Save /> {t("purchasing.orders.saveDraft")}
              </Button>
            ) : null}
            {canSend ? (
              <Button
                variant={status === "Draft" ? "default" : "outline"}
                disabled={!!busy}
                onClick={async () => {
                  try {
                    if (await ensureSaved()) setSendOpen(true);
                  } catch (e) {
                    toast.error(errText(e));
                  }
                }}
                data-testid="send-order"
              >
                <Send /> {status === "Draft" ? t("purchasing.orders.send") : t("purchasing.orders.resend")}
              </Button>
            ) : null}
            {canReceive ? (
              <Button variant="success" onClick={onReceive} loading={busy === "receive"} disabled={!!busy} title={t("purchasing.orders.receiveHint")} data-testid="receive-order">
                <PackageCheck /> {t("purchasing.orders.receive")}
              </Button>
            ) : null}
          </>
        }
      />

      {order && status === "PendingApproval" ? <Banner tone="warning">{t("purchasing.orders.pendingBanner", { threshold: formatMoney(threshold) })}</Banner> : null}
      {canEdit && threshold !== null && total > threshold ? <Banner tone="info">{t("purchasing.orders.overThreshold", { threshold: formatMoney(threshold) })}</Banner> : null}
      {order && !canEdit && status !== "PendingApproval" ? (
        <Banner tone="muted">{t("purchasing.orders.readonly", { status: t(`purchasing.orderStatus.${status}`) })}</Banner>
      ) : null}

      <Card className="mb-3 grid gap-3 p-4 sm:grid-cols-2 lg:grid-cols-4">
        <Field label={t("purchasing.orders.supplier")}>
          <NativeSelect value={form.supplierId} disabled={!canEdit} onChange={(e) => patch({ supplierId: e.target.value })}>
            <option value="">—</option>
            {order && !suppliers.data?.some((s) => s.id === order.supplierId) ? <option value={order.supplierId}>{order.supplierName}</option> : null}
            {(suppliers.data ?? []).map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </NativeSelect>
        </Field>
        <Field label={t("purchasing.orders.warehouse")}>
          <NativeSelect value={form.warehouseId} disabled={!canEdit} onChange={(e) => patch({ warehouseId: e.target.value })}>
            <option value="">—</option>
            {whList.map((w) => (
              <option key={w.id} value={w.id}>
                {w.name}
              </option>
            ))}
          </NativeSelect>
        </Field>
        <Field label={t("purchasing.orders.expectedAt")}>
          <Input type="date" value={form.expectedAt} disabled={!canEdit} onChange={(e) => patch({ expectedAt: e.target.value })} />
        </Field>
        <Field label={t("purchasing.orders.comment")}>
          <Textarea className="min-h-[38px]" rows={1} value={form.comment} disabled={!canEdit} onChange={(e) => patch({ comment: e.target.value })} />
        </Field>
        {order ? (
          <div className="grid gap-x-6 gap-y-1 text-sm text-muted-foreground sm:col-span-2 sm:grid-cols-2 lg:col-span-4 lg:grid-cols-4">
            <span>
              {t("purchasing.orders.createdBy")}: {formatDateTime(order.createdAt)}
              {order.createdByName ? ` · ${order.createdByName}` : ""}
            </span>
            {order.sentAt ? (
              <span>
                {t("purchasing.orders.sentAt")}: {formatDateTime(order.sentAt)}
                {order.sentVia ? ` · ${t(`purchasing.sentVia.${order.sentVia}`)}` : ""}
              </span>
            ) : null}
            {order.supplierPhone || order.supplierWhatsapp || order.supplierEmail ? (
              <span className="lg:col-span-2">
                {t("purchasing.orders.contacts")}: {[order.supplierWhatsapp ?? order.supplierPhone ? formatPhone(order.supplierWhatsapp ?? order.supplierPhone) : null, order.supplierEmail].filter(Boolean).join(" · ")}
              </span>
            ) : null}
          </div>
        ) : null}
      </Card>

      <Card className="mb-3">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b p-3">
          <h2 className="font-medium">{t("purchasing.orders.lines")}</h2>
          {canEdit ? (
            <div className="flex w-full flex-col gap-1 sm:w-96">
              <ItemPicker
                onSelect={(it) => {
                  const si = supplierItems.data?.find((x) => x.itemId === it.id);
                  setForm((f) =>
                    f.lines.some((l) => l.itemId === it.id)
                      ? f
                      : {
                          ...f,
                          lines: [
                            ...f.lines,
                            {
                              key: nextKey(), itemId: it.id, itemName: it.name, itemSku: it.sku, supplierSku: si?.supplierSku ?? null, baseUnit: it.baseUnit,
                              qty: "1", price: si && si.lastPrice > 0 ? String(si.lastPrice / 100) : "", receivedQty: 0, remainingQty: 0,
                            },
                          ],
                        },
                  );
                  setDirty(true);
                }}
              />
              <span className="text-xs text-muted-foreground">{t("purchasing.orders.lastPrice")}</span>
            </div>
          ) : null}
        </div>
        <div className="overflow-x-auto">
          <Table>
            <THead>
              <TR>
                <TH>{t("purchasing.orders.item")}</TH>
                <TH className="w-36 text-right">{t("purchasing.orders.qty")}</TH>
                <TH className="w-36 text-right">{t("purchasing.orders.unitPrice")}</TH>
                <TH className="text-right">{t("purchasing.orders.lineTotal")}</TH>
                {order && status !== "Draft" ? <TH className="text-right">{t("purchasing.orders.receivedQty")}</TH> : null}
                {canEdit ? <TH className="w-10" /> : null}
              </TR>
            </THead>
            <TBody>
              {form.lines.length === 0 ? (
                <TR>
                  <TD colSpan={6} className="py-8 text-center text-sm text-muted-foreground">
                    {t("purchasing.orders.noLines")}
                  </TD>
                </TR>
              ) : (
                form.lines.map((l) => (
                  <TR key={l.key}>
                    <TD>
                      <div className="font-medium">{l.itemName}</div>
                      <div className="text-xs text-muted-foreground">
                        {l.itemSku}
                        {l.supplierSku ? ` · ${t("purchasing.orders.sku")} ${l.supplierSku}` : ""}
                      </div>
                    </TD>
                    <TD className="text-right">
                      {canEdit ? (
                        <div className="flex items-center justify-end gap-1">
                          <Input className="h-8 w-20 text-right" inputMode="decimal" value={l.qty} onChange={(e) => patchLine(l.key, { qty: e.target.value })} />
                          <span className="w-8 text-left text-xs text-muted-foreground">{unitLabel(l.baseUnit)}</span>
                        </div>
                      ) : (
                        <span className="tabular">
                          {formatNumber(parseNum(l.qty))} {unitLabel(l.baseUnit)}
                        </span>
                      )}
                    </TD>
                    <TD className="text-right">
                      {canEdit ? (
                        <Input className="h-8 w-28 text-right" inputMode="decimal" value={l.price} onChange={(e) => patchLine(l.key, { price: e.target.value })} />
                      ) : (
                        <span className="tabular">{formatMoney(toMinor(parseNum(l.price) ?? 0))}</span>
                      )}
                    </TD>
                    <TD className="tabular whitespace-nowrap text-right">{formatMoney(lineTotal(l))}</TD>
                    {order && status !== "Draft" ? (
                      <TD className={`tabular text-right ${l.receivedQty >= (parseNum(l.qty) ?? 0) ? "text-success" : ""}`}>
                        {formatNumber(l.receivedQty)} / {formatNumber(parseNum(l.qty))}
                      </TD>
                    ) : null}
                    {canEdit ? (
                      <TD>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-label={t("common.delete")}
                          onClick={() => {
                            setForm((f) => ({ ...f, lines: f.lines.filter((x) => x.key !== l.key) }));
                            setDirty(true);
                          }}
                        >
                          <Trash2 />
                        </Button>
                      </TD>
                    ) : null}
                  </TR>
                ))
              )}
            </TBody>
            {form.lines.length > 0 ? (
              <TFoot>
                <TR>
                  <TD colSpan={3} className="text-right font-medium">
                    {t("common.total")}
                  </TD>
                  <TD className="tabular whitespace-nowrap text-right font-semibold">{formatMoney(total)}</TD>
                  {order && status !== "Draft" ? <TD /> : null}
                  {canEdit ? <TD /> : null}
                </TR>
              </TFoot>
            ) : null}
          </Table>
        </div>
      </Card>

      {order && status !== "Draft" ? (
        <div className="grid gap-3 lg:grid-cols-2">
          <Card className="p-4">
            <h2 className="mb-2 font-medium">{t("purchasing.orders.receipts")}</h2>
            {order.receipts.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t("purchasing.orders.noReceipts")}</p>
            ) : (
              <ul className="space-y-1 text-sm">
                {order.receipts.map((d) => (
                  <li key={d.id} className="flex items-center justify-between gap-2">
                    <Link className="text-primary hover:underline" href={href(`inventory/documents/${d.id}`)}>
                      {d.number}
                    </Link>
                    <span className="text-muted-foreground">{formatDateTime(d.postedAt ?? d.createdAt)}</span>
                    <span className="tabular">{formatMoney(d.totalCost)}</span>
                    <DocStatusBadge status={d.status} />
                  </li>
                ))}
              </ul>
            )}
          </Card>
          <Card className="p-4">
            <h2 className="mb-2 font-medium">{t("purchasing.orders.invoices")}</h2>
            {order.invoices.length === 0 ? (
              <p className="text-sm text-muted-foreground">—</p>
            ) : (
              <ul className="space-y-1 text-sm">
                {order.invoices.map((i) => (
                  <li key={i.id} className="flex items-center justify-between gap-2">
                    <span className="font-medium">{i.number}</span>
                    <span className="text-muted-foreground">{formatDate(i.date)}</span>
                    <span className="tabular">
                      {formatMoney(i.paidAmount)} / {formatMoney(i.amount)}
                    </span>
                    <InvoiceStatusBadge status={i.status} />
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </div>
      ) : null}

      {order && sendOpen ? (
        <SendDialog
          order={order}
          supplierName={supplierName ?? ""}
          onClose={() => setSendOpen(false)}
          onSent={(o) => {
            apply(o);
            setSendOpen(false);
          }}
        />
      ) : null}
      {order && cancelOpen ? (
        <CancelDialog
          order={order}
          onClose={() => setCancelOpen(false)}
          onDone={(o) => {
            apply(o);
            setCancelOpen(false);
          }}
        />
      ) : null}
    </div>
  );
}

function SendDialog({ order, supplierName, onClose, onSent }: { order: PurchaseOrder; supplierName: string; onClose: () => void; onSent: (o: PurchaseOrder) => void }) {
  const [via, setVia] = React.useState<string>(order.sentVia ?? (order.supplierWhatsapp ? "whatsapp" : "pdf"));
  const [busy, setBusy] = React.useState(false);
  const textQuery = useQuery({
    queryKey: ["purchase-order-text", order.id, order.version],
    queryFn: () => api<string>(`/purchase-orders/${order.id}/export`, { query: { format: "text" } }),
  });
  const text = textQuery.data ?? "";
  const wa = whatsappLink(order.supplierWhatsapp ?? order.supplierPhone, text);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(text);
      toast.success(t("purchasing.orders.textCopied"));
      setVia("whatsapp");
    } catch {
      toast.error(t("errors.INTERNAL_ERROR"));
    }
  };

  const send = async () => {
    setBusy(true);
    try {
      const o = await api<PurchaseOrder>(`/purchase-orders/${order.id}/send`, {
        method: "POST",
        body: { sentVia: via, version: order.version } satisfies Schemas["SendPurchaseOrderRequest"],
      });
      if (o.status === "PendingApproval") toast.warning(t("purchasing.orders.pendingApproval", { threshold: formatMoney(o.approvalThreshold) }), { duration: 8000 });
      else toast.success(t("purchasing.orders.sentDone"));
      onSent(o);
    } catch (e) {
      toast.error(errText(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent wide>
        <DialogHeader>
          <DialogTitle>{t("purchasing.orders.sendTitle", { number: order.number })}</DialogTitle>
          <DialogDescription>
            {supplierName} · {formatMoney(order.total)}. {t("purchasing.orders.sendHint")}
          </DialogDescription>
        </DialogHeader>
        {order.needsApproval && order.status === "Draft" ? (
          <Banner tone="warning">{t("purchasing.orders.overThreshold", { threshold: formatMoney(order.approvalThreshold) })}</Banner>
        ) : null}
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" size="sm" onClick={() => download(`/purchase-orders/${order.id}/export`, { format: "pdf" }, `${order.number}.pdf`).then(() => setVia("pdf"))}>
            <Download /> {t("purchasing.orders.downloadPdf")}
          </Button>
          <Button variant="outline" size="sm" onClick={() => download(`/purchase-orders/${order.id}/export`, { format: "xlsx" }, `${order.number}.xlsx`).then(() => setVia("excel"))}>
            <Download /> {t("purchasing.orders.downloadXlsx")}
          </Button>
          <Button variant="outline" size="sm" onClick={copy} disabled={!text}>
            <Copy /> {t("purchasing.orders.copyText")}
          </Button>
          {wa ? (
            <Button variant="outline" size="sm" asChild>
              <a href={wa} target="_blank" rel="noreferrer" onClick={() => setVia("whatsapp")}>
                <MessageCircle /> {t("purchasing.orders.openWhatsapp")}
              </a>
            </Button>
          ) : null}
        </div>
        <Textarea readOnly rows={10} className="font-mono text-xs" value={textQuery.isLoading ? t("common.loading") : text} />
        <Field label={t("purchasing.orders.sendVia")}>
          <NativeSelect value={via} onChange={(e) => setVia(e.target.value)}>
            {SENT_VIA.map((v) => (
              <option key={v} value={v}>
                {t(`purchasing.sentVia.${v}`)}
              </option>
            ))}
          </NativeSelect>
        </Field>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            {t("common.cancel")}
          </Button>
          <Button onClick={send} loading={busy} data-testid="mark-sent">
            <Send /> {t("purchasing.orders.markSent")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function CancelDialog({ order, onClose, onDone }: { order: PurchaseOrder; onClose: () => void; onDone: (o: PurchaseOrder) => void }) {
  const [comment, setComment] = React.useState("");
  const [busy, setBusy] = React.useState(false);
  const needReason = order.status === "Sent";
  const submit = async () => {
    if (needReason && !comment.trim()) return toast.error(t("purchasing.requests.commentRequired"));
    setBusy(true);
    try {
      const o = await api<PurchaseOrder>(`/purchase-orders/${order.id}/cancel`, {
        method: "POST",
        body: { comment: comment.trim() || null, version: order.version } satisfies Schemas["PurchaseOrderActionRequest"],
      });
      toast.success(t("purchasing.orders.cancelled"));
      onDone(o);
    } catch (e) {
      toast.error(errText(e));
    } finally {
      setBusy(false);
    }
  };
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("purchasing.orders.cancelTitle")}</DialogTitle>
        </DialogHeader>
        <Field label={t("purchasing.orders.cancelReason")}>
          <Textarea rows={3} value={comment} onChange={(e) => setComment(e.target.value)} />
        </Field>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            {t("common.cancel")}
          </Button>
          <Button variant="destructive" onClick={submit} loading={busy}>
            <Ban /> {t("purchasing.orders.cancel")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
