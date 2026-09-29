"use client";

// Общие справочники и хелперы модуля «Склад».
import * as React from "react";
import { useQuery } from "@tanstack/react-query";
import { Badge } from "@/components/ui/badge";
import { api } from "@/lib/api-client";
import { t } from "@/lib/i18n";
import type { Schemas } from "@/lib/types";

export type Warehouse = Schemas["WarehouseDto"];
export type ItemCategory = Schemas["ItemCategoryDto"];
export type Item = Schemas["ItemDto"];
export type Supplier = Schemas["SupplierDto"];
export type DocType = Schemas["StockDocumentType"];
export type DocStatus = Schemas["StockDocumentStatus"];
export type BaseUnit = Schemas["BaseUnit"];

export const DOC_TYPES: DocType[] = ["Receipt", "Writeoff", "Transfer", "Inventory", "ReturnToSupplier", "VisitConsumption"];
export const DOC_STATUSES: DocStatus[] = ["Draft", "PendingApproval", "Posted", "InTransit", "Received", "Cancelled"];
export const BASE_UNITS: BaseUnit[] = ["Pcs", "G", "Ml", "Pack"];

/** Все доступные пользователю склады (фильтр по филиалу — на клиенте, центральные видны всегда). */
export function useWarehouses() {
  return useQuery({ queryKey: ["warehouses"], queryFn: () => api<Warehouse[]>("/warehouses"), staleTime: 5 * 60_000 });
}

export function filterByBranch(list: Warehouse[] | undefined, branchId: string | null): Warehouse[] {
  if (!list) return [];
  if (!branchId) return list;
  return list.filter((w) => w.branchId === null || w.branchId === branchId);
}

export function useItemCategories() {
  return useQuery({ queryKey: ["item-categories"], queryFn: () => api<ItemCategory[]>("/item-categories"), staleTime: 5 * 60_000 });
}

export function useSupplierOptions() {
  return useQuery({
    queryKey: ["suppliers", "options"],
    queryFn: () => api<Schemas["PagedResultOfSupplierDto"]>("/suppliers", { query: { page_size: 500 } }).then((r) => r.items),
    staleTime: 60_000,
  });
}

/** Категории в порядке дерева с отступами для select. */
export function categoryOptions(list: ItemCategory[] | undefined): { id: string; label: string }[] {
  if (!list) return [];
  const byParent = new Map<string | null, ItemCategory[]>();
  for (const c of list) {
    const key = c.parentId && list.some((x) => x.id === c.parentId) ? c.parentId : null;
    byParent.set(key, [...(byParent.get(key) ?? []), c]);
  }
  const out: { id: string; label: string }[] = [];
  const walk = (parent: string | null, depth: number) => {
    for (const c of (byParent.get(parent) ?? []).sort((a, b) => a.name.localeCompare(b.name, "ru"))) {
      out.push({ id: c.id, label: `${"  ".repeat(depth)}${depth ? "— " : ""}${c.name}` });
      if (depth < 8) walk(c.id, depth + 1);
    }
  };
  walk(null, 0);
  return out;
}

export function unitLabel(u: string | null | undefined): string {
  if (!u) return "";
  const s = t(`inventory.units.${u}`);
  return s.startsWith("inventory.") ? u : s;
}

export function docTypeLabel(type: DocType): string {
  return t(`inventory.docTypes.${type}`);
}

const STATUS_VARIANT: Record<DocStatus, "muted" | "warning" | "success" | "default" | "destructive" | "secondary"> = {
  Draft: "muted",
  PendingApproval: "warning",
  Posted: "success",
  InTransit: "default",
  Received: "success",
  Cancelled: "destructive",
};

export function DocStatusBadge({ status }: { status: DocStatus }) {
  return <Badge variant={STATUS_VARIANT[status]}>{t(`inventory.docStatus.${status}`)}</Badge>;
}

/** Строка ввода числа: пустая строка → null, запятая как разделитель. */
export function parseNum(v: string): number | null {
  const s = v.replace(/\s/g, "").replace(",", ".");
  if (s === "") return null;
  const n = Number(s);
  return Number.isFinite(n) ? n : null;
}

export function useDebounced<T>(value: T, ms = 300): T {
  const [v, setV] = React.useState(value);
  React.useEffect(() => {
    const id = setTimeout(() => setV(value), ms);
    return () => clearTimeout(id);
  }, [value, ms]);
  return v;
}
