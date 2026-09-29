"use client";

import * as React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { AlertTriangle, ArrowLeft, Ban, Check, Info, PackageCheck, Save, Send, Trash2 } from "lucide-react";
import { PageHeader } from "@/components/ui/empty-state";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect, Textarea } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Card } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TBody, TD, TFoot, TH, THead, TR } from "@/components/ui/table";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { useConfirm } from "@/components/ui/confirm";
import { api, ApiError, newIdempotencyKey } from "@/lib/api-client";
import { useAuth, useBranch, useOrgHref } from "@/lib/auth";
import { useReference } from "@/lib/queries";
import { formatDate, formatDateTime, formatMoney, formatNumber, toMinor } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import { cn } from "@/lib/utils";
import type { Schemas } from "@/lib/types";
import { ItemPicker } from "./item-picker";
import { DocStatusBadge, docTypeLabel, filterByBranch, parseNum, unitLabel, useSupplierOptions, useWarehouses, type DocType, type Item } from "./shared";

type Doc = Schemas["StockDocumentDto"];
type LineDto = Schemas["StockDocumentLineDto"];
type LineInput = Schemas["StockLineInput"];
type Unit = { id: string; unitName: string; factorToBase: number };

type Line = {
  key: string;
  lineId: string | null;
  itemId: string;
  itemName: string;
  itemSku: string;
  baseUnit: string;
  units: Unit[];
  trackBatches: boolean;
  trackExpiry: boolean;
  trackSerials: boolean;
  qty: string;
  unitId: string;
  price: string; // тенге за единицу ввода
  batchId: string;
  batchNumber: string;
  serialNumber: string;
  expiresAt: string;
  expectedQty: number | null;
  actual: string;
  savedTotal: number | null;
  savedQty: number | null;
};

type Form = {
  warehouseFromId: string;
  warehouseToId: string;
  supplierId: string;
  invoiceNumber: string;
  invoiceDate: string;
  reasonId: string;
  comment: string;
  lines: Line[];
};

let keySeq = 0;
const nextKey = () => `l${++keySeq}`;

// Коды, для которых сообщение сервера конкретнее (содержит название товара).
const SPECIFIC_CODES = new Set(["BATCH_REQUIRED", "EXPIRY_REQUIRED", "SERIAL_REQUIRED", "STOCK_INSUFFICIENT", "VALIDATION_FAILED", "WAREHOUSE_LOCKED", "SAME_WAREHOUSE"]);
function errText(e: unknown): string {
  if (e instanceof ApiError) return SPECIFIC_CODES.has(e.code) && e.message ? e.message : e.userMessage;
  return t("errors.INTERNAL_ERROR");
}

const PERM_EDIT: Record<DocType, string> = {
  Receipt: P.inventoryReceive,
  Writeoff: P.writeoff,
  Transfer: P.transferCreate,
  Inventory: P.inventoryCount,
  ReturnToSupplier: P.writeoff,
  VisitConsumption: "__none__",
};
const PERM_POST: Record<DocType, string> = { ...PERM_EDIT, Inventory: P.inventoryCountApprove };

function fromDto(l: LineDto): Line {
  const factor = l.unitId && l.qtyInput ? l.qty / l.qtyInput : 1;
  return {
    key: l.id,
    lineId: l.id,
    itemId: l.itemId,
    itemName: l.itemName,
    itemSku: l.itemSku,
    baseUnit: l.baseUnit,
    units: l.unitId ? [{ id: l.unitId, unitName: l.unitName ?? "", factorToBase: factor }] : [],
    trackBatches: !!l.batchNumber,
    trackExpiry: !!l.expiresAt,
    trackSerials: !!l.serialNumber,
    qty: String(l.qtyInput),
    unitId: l.unitId ?? "",
    price: l.unitCost ? String(Math.round(l.unitCost * factor) / 100) : "",
    batchId: l.batchId ?? "",
    batchNumber: l.batchNumber ?? "",
    serialNumber: l.serialNumber ?? "",
    expiresAt: l.expiresAt ?? "",
    expectedQty: l.expectedQty,
    actual: l.actualQty === null || l.actualQty === undefined ? "" : String(l.actualQty),
    savedTotal: l.totalCost,
    savedQty: l.qty,
  };
}

function fromItem(it: Item): Line {
  return {
    key: nextKey(),
    lineId: null,
    itemId: it.id,
    itemName: it.name,
    itemSku: it.sku,
    baseUnit: it.baseUnit,
    units: it.units,
    trackBatches: it.trackBatches,
    trackExpiry: it.trackExpiry,
    trackSerials: it.trackSerials,
    qty: "1",
    unitId: "",
    price: "",
    batchId: "",
    batchNumber: "",
    serialNumber: "",
    expiresAt: "",
    expectedQty: null,
    actual: "",
    savedTotal: null,
    savedQty: null,
  };
}

function formFromDoc(d: Doc): Form {
  return {
    warehouseFromId: d.warehouseFromId ?? "",
    warehouseToId: d.warehouseToId ?? "",
    supplierId: d.supplierId ?? "",
    invoiceNumber: d.invoiceNumber ?? "",
    invoiceDate: d.invoiceDate ?? "",
    reasonId: d.reasonId ?? "",
    comment: d.comment ?? "",
    lines: d.lines.map(fromDto),
  };
}

const emptyForm = (): Form => ({ warehouseFromId: "", warehouseToId: "", supplierId: "", invoiceNumber: "", invoiceDate: "", reasonId: "", comment: "", lines: [] });

function toInput(type: DocType, l: Line): LineInput {
  if (type === "Inventory") {
    const actual = parseNum(l.actual);
    return {
      itemId: l.itemId, qty: actual ?? 0, unitId: null, batchId: l.batchId || null, unitCost: null,
      batchNumber: null, serialNumber: null, expiresAt: null, actualQty: actual,
    };
  }
  const price = parseNum(l.price);
  return {
    itemId: l.itemId,
    qty: parseNum(l.qty) ?? 0,
    unitId: l.unitId || null,
    batchId: type === "Receipt" ? null : l.batchId || null,
    unitCost: type === "Receipt" && price !== null ? toMinor(price) : null,
    batchNumber: type === "Receipt" ? l.batchNumber.trim() || null : null,
    serialNumber: type === "Receipt" ? l.serialNumber.trim() || null : null,
    expiresAt: type === "Receipt" ? l.expiresAt || null : null,
    actualQty: null,
  };
}

export function DocumentEditor({ id, newType }: { id?: string; newType?: DocType }) {
  const router = useRouter();
  const qc = useQueryClient();
  const confirm = useConfirm();
  const { can } = useAuth();
  const { branchId } = useBranch();
  const { href } = useOrgHref();
  const warehouses = useWarehouses();

  const docQuery = useQuery({ queryKey: ["stock-document", id], queryFn: () => api<Doc>(`/stock-documents/${id}`), enabled: !!id });
  const doc = docQuery.data;
  const type: DocType | undefined = doc?.type ?? newType;

  const [form, setForm] = React.useState<Form>(emptyForm);
  const [dirty, setDirty] = React.useState(false);
  const [busy, setBusy] = React.useState<null | "save" | "post" | "cancel" | "receive">(null);
  const [errors, setErrors] = React.useState<Record<string, string>>({});
  const [cancelOpen, setCancelOpen] = React.useState(false);
  const [receiveOpen, setReceiveOpen] = React.useState(false);

  // Инициализация из загруженного документа (и после каждого действия).
  const loadedVersion = React.useRef<string | null>(null);
  React.useEffect(() => {
    if (!doc) return;
    const sig = `${doc.id}:${doc.version}:${doc.status}`;
    if (loadedVersion.current === sig) return;
    loadedVersion.current = sig;
    setForm(formFromDoc(doc));
    setDirty(false);
  }, [doc]);

  // Склад по умолчанию для нового документа — первый склад текущего филиала.
  React.useEffect(() => {
    if (id || !newType || !warehouses.data) return;
    const own = filterByBranch(warehouses.data, branchId);
    const def = own.find((w) => w.branchId === branchId) ?? own[0];
    if (!def) return;
    setForm((f) => {
      if (f.warehouseFromId || f.warehouseToId) return f;
      return newType === "Receipt" ? { ...f, warehouseToId: def.id } : { ...f, warehouseFromId: def.id };
    });
  }, [id, newType, warehouses.data, branchId]);

  const patch = (p: Partial<Form>) => {
    setForm((f) => ({ ...f, ...p }));
    setDirty(true);
  };
  const patchLine = (key: string, p: Partial<Line>) => {
    setForm((f) => ({ ...f, lines: f.lines.map((l) => (l.key === key ? { ...l, ...p } : l)) }));
    setDirty(true);
  };
  const removeLine = (key: string) => {
    setForm((f) => ({ ...f, lines: f.lines.filter((l) => l.key !== key) }));
    setDirty(true);
  };

  if (id && docQuery.isLoading) {
    return (
      <div className="space-y-3">
        <Skeleton className="h-8 w-80" />
        <Skeleton className="h-32 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }
  if (id && docQuery.isError) {
    return (
      <Card className="flex flex-col items-center gap-3 p-10 text-center">
        <AlertTriangle className="h-8 w-8 text-destructive" />
        <p>{errText(docQuery.error)}</p>
        <Button variant="outline" asChild>
          <Link href={href("inventory/documents")}>{t("inventory.doc.backToList")}</Link>
        </Button>
      </Card>
    );
  }
  if (!type) return null;

  const status = doc?.status ?? "Draft";
  const isDraft = status === "Draft";
  const canEdit = isDraft && can(PERM_EDIT[type]);
  const canPost = isDraft && can(PERM_POST[type]);
  const isReceipt = type === "Receipt";
  const isCount = type === "Inventory";
  const isOut = type === "Writeoff" || type === "Transfer" || type === "ReturnToSupplier";
  const canCancel =
    !!doc &&
    can(PERM_EDIT[type]) &&
    (status === "Draft" || status === "PendingApproval" || (status === "Posted" && (type === "Receipt" || type === "Writeoff" || type === "ReturnToSupplier")));
  const canReceive = !!doc && type === "Transfer" && status === "InTransit" && can(P.transferReceive);
  const creatingCount = isCount && !id;

  const allWh = warehouses.data ?? [];
  const ownWh = filterByBranch(allWh, branchId);
  const whOptions = (selected: string, list = ownWh) => {
    const sel = allWh.find((w) => w.id === selected);
    return sel && !list.some((w) => w.id === sel.id) ? [sel, ...list] : list;
  };

  // ---------- проверки ----------
  const validate = (forPost: boolean): boolean => {
    const e: Record<string, string> = {};
    if (isReceipt && !form.warehouseToId) e.warehouseToId = t("inventory.doc.validation.warehouse");
    if (!isReceipt && !form.warehouseFromId) e.warehouseFromId = t("inventory.doc.validation.warehouse");
    if (type === "Transfer") {
      if (!form.warehouseToId) e.warehouseToId = t("inventory.doc.validation.warehouseTo");
      else if (form.warehouseToId === form.warehouseFromId) e.warehouseToId = t("inventory.doc.validation.sameWarehouse");
    }
    if (!isCount) {
      for (const l of form.lines) {
        const q = parseNum(l.qty);
        if (q === null || q <= 0) e[`qty:${l.key}`] = t("inventory.doc.validation.qty");
      }
    }
    if (forPost) {
      if ((isReceipt || type === "ReturnToSupplier") && !form.supplierId) e.supplierId = t("inventory.doc.validation.supplier");
      if (type === "Writeoff" && !form.reasonId) e.reasonId = t("inventory.doc.validation.reason");
      if (!isCount && form.lines.length === 0) e.lines = t("inventory.doc.validation.lines");
    }
    setErrors(e);
    const first = Object.values(e)[0];
    if (first) toast.error(first);
    return !first;
  };

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ["stock-documents"] });
    qc.invalidateQueries({ queryKey: ["stock"] });
    qc.invalidateQueries({ queryKey: ["warehouses"] });
    qc.invalidateQueries({ queryKey: ["purchase-order"] });
    qc.invalidateQueries({ queryKey: ["purchase-orders"] });
  };

  const applyDoc = (d: Doc) => {
    qc.setQueryData(["stock-document", d.id], d);
    invalidate();
  };

  /** Сохраняет черновик; для нового документа — создаёт и переходит на его страницу. */
  const persist = async (): Promise<Doc> => {
    const lines = isCount
      ? form.lines.filter((l) => l.actual !== "" || !l.lineId).map((l) => toInput(type, l))
      : form.lines.map((l) => toInput(type, l));
    const common = {
      warehouseFromId: form.warehouseFromId || null,
      warehouseToId: isReceipt || type === "Transfer" ? form.warehouseToId || null : null,
      supplierId: form.supplierId || null,
      invoiceNumber: isReceipt ? form.invoiceNumber : null,
      invoiceDate: isReceipt ? form.invoiceDate || null : null,
      reasonId: form.reasonId || null,
      comment: form.comment,
    };
    if (!doc) {
      const created = await api<Doc>("/stock-documents", {
        method: "POST",
        body: { ...common, type, purchaseOrderId: null, lines: isCount ? null : lines } satisfies Schemas["CreateStockDocumentRequest"],
      });
      return created;
    }
    return api<Doc>(`/stock-documents/${doc.id}`, {
      method: "PATCH",
      body: { ...common, lines, version: doc.version } satisfies Schemas["UpdateStockDocumentRequest"],
    });
  };

  const onSave = async () => {
    if (!validate(false)) return;
    setBusy("save");
    try {
      const d = await persist();
      applyDoc(d);
      toast.success(t("inventory.doc.saved"));
      if (!doc) router.replace(href(`inventory/documents/${d.id}`));
    } catch (e) {
      toast.error(errText(e));
    } finally {
      setBusy(null);
    }
  };

  const onPost = async () => {
    if (!validate(true)) return;
    const ok = await confirm({
      title: t("inventory.doc.postConfirmTitle"),
      description: isCount ? t("inventory.doc.countConfirmText") : t("inventory.doc.postConfirmText"),
      confirmText: isCount ? t("inventory.doc.approveCount") : type === "Transfer" ? t("inventory.doc.postTransfer") : t("inventory.doc.post"),
    });
    if (!ok) return;
    setBusy("post");
    let current = doc;
    try {
      if (!current || dirty) {
        current = await persist();
        applyDoc(current);
      }
      const counts = isCount
        ? current.lines.filter((l) => l.actualQty !== null).map((l) => ({ lineId: l.id, actualQty: l.actualQty as number }))
        : null;
      const posted = await api<Doc>(`/stock-documents/${current.id}/post`, {
        method: "POST",
        body: { comment: null, version: current.version, counts } satisfies Schemas["StockActionRequest"],
      });
      applyDoc(posted);
      if (posted.status === "PendingApproval") toast.warning(t("inventory.doc.pendingApproval"), { duration: 8000 });
      else toast.success(type === "Transfer" ? t("inventory.doc.sentTransfer") : t("inventory.doc.posted"));
      if (!doc) router.replace(href(`inventory/documents/${posted.id}`));
    } catch (e) {
      toast.error(errText(e));
      if (current && !doc) router.replace(href(`inventory/documents/${current.id}`));
    } finally {
      setBusy(null);
    }
  };

  const onCancel = async (comment: string) => {
    if (!doc) return;
    setBusy("cancel");
    try {
      const d = await api<Doc>(`/stock-documents/${doc.id}/cancel`, {
        method: "POST",
        body: { comment: comment.trim() || null, version: doc.version, counts: null } satisfies Schemas["StockActionRequest"],
      });
      applyDoc(d);
      setCancelOpen(false);
      toast.success(t("inventory.doc.cancelled"));
    } catch (e) {
      toast.error(errText(e));
    } finally {
      setBusy(null);
    }
  };

  const onReceive = async (lines: { lineId: string; actualQty: number }[], comment: string) => {
    if (!doc) return;
    setBusy("receive");
    try {
      const d = await api<Doc>(`/stock-documents/${doc.id}/receive`, {
        method: "POST",
        body: { lines, comment: comment.trim() || null, version: doc.version } satisfies Schemas["ReceiveTransferRequest"],
        idempotencyKey: newIdempotencyKey(),
      });
      applyDoc(d);
      setReceiveOpen(false);
      toast.success(t("inventory.doc.receiveDone"));
    } catch (e) {
      toast.error(errText(e));
    } finally {
      setBusy(null);
    }
  };

  // ---------- итоги ----------
  const lineSum = (l: Line): number | null => {
    if (isReceipt) {
      const q = parseNum(l.qty);
      const p = parseNum(l.price);
      return q !== null && p !== null ? Math.round(q * toMinor(p)) : null;
    }
    return dirty && !isCount ? null : l.savedTotal;
  };
  const total = form.lines.reduce((s, l) => s + (lineSum(l) ?? 0), 0);

  const title = doc ? t("inventory.doc.title", { type: docTypeLabel(type), number: doc.number }) : t("inventory.doc.newTitle", { type: docTypeLabel(type) });

  return (
    <div className="pb-16">
      <PageHeader
        title={title}
        description={
          <Link href={href("inventory/documents")} className="inline-flex items-center gap-1 hover:underline">
            <ArrowLeft className="h-3.5 w-3.5" /> {t("inventory.doc.backToList")}
          </Link>
        }
        actions={
          <>
            {doc ? <DocStatusBadge status={doc.status} /> : null}
            {canCancel ? (
              <Button variant="outline" onClick={() => setCancelOpen(true)} disabled={!!busy}>
                <Ban /> {t("inventory.doc.cancel")}
              </Button>
            ) : null}
            {canReceive ? (
              <Button variant="success" onClick={() => setReceiveOpen(true)} disabled={!!busy}>
                <PackageCheck /> {t("inventory.doc.receive")}
              </Button>
            ) : null}
            {creatingCount ? (
              <Button onClick={onSave} loading={busy === "save"} disabled={!!busy}>
                <Check /> {t("inventory.doc.startCount")}
              </Button>
            ) : (
              <>
                {canEdit ? (
                  <Button variant="outline" onClick={onSave} loading={busy === "save"} disabled={!!busy || (!!doc && !dirty)}>
                    <Save /> {t("inventory.doc.saveDraft")}
                  </Button>
                ) : null}
                {canPost ? (
                  <Button onClick={onPost} loading={busy === "post"} disabled={!!busy}>
                    <Send /> {isCount ? t("inventory.doc.approveCount") : type === "Transfer" ? t("inventory.doc.postTransfer") : t("inventory.doc.post")}
                  </Button>
                ) : null}
              </>
            )}
          </>
        }
      />

      {doc && status === "PendingApproval" ? <Banner tone="warning">{t("inventory.doc.pendingApprovalBanner")}</Banner> : null}
      {doc && status === "InTransit" ? <Banner tone="info">{t("inventory.doc.inTransitBanner")}</Banner> : null}
      {doc && isCount && isDraft ? <Banner tone="info">{t("inventory.doc.lockedBanner")}</Banner> : null}
      {creatingCount ? <Banner tone="info">{t("inventory.doc.countCreateHint")}</Banner> : null}
      {doc && !isDraft && status !== "PendingApproval" && status !== "InTransit" ? (
        <Banner tone="muted">{t("inventory.doc.readonlyBanner", { status: t(`inventory.docStatus.${status}`) })}</Banner>
      ) : null}

      {/* ---------- шапка документа ---------- */}
      <Card className="mb-3 grid gap-3 p-4 sm:grid-cols-2 lg:grid-cols-4">
        {isReceipt ? (
          <WarehouseField label={t("inventory.doc.warehouseTo")} value={form.warehouseToId} options={whOptions(form.warehouseToId)} disabled={!canEdit} error={errors.warehouseToId} onChange={(v) => patch({ warehouseToId: v })} />
        ) : (
          <WarehouseField
            label={type === "Transfer" ? t("inventory.doc.transferFrom") : isCount ? t("inventory.doc.countWarehouse") : t("inventory.doc.warehouseFrom")}
            value={form.warehouseFromId}
            options={whOptions(form.warehouseFromId)}
            disabled={!canEdit || (isCount && !!doc)}
            error={errors.warehouseFromId}
            onChange={(v) => patch({ warehouseFromId: v, lines: isOut ? form.lines.map((l) => ({ ...l, batchId: "" })) : form.lines })}
          />
        )}
        {type === "Transfer" ? (
          <WarehouseField
            label={t("inventory.doc.transferTo")}
            value={form.warehouseToId}
            options={whOptions(form.warehouseToId, allWh).filter((w) => w.id !== form.warehouseFromId)}
            disabled={!canEdit}
            error={errors.warehouseToId}
            onChange={(v) => patch({ warehouseToId: v })}
          />
        ) : null}
        {isReceipt || type === "ReturnToSupplier" ? <SupplierField value={form.supplierId} disabled={!canEdit} error={errors.supplierId} onChange={(v) => patch({ supplierId: v })} fallbackName={doc?.supplierName} /> : null}
        {isReceipt ? (
          <>
            <Field label={t("inventory.doc.invoiceNumber")}>
              <Input value={form.invoiceNumber} disabled={!canEdit} onChange={(e) => patch({ invoiceNumber: e.target.value })} />
            </Field>
            <Field label={t("inventory.doc.invoiceDate")}>
              <Input type="date" value={form.invoiceDate} disabled={!canEdit} onChange={(e) => patch({ invoiceDate: e.target.value })} />
            </Field>
          </>
        ) : null}
        {type === "Writeoff" ? <ReasonField value={form.reasonId} disabled={!canEdit} error={errors.reasonId} onChange={(v) => patch({ reasonId: v })} fallbackName={doc?.reasonName} /> : null}
        <Field label={t("inventory.doc.comment")} className={cn(isReceipt ? "sm:col-span-2 lg:col-span-4" : "sm:col-span-2")}>
          <Textarea className="min-h-[38px]" rows={1} value={form.comment} disabled={!canEdit && !creatingCount} onChange={(e) => patch({ comment: e.target.value })} />
        </Field>
        {doc ? (
          <div className="grid gap-x-6 gap-y-1 text-sm text-muted-foreground sm:col-span-2 sm:grid-cols-2 lg:col-span-4 lg:grid-cols-4">
            <span>
              {t("inventory.doc.createdAt")}: {formatDateTime(doc.createdAt)}
              {doc.createdByName ? ` · ${doc.createdByName}` : ""}
            </span>
            {doc.postedAt ? (
              <span>
                {t("inventory.doc.postedAt")}: {formatDateTime(doc.postedAt)}
                {doc.postedByName ? ` · ${doc.postedByName}` : ""}
              </span>
            ) : null}
            {doc.receivedAt ? (
              <span>
                {t("inventory.doc.receivedAt")}: {formatDateTime(doc.receivedAt)}
              </span>
            ) : null}
            {doc.visitId ? <span>{t("inventory.doc.createdByVisit")}</span> : null}
            {doc.purchaseOrderId ? (
              <Link className="text-primary hover:underline" href={href(`purchasing/orders/${doc.purchaseOrderId}`)}>
                {t("purchasing.orders.openOrder")}
              </Link>
            ) : null}
            {doc.sourceDocumentId ? (
              <Link className="text-primary hover:underline" href={href(`inventory/documents/${doc.sourceDocumentId}`)}>
                {t("inventory.doc.source")}
              </Link>
            ) : null}
          </div>
        ) : null}
      </Card>

      {/* ---------- позиции ---------- */}
      {!creatingCount ? (
        <Card>
          <div className="flex flex-wrap items-center justify-between gap-3 border-b p-3">
            <h2 className="text-sm font-semibold">
              {t("inventory.doc.lines")} <span className="font-normal text-muted-foreground">({form.lines.length})</span>
            </h2>
            {canEdit ? (
              <ItemPicker
                className="w-full sm:w-96"
                onSelect={(it) => {
                  setForm((f) => ({ ...f, lines: [...f.lines, fromItem(it)] }));
                  setDirty(true);
                }}
              />
            ) : null}
          </div>
          {form.lines.length === 0 ? (
            <p className={cn("p-8 text-center text-sm", errors.lines ? "text-destructive" : "text-muted-foreground")}>
              {isCount ? t("inventory.doc.countNoLines") : t("inventory.doc.noLines")}
            </p>
          ) : (
            <div className="overflow-x-auto">
              <Table>
                <THead>
                  <TR>
                    <TH className="min-w-56">{t("inventory.doc.item")}</TH>
                    {isReceipt ? (
                      <>
                        <TH className="w-32">{t("inventory.doc.batchNumber")}</TH>
                        <TH className="w-36">{t("inventory.doc.expiry")}</TH>
                      </>
                    ) : isOut || isCount ? (
                      <TH className="w-48">{t("inventory.doc.batch")}</TH>
                    ) : null}
                    {isCount ? (
                      <>
                        <TH className="w-24 text-right">{t("inventory.doc.expected")}</TH>
                        <TH className="w-28 text-right">{t("inventory.doc.actual")}</TH>
                        <TH className="w-24 text-right">{t("inventory.doc.diff")}</TH>
                      </>
                    ) : (
                      <>
                        <TH className="w-28 text-right">{type === "Transfer" && status === "Received" ? t("inventory.doc.sent") : t("inventory.doc.qty")}</TH>
                        {type === "Transfer" && status === "Received" ? <TH className="w-24 text-right">{t("inventory.doc.received")}</TH> : null}
                        <TH className="w-32">{t("inventory.doc.unit")}</TH>
                        <TH className="w-32 text-right">{t("inventory.doc.price")}</TH>
                      </>
                    )}
                    <TH className="w-32 text-right">{t("inventory.doc.sum")}</TH>
                    {canEdit ? <TH className="w-10" /> : null}
                  </TR>
                </THead>
                <TBody>
                  {form.lines.map((l) => (
                    <LineRow
                      key={l.key}
                      line={l}
                      type={type}
                      status={status}
                      editable={canEdit}
                      warehouseId={form.warehouseFromId}
                      sum={lineSum(l)}
                      qtyError={errors[`qty:${l.key}`]}
                      onChange={(p) => patchLine(l.key, p)}
                      onRemove={() => removeLine(l.key)}
                    />
                  ))}
                </TBody>
                <TFoot>
                  <TR>
                    <TD colSpan={100} className="text-right">
                      <span className="mr-3 text-muted-foreground">{t("inventory.doc.total")}</span>
                      <span className="tabular font-semibold">{dirty && !isReceipt && !isCount ? "—" : formatMoney(doc && !dirty ? doc.totalCost : total)}</span>
                    </TD>
                  </TR>
                </TFoot>
              </Table>
            </div>
          )}
        </Card>
      ) : null}

      {doc ? (
        <CancelDialog
          open={cancelOpen}
          onOpenChange={setCancelOpen}
          number={doc.number}
          requireComment={doc.status === "Posted"}
          busy={busy === "cancel"}
          onConfirm={onCancel}
        />
      ) : null}
      {doc && canReceive ? <ReceiveDialog open={receiveOpen} onOpenChange={setReceiveOpen} doc={doc} busy={busy === "receive"} onConfirm={onReceive} /> : null}
    </div>
  );
}

// ================= Вспомогательные компоненты =================

function Banner({ tone, children }: { tone: "warning" | "info" | "muted"; children: React.ReactNode }) {
  return (
    <div
      className={cn(
        "mb-3 flex items-start gap-2 rounded-lg border px-3 py-2 text-sm",
        tone === "warning" && "border-warning/40 bg-warning/10",
        tone === "info" && "border-primary/30 bg-primary/5",
        tone === "muted" && "bg-muted/50 text-muted-foreground",
      )}
    >
      <Info className="mt-0.5 h-4 w-4 shrink-0" />
      <span>{children}</span>
    </div>
  );
}

function WarehouseField({
  label, value, options, disabled, error, onChange,
}: { label: string; value: string; options: Schemas["WarehouseDto"][]; disabled?: boolean; error?: string; onChange: (v: string) => void }) {
  return (
    <Field label={label} error={error}>
      <NativeSelect value={value} disabled={disabled} aria-invalid={!!error} onChange={(e) => onChange(e.target.value)}>
        <option value="">{t("inventory.doc.selectWarehouse")}</option>
        {options.map((w) => (
          <option key={w.id} value={w.id}>
            {w.name}
          </option>
        ))}
      </NativeSelect>
    </Field>
  );
}

function SupplierField({ value, disabled, error, onChange, fallbackName }: { value: string; disabled?: boolean; error?: string; onChange: (v: string) => void; fallbackName?: string | null }) {
  const suppliers = useSupplierOptions();
  const list = suppliers.data ?? [];
  return (
    <Field label={t("inventory.doc.supplier")} error={error}>
      <NativeSelect value={value} disabled={disabled} aria-invalid={!!error} onChange={(e) => onChange(e.target.value)}>
        <option value="">{t("inventory.doc.selectSupplier")}</option>
        {value && !list.some((s) => s.id === value) ? <option value={value}>{fallbackName ?? "…"}</option> : null}
        {list.map((s) => (
          <option key={s.id} value={s.id}>
            {s.name}
          </option>
        ))}
      </NativeSelect>
    </Field>
  );
}

function ReasonField({ value, disabled, error, onChange, fallbackName }: { value: string; disabled?: boolean; error?: string; onChange: (v: string) => void; fallbackName?: string | null }) {
  const reasons = useReference("/writeoff-reasons");
  const list = reasons.data ?? [];
  return (
    <Field label={t("inventory.doc.reason")} error={error}>
      <NativeSelect value={value} disabled={disabled} aria-invalid={!!error} onChange={(e) => onChange(e.target.value)}>
        <option value="">{t("inventory.doc.selectReason")}</option>
        {value && !list.some((s) => s.id === value) ? <option value={value}>{fallbackName ?? "…"}</option> : null}
        {list.map((s) => (
          <option key={s.id} value={s.id}>
            {s.name}
          </option>
        ))}
      </NativeSelect>
    </Field>
  );
}

/** Выбор партии из остатков склада-источника (пусто — автоподбор FEFO при проведении). */
function BatchSelect({ itemId, warehouseId, value, onChange }: { itemId: string; warehouseId: string; value: string; onChange: (v: string) => void }) {
  const card = useQuery({
    queryKey: ["stock", "card", itemId],
    queryFn: () => api<Schemas["ItemCardDto"]>(`/stock/items/${itemId}/card`),
    enabled: !!warehouseId,
    staleTime: 30_000,
  });
  const batches = (card.data?.balances ?? []).filter((b) => b.warehouseId === warehouseId && b.batchId && b.qty > 0);
  return (
    <NativeSelect className="h-8" value={value} onChange={(e) => onChange(e.target.value)}>
      <option value="">{t("inventory.doc.batchAuto")}</option>
      {batches.map((b) => (
        <option key={b.batchId} value={b.batchId ?? ""}>
          {b.batchNumber ?? b.serialNumber ?? "—"}
          {b.expiresAt ? ` · ${formatDate(b.expiresAt)}` : ""} · {formatNumber(b.qty)}
        </option>
      ))}
    </NativeSelect>
  );
}

function LineRow({
  line: l, type, status, editable, warehouseId, sum, qtyError, onChange, onRemove,
}: {
  line: Line;
  type: DocType;
  status: Schemas["StockDocumentStatus"];
  editable: boolean;
  warehouseId: string;
  sum: number | null;
  qtyError?: string;
  onChange: (p: Partial<Line>) => void;
  onRemove: () => void;
}) {
  const isReceipt = type === "Receipt";
  const isCount = type === "Inventory";
  const isOut = type === "Writeoff" || type === "Transfer" || type === "ReturnToSupplier";
  const base = unitLabel(l.baseUnit);
  const unitName = l.unitId ? (l.units.find((u) => u.id === l.unitId)?.unitName ?? base) : base;
  const batchText = [l.batchNumber || l.serialNumber, l.expiresAt ? formatDate(l.expiresAt) : ""].filter(Boolean).join(" · ") || "—";

  const actual = parseNum(l.actual);
  const expected = l.expectedQty ?? 0;
  const diff = actual === null ? null : actual - expected;

  return (
    <TR>
      <TD>
        <div className="font-medium">{l.itemName}</div>
        <div className="text-xs text-muted-foreground">{l.itemSku}</div>
      </TD>

      {isReceipt ? (
        <>
          <TD>
            {editable && (l.trackBatches || l.trackSerials || l.batchNumber || l.serialNumber) ? (
              <div className="space-y-1">
                {l.trackBatches || l.batchNumber || !l.trackSerials ? (
                  <Input className="h-8" placeholder={t("inventory.doc.batchNumber")} value={l.batchNumber} onChange={(e) => onChange({ batchNumber: e.target.value })} />
                ) : null}
                {l.trackSerials || l.serialNumber ? (
                  <Input className="h-8" placeholder={t("inventory.doc.serial")} value={l.serialNumber} onChange={(e) => onChange({ serialNumber: e.target.value })} />
                ) : null}
              </div>
            ) : (
              <span className="text-sm">{l.batchNumber || l.serialNumber || "—"}</span>
            )}
          </TD>
          <TD>
            {editable && (l.trackExpiry || l.expiresAt) ? (
              <Input type="date" className="h-8" value={l.expiresAt} onChange={(e) => onChange({ expiresAt: e.target.value })} />
            ) : (
              <span className="text-sm">{formatDate(l.expiresAt || null)}</span>
            )}
          </TD>
        </>
      ) : isOut ? (
        <TD>{editable ? <BatchSelect itemId={l.itemId} warehouseId={warehouseId} value={l.batchId} onChange={(v) => onChange({ batchId: v })} /> : <span className="text-sm">{batchText}</span>}</TD>
      ) : isCount ? (
        <TD className="text-sm">{batchText}</TD>
      ) : null}

      {isCount ? (
        <>
          <TD className="tabular text-right">
            {formatNumber(expected)} <span className="text-xs text-muted-foreground">{base}</span>
          </TD>
          <TD className="text-right">
            {editable ? (
              <Input className="h-8 text-right" inputMode="decimal" placeholder={formatNumber(expected)} value={l.actual} onChange={(e) => onChange({ actual: e.target.value })} />
            ) : (
              <span className="tabular">{actual === null ? "—" : formatNumber(actual)}</span>
            )}
          </TD>
          <TD className={cn("tabular text-right font-medium", diff !== null && diff < 0 && "text-destructive", diff !== null && diff > 0 && "text-success")}>
            {diff === null || diff === 0 ? "—" : `${diff > 0 ? "+" : ""}${formatNumber(diff)}`}
          </TD>
        </>
      ) : (
        <>
          <TD className="text-right">
            {editable ? (
              <Input
                className="h-8 text-right"
                inputMode="decimal"
                aria-invalid={!!qtyError}
                title={qtyError}
                value={l.qty}
                onChange={(e) => onChange({ qty: e.target.value })}
              />
            ) : (
              <span className="tabular">{formatNumber(parseNum(l.qty))}</span>
            )}
          </TD>
          {type === "Transfer" && status === "Received" ? (
            <TD className="tabular text-right">
              {/* actual хранится в базовых единицах */}
              {l.actual === "" ? "—" : formatNumber(parseNum(l.actual))}
            </TD>
          ) : null}
          <TD>
            {editable && l.units.length > 0 ? (
              <NativeSelect className="h-8" value={l.unitId} onChange={(e) => onChange({ unitId: e.target.value })}>
                <option value="">{base}</option>
                {l.units.map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.unitName} ({formatNumber(u.factorToBase)} {base})
                  </option>
                ))}
              </NativeSelect>
            ) : (
              <span className="text-sm text-muted-foreground">{unitName}</span>
            )}
          </TD>
          <TD className="text-right">
            {editable && isReceipt ? (
              <Input className="h-8 text-right" inputMode="decimal" placeholder="0" value={l.price} onChange={(e) => onChange({ price: e.target.value })} />
            ) : (
              <span className="tabular text-sm">{l.price ? formatMoney(toMinor(l.price)) : "—"}</span>
            )}
          </TD>
        </>
      )}

      <TD className={cn("tabular whitespace-nowrap text-right", sum !== null && sum < 0 && "text-destructive")}>{sum === null ? "—" : formatMoney(sum)}</TD>
      {editable ? (
        <TD>
          {!(isCount && l.lineId) ? (
            <Button variant="ghost" size="icon-sm" onClick={onRemove} aria-label={t("common.delete")}>
              <Trash2 />
            </Button>
          ) : null}
        </TD>
      ) : null}
    </TR>
  );
}

function CancelDialog({
  open, onOpenChange, number, requireComment, busy, onConfirm,
}: { open: boolean; onOpenChange: (o: boolean) => void; number: string; requireComment: boolean; busy: boolean; onConfirm: (comment: string) => void }) {
  const [comment, setComment] = React.useState("");
  React.useEffect(() => {
    if (open) setComment("");
  }, [open]);
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("inventory.doc.cancelTitle", { number })}</DialogTitle>
          <DialogDescription>{requireComment ? t("inventory.doc.cancelPostedText") : t("inventory.doc.cancelDraftText")}</DialogDescription>
        </DialogHeader>
        <Field label={t("inventory.doc.cancelReason")}>
          <Textarea value={comment} onChange={(e) => setComment(e.target.value)} autoFocus />
        </Field>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t("common.close")}
          </Button>
          <Button variant="destructive" loading={busy} disabled={busy || (requireComment && !comment.trim())} onClick={() => onConfirm(comment)}>
            {t("inventory.doc.cancel")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function ReceiveDialog({
  open, onOpenChange, doc, busy, onConfirm,
}: { open: boolean; onOpenChange: (o: boolean) => void; doc: Doc; busy: boolean; onConfirm: (lines: { lineId: string; actualQty: number }[], comment: string) => void }) {
  const [values, setValues] = React.useState<Record<string, string>>({});
  const [comment, setComment] = React.useState("");
  React.useEffect(() => {
    if (open) {
      setValues(Object.fromEntries(doc.lines.map((l) => [l.id, String(l.qty)])));
      setComment("");
    }
  }, [open, doc]);
  const invalid = doc.lines.some((l) => {
    const v = parseNum(values[l.id] ?? "");
    return v === null || v < 0 || v > l.qty;
  });
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent wide>
        <DialogHeader>
          <DialogTitle>{t("inventory.doc.receiveTitle", { number: doc.number })}</DialogTitle>
          <DialogDescription>{t("inventory.doc.receiveHint")}</DialogDescription>
        </DialogHeader>
        <div className="max-h-[50vh] overflow-y-auto rounded-lg border">
          <Table>
            <THead>
              <TR>
                <TH>{t("inventory.doc.item")}</TH>
                <TH>{t("inventory.doc.batch")}</TH>
                <TH className="text-right">{t("inventory.doc.sent")}</TH>
                <TH className="w-32 text-right">{t("inventory.doc.received")}</TH>
              </TR>
            </THead>
            <TBody>
              {doc.lines.map((l) => {
                const v = parseNum(values[l.id] ?? "");
                const bad = v === null || v < 0 || v > l.qty;
                return (
                  <TR key={l.id}>
                    <TD>
                      <div className="font-medium">{l.itemName}</div>
                      <div className="text-xs text-muted-foreground">{l.itemSku}</div>
                    </TD>
                    <TD className="text-sm">{[l.batchNumber ?? l.serialNumber, l.expiresAt ? formatDate(l.expiresAt) : ""].filter(Boolean).join(" · ") || "—"}</TD>
                    <TD className="tabular text-right">
                      {formatNumber(l.qty)} {unitLabel(l.baseUnit)}
                    </TD>
                    <TD>
                      <Input
                        className={cn("h-8 text-right", !bad && v !== null && v < l.qty && "border-warning")}
                        inputMode="decimal"
                        aria-invalid={bad}
                        value={values[l.id] ?? ""}
                        onChange={(e) => setValues((s) => ({ ...s, [l.id]: e.target.value }))}
                      />
                    </TD>
                  </TR>
                );
              })}
            </TBody>
          </Table>
        </div>
        <Field label={t("inventory.doc.comment")}>
          <Textarea rows={2} value={comment} onChange={(e) => setComment(e.target.value)} />
        </Field>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t("common.cancel")}
          </Button>
          <Button
            variant="success"
            loading={busy}
            disabled={busy || invalid}
            onClick={() => onConfirm(doc.lines.map((l) => ({ lineId: l.id, actualQty: parseNum(values[l.id] ?? "") ?? 0 })), comment)}
          >
            <PackageCheck /> {t("inventory.doc.receive")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
