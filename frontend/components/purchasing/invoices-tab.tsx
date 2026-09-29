"use client";

import * as React from "react";
import Link from "next/link";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { AlertTriangle, ChevronLeft, ChevronRight, Wallet } from "lucide-react";
import { EmptyState } from "@/components/ui/empty-state";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect, Textarea } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Card } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { TableSkeleton } from "@/components/ui/skeleton";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { parseNum, useSupplierOptions } from "@/components/inventory/shared";
import { api, newIdempotencyKey } from "@/lib/api-client";
import { useAuth, useOrgHref } from "@/lib/auth";
import { formatDate, formatMoney, isoDate, toMinor } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import { cn } from "@/lib/utils";
import type { Schemas } from "@/lib/types";
import { INVOICE_STATUSES, InvoiceStatusBadge, errText, type SupplierInvoice } from "./shared";

const PAGE_SIZE = 50;

export function InvoicesTab() {
  const { can } = useAuth();
  const { href } = useOrgHref();
  const suppliers = useSupplierOptions();
  const [supplierId, setSupplierId] = React.useState("");
  const [status, setStatus] = React.useState("");
  const [overdue, setOverdue] = React.useState(false);
  const [page, setPage] = React.useState(1);
  const [paying, setPaying] = React.useState<SupplierInvoice | null>(null);
  const canPay = can(P.purchaseOrder);

  React.useEffect(() => setPage(1), [supplierId, status, overdue]);

  const debts = useQuery({ queryKey: ["supplier-debts"], queryFn: () => api<Schemas["SupplierDebtsDto"]>("/suppliers/debts") });
  const query = useQuery({
    queryKey: ["supplier-invoices", supplierId, status, overdue, page],
    queryFn: () =>
      api<Schemas["PagedResultOfSupplierInvoiceDto"]>("/supplier-invoices", {
        query: { supplier_id: supplierId || undefined, status: status || undefined, overdue: overdue || undefined, page, page_size: PAGE_SIZE },
      }),
    placeholderData: keepPreviousData,
  });
  const data = query.data;
  const rows = data?.items ?? [];
  const totalPages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;
  const filtered = !!(supplierId || status || overdue);

  return (
    <div className="space-y-4">
      <Card>
        <div className="flex flex-wrap items-baseline justify-between gap-2 border-b p-3">
          <h2 className="font-medium">{t("purchasing.invoices.debtsTitle")}</h2>
          {debts.data && debts.data.totalDebt > 0 ? (
            <span className="text-sm">
              {t("purchasing.invoices.totalDebt", { amount: formatMoney(debts.data.totalDebt) })}
              {debts.data.totalOverdue > 0 ? (
                <span className="text-destructive"> · {t("purchasing.invoices.totalOverdue", { amount: formatMoney(debts.data.totalOverdue) })}</span>
              ) : null}
            </span>
          ) : null}
        </div>
        {debts.isLoading ? (
          <TableSkeleton rows={3} cols={6} />
        ) : !debts.data || debts.data.rows.length === 0 ? (
          <p className="p-4 text-sm text-muted-foreground">{t("purchasing.invoices.noDebts")}</p>
        ) : (
          <Table>
            <THead>
              <TR>
                <TH>{t("purchasing.invoices.supplier")}</TH>
                <TH className="text-right">{t("purchasing.invoices.invoicesCount")}</TH>
                <TH className="text-right">{t("purchasing.invoices.amount")}</TH>
                <TH className="text-right">{t("purchasing.invoices.paid")}</TH>
                <TH className="text-right">{t("purchasing.invoices.debt")}</TH>
                <TH className="text-right">{t("purchasing.invoices.overdueDebt")}</TH>
                <TH>{t("purchasing.invoices.nextDue")}</TH>
              </TR>
            </THead>
            <TBody>
              {debts.data.rows.map((d) => (
                <TR
                  key={d.supplierId}
                  className={cn("cursor-pointer", supplierId === d.supplierId && "bg-accent")}
                  onClick={() => setSupplierId((s) => (s === d.supplierId ? "" : d.supplierId))}
                  title={t("purchasing.invoices.showInvoices")}
                >
                  <TD className="font-medium">{d.supplierName}</TD>
                  <TD className="tabular text-right">{d.invoicesCount}</TD>
                  <TD className="tabular text-right">{formatMoney(d.amount)}</TD>
                  <TD className="tabular text-right">{formatMoney(d.paid + d.returned)}</TD>
                  <TD className="tabular text-right font-medium">{formatMoney(d.debt)}</TD>
                  <TD className={cn("tabular text-right", d.overdueDebt > 0 && "font-medium text-destructive")}>{d.overdueDebt > 0 ? formatMoney(d.overdueDebt) : "—"}</TD>
                  <TD>{formatDate(d.nextDueDate)}</TD>
                </TR>
              ))}
            </TBody>
          </Table>
        )}
      </Card>

      <div>
        <h2 className="mb-2 font-medium">{t("purchasing.invoices.title")}</h2>
        <Card className="mb-3 flex flex-wrap items-center gap-3 p-3">
          <NativeSelect className="w-56" value={supplierId} onChange={(e) => setSupplierId(e.target.value)} aria-label={t("purchasing.invoices.supplier")}>
            <option value="">{t("purchasing.invoices.allSuppliers")}</option>
            {(suppliers.data ?? []).map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </NativeSelect>
          <NativeSelect className="w-48" value={status} onChange={(e) => setStatus(e.target.value)} aria-label={t("purchasing.invoices.status")}>
            <option value="">{t("purchasing.invoices.allStatuses")}</option>
            {INVOICE_STATUSES.map((s) => (
              <option key={s} value={s}>
                {t(`purchasing.invoiceStatus.${s}`)}
              </option>
            ))}
          </NativeSelect>
          <label className="flex items-center gap-2 text-sm">
            <Checkbox checked={overdue} onCheckedChange={(v) => setOverdue(!!v)} /> {t("purchasing.invoices.overdueOnly")}
          </label>
          {filtered ? (
            <Button
              variant="ghost"
              size="sm"
              onClick={() => {
                setSupplierId("");
                setStatus("");
                setOverdue(false);
              }}
            >
              {t("common.reset")}
            </Button>
          ) : null}
        </Card>
        <Card>
          {query.isLoading ? (
            <TableSkeleton cols={9} />
          ) : query.isError ? (
            <EmptyState
              className="m-4"
              icon={<AlertTriangle className="h-8 w-8" />}
              title={errText(query.error)}
              action={<Button variant="outline" onClick={() => query.refetch()}>{t("common.retry")}</Button>}
            />
          ) : rows.length === 0 ? (
            <EmptyState className="m-4" title={t("purchasing.invoices.empty")} description={filtered ? undefined : t("purchasing.invoices.emptyHint")} />
          ) : (
            <Table>
              <THead>
                <TR>
                  <TH>{t("purchasing.invoices.number")}</TH>
                  <TH>{t("purchasing.invoices.date")}</TH>
                  <TH>{t("purchasing.invoices.supplier")}</TH>
                  <TH>{t("purchasing.invoices.order")}</TH>
                  <TH className="text-right">{t("purchasing.invoices.amount")}</TH>
                  <TH className="text-right">{t("purchasing.invoices.remaining")}</TH>
                  <TH>{t("purchasing.invoices.due")}</TH>
                  <TH>{t("purchasing.invoices.status")}</TH>
                  <TH />
                </TR>
              </THead>
              <TBody className={query.isFetching ? "opacity-60" : ""}>
                {rows.map((i) => (
                  <TR key={i.id}>
                    <TD className="font-medium">{i.number}</TD>
                    <TD className="whitespace-nowrap">{formatDate(i.date)}</TD>
                    <TD>{i.supplierName}</TD>
                    <TD className="text-sm">
                      {i.purchaseOrderId ? (
                        <Link className="text-primary hover:underline" href={href(`purchasing/orders/${i.purchaseOrderId}`)}>
                          {i.purchaseOrderNumber}
                        </Link>
                      ) : null}
                      {i.stockDocumentId ? (
                        <div>
                          <Link className="text-xs text-muted-foreground hover:underline" href={href(`inventory/documents/${i.stockDocumentId}`)}>
                            {i.stockDocumentNumber}
                          </Link>
                        </div>
                      ) : null}
                      {!i.purchaseOrderId && !i.stockDocumentId ? "—" : null}
                    </TD>
                    <TD className="tabular whitespace-nowrap text-right">{formatMoney(i.amount)}</TD>
                    <TD className={cn("tabular whitespace-nowrap text-right", i.remaining > 0 && "font-medium")}>{formatMoney(i.remaining)}</TD>
                    <TD className="whitespace-nowrap">
                      {formatDate(i.dueDate)}
                      {i.overdue ? <div className="text-xs text-destructive">{t("purchasing.invoices.overdue", { days: i.daysOverdue ?? 0 })}</div> : null}
                    </TD>
                    <TD>
                      <InvoiceStatusBadge status={i.status} overdue={i.overdue} />
                    </TD>
                    <TD className="text-right">
                      {canPay && i.remaining > 0 ? (
                        <Button size="sm" variant="outline" onClick={() => setPaying(i)} data-testid="pay-invoice">
                          <Wallet /> {t("purchasing.invoices.pay")}
                        </Button>
                      ) : null}
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

      {paying ? <PayDialog invoice={paying} onClose={() => setPaying(null)} /> : null}
    </div>
  );
}

function PayDialog({ invoice, onClose }: { invoice: SupplierInvoice; onClose: () => void }) {
  const qc = useQueryClient();
  const [amount, setAmount] = React.useState(String(invoice.remaining / 100));
  const [date, setDate] = React.useState(isoDate(new Date()));
  const [comment, setComment] = React.useState("");
  const [busy, setBusy] = React.useState(false);
  const key = React.useRef(newIdempotencyKey());

  const submit = async () => {
    const minor = toMinor(parseNum(amount) ?? 0);
    if (minor <= 0 || minor > invoice.remaining) return toast.error(t("purchasing.invoices.amountInvalid"));
    setBusy(true);
    try {
      await api<SupplierInvoice>(`/supplier-invoices/${invoice.id}/payments`, {
        method: "POST",
        idempotencyKey: key.current,
        body: { amount: minor, date: date || null, comment: comment.trim() || null } satisfies Schemas["SupplierPaymentRequest"],
      });
      toast.success(t("purchasing.invoices.paid_done"));
      qc.invalidateQueries({ queryKey: ["supplier-invoices"] });
      qc.invalidateQueries({ queryKey: ["supplier-debts"] });
      qc.invalidateQueries({ queryKey: ["purchase-order"] });
      onClose();
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
          <DialogTitle>{t("purchasing.invoices.payTitle", { number: invoice.number })}</DialogTitle>
          <DialogDescription>
            {invoice.supplierName} · {t("purchasing.invoices.remaining")}: {formatMoney(invoice.remaining)}
          </DialogDescription>
        </DialogHeader>
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label={t("purchasing.invoices.payAmount")}>
            <Input inputMode="decimal" autoFocus value={amount} onChange={(e) => setAmount(e.target.value)} />
            <button type="button" className="text-xs text-primary hover:underline" onClick={() => setAmount(String(invoice.remaining / 100))}>
              {t("purchasing.invoices.payFull", { amount: formatMoney(invoice.remaining) })}
            </button>
          </Field>
          <Field label={t("purchasing.invoices.payDate")}>
            <Input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          </Field>
          <Field label={t("purchasing.invoices.payComment")} className="sm:col-span-2">
            <Textarea rows={2} value={comment} onChange={(e) => setComment(e.target.value)} />
          </Field>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            {t("common.cancel")}
          </Button>
          <Button onClick={submit} loading={busy} data-testid="confirm-pay">
            <Wallet /> {t("purchasing.invoices.pay")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
