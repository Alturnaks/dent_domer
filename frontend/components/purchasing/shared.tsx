"use client";

// Общие типы и хелперы модуля «Закупки».
import * as React from "react";
import { Info } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { ApiError } from "@/lib/api-client";
import { t } from "@/lib/i18n";
import { cn } from "@/lib/utils";
import type { Schemas } from "@/lib/types";

export type RequestStatus = Schemas["PurchaseRequestStatus"];
export type RequestSource = Schemas["PurchaseRequestSource"];
export type OrderStatus = Schemas["PurchaseOrderStatus"];
export type InvoiceStatus = Schemas["SupplierInvoiceStatus"];
export type PurchaseRequest = Schemas["PurchaseRequestDto"];
export type PurchaseRequestListItem = Schemas["PurchaseRequestListItem"];
export type PurchaseOrder = Schemas["PurchaseOrderDto"];
export type PurchaseOrderListItem = Schemas["PurchaseOrderListItem"];
export type SupplierInvoice = Schemas["SupplierInvoiceDto"];
export type NetworkDemand = Schemas["NetworkDemandDto"];
export type DemandRow = Schemas["DemandRow"];

export const REQUEST_STATUSES: RequestStatus[] = ["Draft", "Submitted", "Processed", "Rejected"];
export const ORDER_STATUSES: OrderStatus[] = ["Draft", "PendingApproval", "Sent", "PartiallyReceived", "Received", "Cancelled"];
export const INVOICE_STATUSES: InvoiceStatus[] = ["Unpaid", "PartiallyPaid", "Paid"];
export const SENT_VIA = ["whatsapp", "pdf", "excel", "email", "manual"] as const;

type Variant = "muted" | "warning" | "success" | "default" | "destructive" | "secondary";

const REQUEST_VARIANT: Record<RequestStatus, Variant> = { Draft: "muted", Submitted: "default", Processed: "success", Rejected: "destructive" };
const ORDER_VARIANT: Record<OrderStatus, Variant> = {
  Draft: "muted",
  PendingApproval: "warning",
  Sent: "default",
  PartiallyReceived: "secondary",
  Received: "success",
  Cancelled: "destructive",
};
const INVOICE_VARIANT: Record<InvoiceStatus, Variant> = { Unpaid: "warning", PartiallyPaid: "secondary", Paid: "success" };

export function RequestStatusBadge({ status }: { status: RequestStatus }) {
  return <Badge variant={REQUEST_VARIANT[status]}>{t(`purchasing.requestStatus.${status}`)}</Badge>;
}

export function OrderStatusBadge({ status }: { status: OrderStatus }) {
  return <Badge variant={ORDER_VARIANT[status]}>{t(`purchasing.orderStatus.${status}`)}</Badge>;
}

export function InvoiceStatusBadge({ status, overdue }: { status: InvoiceStatus; overdue?: boolean }) {
  return <Badge variant={overdue ? "destructive" : INVOICE_VARIANT[status]}>{t(`purchasing.invoiceStatus.${status}`)}</Badge>;
}

export function SourceBadge({ source }: { source: RequestSource }) {
  return <Badge variant={source === "Auto" ? "secondary" : "outline"}>{t(`purchasing.source.${source}`)}</Badge>;
}

/** Сообщение ошибки: для кодов с конкретным текстом сервера (название товара) — текст сервера. */
const SPECIFIC_CODES = new Set(["VALIDATION_FAILED", "SUPPLIER_REQUIRED", "STOCK_INSUFFICIENT", "AMOUNT_INVALID", "DOCUMENT_INVALID_STATE", "SAME_WAREHOUSE"]);
export function errText(e: unknown): string {
  if (e instanceof ApiError) return SPECIFIC_CODES.has(e.code) && e.message ? e.message : e.userMessage;
  return t("errors.INTERNAL_ERROR");
}

export function Banner({ tone, children }: { tone: "warning" | "info" | "muted"; children: React.ReactNode }) {
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
