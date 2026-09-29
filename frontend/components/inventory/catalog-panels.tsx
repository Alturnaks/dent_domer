"use client";

import * as React from "react";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { AlertTriangle, ChevronLeft, ChevronRight, Pencil, Plus, Search } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { EmptyState } from "@/components/ui/empty-state";
import { Input, NativeSelect, Textarea } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { TableSkeleton } from "@/components/ui/skeleton";
import { api, ApiError } from "@/lib/api-client";
import { formatPhone } from "@/lib/format";
import { t } from "@/lib/i18n";
import type { Schemas } from "@/lib/types";
import { categoryOptions, useDebounced, useItemCategories, type ItemCategory, type Supplier } from "./shared";

const errText = (e: unknown) => (e instanceof ApiError ? e.userMessage : t("errors.INTERNAL_ERROR"));

// ================= Категории =================

export function CategoriesPanel({ canManage }: { canManage: boolean }) {
  const categories = useItemCategories();
  const [editing, setEditing] = React.useState<ItemCategory | "new" | null>(null);
  const options = categoryOptions(categories.data);
  const byId = new Map((categories.data ?? []).map((c) => [c.id, c]));

  return (
    <Card>
      {canManage ? (
        <div className="flex justify-end border-b p-3">
          <Button size="sm" onClick={() => setEditing("new")}>
            <Plus /> {t("inventory.categories.new")}
          </Button>
        </div>
      ) : null}
      {categories.isLoading ? (
        <TableSkeleton cols={2} />
      ) : categories.isError ? (
        <EmptyState className="m-4" icon={<AlertTriangle className="h-8 w-8" />} title={errText(categories.error)} />
      ) : options.length === 0 ? (
        <EmptyState className="m-4" title={t("inventory.categories.empty")} description={t("inventory.categories.emptyHint")} />
      ) : (
        <Table>
          <THead>
            <TR>
              <TH>{t("inventory.categories.name")}</TH>
              <TH>{t("inventory.categories.parent")}</TH>
              {canManage ? <TH className="w-12" /> : null}
            </TR>
          </THead>
          <TBody>
            {options.map((o) => {
              const c = byId.get(o.id)!;
              return (
                <TR key={o.id}>
                  <TD className="whitespace-pre font-medium">{o.label}</TD>
                  <TD className="text-muted-foreground">{c.parentId ? (byId.get(c.parentId)?.name ?? "—") : "—"}</TD>
                  {canManage ? (
                    <TD>
                      <Button variant="ghost" size="icon-sm" aria-label={t("common.edit")} onClick={() => setEditing(c)}>
                        <Pencil />
                      </Button>
                    </TD>
                  ) : null}
                </TR>
              );
            })}
          </TBody>
        </Table>
      )}
      <CategoryDialog value={editing} onClose={() => setEditing(null)} all={categories.data ?? []} />
    </Card>
  );
}

function CategoryDialog({ value, onClose, all }: { value: ItemCategory | "new" | null; onClose: () => void; all: ItemCategory[] }) {
  const qc = useQueryClient();
  const current = value && value !== "new" ? value : null;
  const [name, setName] = React.useState("");
  const [parentId, setParentId] = React.useState("");
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState("");

  React.useEffect(() => {
    if (!value) return;
    setName(current?.name ?? "");
    setParentId(current?.parentId ?? "");
    setError("");
  }, [value, current]);

  // Нельзя выбрать саму категорию и её потомков родителем.
  const excluded = React.useMemo(() => {
    const s = new Set<string>();
    if (!current) return s;
    const walk = (id: string) => {
      s.add(id);
      all.filter((c) => c.parentId === id).forEach((c) => !s.has(c.id) && walk(c.id));
    };
    walk(current.id);
    return s;
  }, [current, all]);

  const save = async () => {
    if (!name.trim()) {
      setError(t("common.required"));
      return;
    }
    setBusy(true);
    try {
      const body: Schemas["ItemCategoryRequest"] = { name: name.trim(), parentId: parentId || null };
      await api(current ? `/item-categories/${current.id}` : "/item-categories", { method: current ? "PATCH" : "POST", body });
      toast.success(t("inventory.categories.saved"));
      qc.invalidateQueries({ queryKey: ["item-categories"] });
      onClose();
    } catch (e) {
      toast.error(errText(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={!!value} onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{current ? t("inventory.categories.edit") : t("inventory.categories.new")}</DialogTitle>
        </DialogHeader>
        <form
          className="space-y-3"
          onSubmit={(e) => {
            e.preventDefault();
            save();
          }}
        >
          <Field label={t("inventory.categories.name")} error={error}>
            <Input autoFocus value={name} aria-invalid={!!error} onChange={(e) => setName(e.target.value)} />
          </Field>
          <Field label={t("inventory.categories.parent")}>
            <NativeSelect value={parentId} onChange={(e) => setParentId(e.target.value)}>
              <option value="">{t("inventory.categories.noParent")}</option>
              {categoryOptions(all)
                .filter((o) => !excluded.has(o.id))
                .map((o) => (
                  <option key={o.id} value={o.id}>
                    {o.label}
                  </option>
                ))}
            </NativeSelect>
          </Field>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" loading={busy} disabled={busy}>
              {t("common.save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ================= Поставщики =================

const SUPPLIERS_PAGE = 50;

export function SuppliersPanel({ canManage }: { canManage: boolean }) {
  const [q, setQ] = React.useState("");
  const [page, setPage] = React.useState(1);
  const [editing, setEditing] = React.useState<Supplier | "new" | null>(null);
  const search = useDebounced(q.trim());
  React.useEffect(() => setPage(1), [search]);

  const query = useQuery({
    queryKey: ["suppliers", "list", search, page],
    queryFn: () => api<Schemas["PagedResultOfSupplierDto"]>("/suppliers", { query: { q: search || undefined, page, page_size: SUPPLIERS_PAGE } }),
    placeholderData: keepPreviousData,
  });
  const data = query.data;
  const rows = data?.items ?? [];
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  return (
    <Card>
      <div className="flex flex-wrap items-center gap-3 border-b p-3">
        <div className="relative min-w-56 flex-1">
          <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input className="pl-8" placeholder={t("inventory.suppliers.searchPlaceholder")} value={q} onChange={(e) => setQ(e.target.value)} />
        </div>
        {canManage ? (
          <Button size="sm" onClick={() => setEditing("new")}>
            <Plus /> {t("inventory.suppliers.new")}
          </Button>
        ) : null}
      </div>
      {query.isLoading ? (
        <TableSkeleton cols={5} />
      ) : query.isError ? (
        <EmptyState className="m-4" icon={<AlertTriangle className="h-8 w-8" />} title={errText(query.error)} />
      ) : rows.length === 0 ? (
        <EmptyState className="m-4" title={t("inventory.suppliers.empty")} description={search ? undefined : t("inventory.suppliers.emptyHint")} />
      ) : (
        <Table>
          <THead>
            <TR>
              <TH>{t("inventory.suppliers.name")}</TH>
              <TH>{t("inventory.suppliers.bin")}</TH>
              <TH>{t("inventory.suppliers.contactPerson")}</TH>
              <TH>{t("inventory.suppliers.phone")}</TH>
              <TH className="text-right">{t("inventory.suppliers.paymentTerms")}</TH>
            </TR>
          </THead>
          <TBody>
            {rows.map((s) => (
              <TR key={s.id} className={canManage ? "cursor-pointer" : ""} onClick={() => canManage && setEditing(s)}>
                <TD>
                  <div className="font-medium">{s.name}</div>
                  {s.email ? <div className="text-xs text-muted-foreground">{s.email}</div> : null}
                </TD>
                <TD className="tabular">{s.bin ?? "—"}</TD>
                <TD>{s.contactPerson ?? "—"}</TD>
                <TD className="whitespace-nowrap">{formatPhone(s.phone)}</TD>
                <TD className="tabular text-right">{s.paymentTermsDays ? t("inventory.suppliers.days", { n: s.paymentTermsDays }) : "—"}</TD>
              </TR>
            ))}
          </TBody>
        </Table>
      )}
      {data && data.total > data.pageSize ? (
        <div className="flex items-center justify-end gap-2 border-t p-3 text-sm">
          <span className="text-muted-foreground">
            {t("inventory.documents.pageInfo", { from: (data.page - 1) * data.pageSize + 1, to: Math.min(data.page * data.pageSize, data.total), total: data.total })}
          </span>
          <Button variant="outline" size="icon-sm" disabled={page <= 1} onClick={() => setPage((p) => p - 1)} aria-label={t("common.prev")}>
            <ChevronLeft />
          </Button>
          <Button variant="outline" size="icon-sm" disabled={page >= pages} onClick={() => setPage((p) => p + 1)} aria-label={t("common.next")}>
            <ChevronRight />
          </Button>
        </div>
      ) : null}
      <SupplierDialog value={editing} onClose={() => setEditing(null)} />
    </Card>
  );
}

function SupplierDialog({ value, onClose }: { value: Supplier | "new" | null; onClose: () => void }) {
  const qc = useQueryClient();
  const current = value && value !== "new" ? value : null;
  const [f, setF] = React.useState({ name: "", bin: "", contactPerson: "", phone: "", email: "", whatsapp: "", paymentTermsDays: "", notes: "" });
  const [error, setError] = React.useState("");
  const [busy, setBusy] = React.useState(false);

  React.useEffect(() => {
    if (!value) return;
    setF({
      name: current?.name ?? "",
      bin: current?.bin ?? "",
      contactPerson: current?.contactPerson ?? "",
      phone: current?.phone ?? "",
      email: current?.email ?? "",
      whatsapp: current?.whatsapp ?? "",
      paymentTermsDays: current ? String(current.paymentTermsDays) : "",
      notes: current?.notes ?? "",
    });
    setError("");
  }, [value, current]);

  const upd = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => setF((s) => ({ ...s, [k]: e.target.value }));

  const save = async () => {
    if (!f.name.trim()) {
      setError(t("common.required"));
      return;
    }
    const n = (v: string) => v.trim() || null;
    const days = parseInt(f.paymentTermsDays, 10);
    const body: Schemas["SupplierRequest"] = {
      name: f.name.trim(),
      bin: n(f.bin),
      contactPerson: n(f.contactPerson),
      phone: n(f.phone),
      email: n(f.email),
      whatsapp: n(f.whatsapp),
      paymentTermsDays: Number.isFinite(days) ? days : null,
      notes: n(f.notes),
    };
    setBusy(true);
    try {
      await api(current ? `/suppliers/${current.id}` : "/suppliers", { method: current ? "PATCH" : "POST", body });
      toast.success(t("inventory.suppliers.saved"));
      qc.invalidateQueries({ queryKey: ["suppliers"] });
      onClose();
    } catch (e) {
      toast.error(errText(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={!!value} onOpenChange={(o) => !o && onClose()}>
      <DialogContent wide>
        <DialogHeader>
          <DialogTitle>{current ? t("inventory.suppliers.edit") : t("inventory.suppliers.new")}</DialogTitle>
        </DialogHeader>
        <form
          className="grid gap-3 sm:grid-cols-2"
          onSubmit={(e) => {
            e.preventDefault();
            save();
          }}
        >
          <Field label={t("inventory.suppliers.name")} error={error} className="sm:col-span-2">
            <Input autoFocus value={f.name} aria-invalid={!!error} onChange={upd("name")} />
          </Field>
          <Field label={t("inventory.suppliers.bin")}>
            <Input inputMode="numeric" maxLength={12} value={f.bin} onChange={upd("bin")} />
          </Field>
          <Field label={t("inventory.suppliers.contactPerson")}>
            <Input value={f.contactPerson} onChange={upd("contactPerson")} />
          </Field>
          <Field label={t("inventory.suppliers.phone")}>
            <Input type="tel" value={f.phone} onChange={upd("phone")} />
          </Field>
          <Field label={t("inventory.suppliers.whatsapp")}>
            <Input type="tel" value={f.whatsapp} onChange={upd("whatsapp")} />
          </Field>
          <Field label={t("inventory.suppliers.email")}>
            <Input type="email" value={f.email} onChange={upd("email")} />
          </Field>
          <Field label={t("inventory.suppliers.paymentTerms")}>
            <Input inputMode="numeric" value={f.paymentTermsDays} onChange={upd("paymentTermsDays")} />
          </Field>
          <Field label={t("inventory.suppliers.notes")} className="sm:col-span-2">
            <Textarea rows={2} value={f.notes} onChange={upd("notes")} />
          </Field>
          <DialogFooter className="sm:col-span-2">
            <Button type="button" variant="outline" onClick={onClose}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" loading={busy} disabled={busy}>
              {t("common.save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
