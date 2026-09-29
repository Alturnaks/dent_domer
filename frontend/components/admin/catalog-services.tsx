"use client";

import * as React from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { FlaskConical, Pencil, Plus, Search, Trash2 } from "lucide-react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Checkbox, Switch } from "@/components/ui/checkbox";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { TableSkeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { ErrorBlock, toastError, useDebounced } from "@/components/admin/common";
import { api } from "@/lib/api-client";
import { useAuth } from "@/lib/auth";
import { useServiceCategories } from "@/lib/queries";
import { formatDateTime, formatMoney, formatNumber } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import { cn } from "@/lib/utils";
import type { Schemas, ServiceItem } from "@/lib/types";

type Category = Schemas["ServiceCategoryDto"];

/** Плоский список категорий в порядке дерева с уровнем вложенности. */
export function flattenCategories(cats: Category[]): { cat: Category; depth: number }[] {
  const byParent = new Map<string | null, Category[]>();
  for (const c of cats) {
    const k = c.parentId ?? null;
    byParent.set(k, [...(byParent.get(k) ?? []), c]);
  }
  const out: { cat: Category; depth: number }[] = [];
  const walk = (parent: string | null, depth: number) => {
    for (const c of (byParent.get(parent) ?? []).sort((a, b) => a.sort - b.sort || a.name.localeCompare(b.name))) {
      out.push({ cat: c, depth });
      walk(c.id, depth + 1);
    }
  };
  walk(null, 0);
  // Сироты (родитель не найден)
  for (const c of cats) if (!out.some((x) => x.cat.id === c.id)) out.push({ cat: c, depth: 0 });
  return out;
}

export function ServicesTab() {
  const { can } = useAuth();
  const canPrices = can(P.pricesManage);
  const canTech = can(P.techcardsManage);
  const cats = useServiceCategories();
  const [categoryId, setCategoryId] = React.useState("");
  const [q, setQ] = React.useState("");
  const dq = useDebounced(q.trim());
  const [inactive, setInactive] = React.useState(false);
  const [editing, setEditing] = React.useState<ServiceItem | null>(null);
  const [serviceOpen, setServiceOpen] = React.useState(false);
  const [catEdit, setCatEdit] = React.useState<Category | null>(null);
  const [catOpen, setCatOpen] = React.useState(false);
  const [techFor, setTechFor] = React.useState<ServiceItem | null>(null);

  const services = useQuery({
    queryKey: ["services", "catalog", { categoryId, dq, inactive }],
    queryFn: () => api<ServiceItem[]>("/services", { query: { category_id: categoryId || undefined, q: dq || undefined, include_inactive: inactive || undefined } }),
  });
  const flat = React.useMemo(() => flattenCategories(cats.data ?? []), [cats.data]);
  const catName = React.useMemo(() => new Map((cats.data ?? []).map((c) => [c.id, c.name])), [cats.data]);
  const rows = services.data ?? [];

  return (
    <div className="grid gap-4 lg:grid-cols-[240px_1fr]">
      <Card className="h-fit p-2">
        <div className="mb-1 flex items-center justify-between px-2 py-1">
          <span className="text-sm font-semibold">{t("catalog.categories")}</span>
          {canPrices ? (
            <Button
              size="sm"
              variant="ghost"
              onClick={() => {
                setCatEdit(null);
                setCatOpen(true);
              }}
            >
              <Plus /> {t("catalog.addCategory")}
            </Button>
          ) : null}
        </div>
        <button
          type="button"
          className={cn("w-full rounded-md px-2.5 py-1.5 text-left text-sm hover:bg-muted", !categoryId && "bg-muted font-medium")}
          onClick={() => setCategoryId("")}
        >
          {t("catalog.allCategories")}
        </button>
        {cats.isLoading ? <TableSkeleton rows={5} cols={1} /> : null}
        {flat.map(({ cat, depth }) => (
          <div key={cat.id} className="group flex items-center">
            <button
              type="button"
              style={{ paddingLeft: 10 + depth * 14 }}
              className={cn("flex-1 truncate rounded-md py-1.5 pr-2 text-left text-sm hover:bg-muted", categoryId === cat.id && "bg-muted font-medium")}
              onClick={() => setCategoryId(cat.id)}
            >
              {cat.name}
            </button>
            {canPrices ? (
              <Button
                size="icon-sm"
                variant="ghost"
                className="opacity-0 group-hover:opacity-100"
                title={t("common.edit")}
                onClick={() => {
                  setCatEdit(cat);
                  setCatOpen(true);
                }}
              >
                <Pencil />
              </Button>
            ) : null}
          </div>
        ))}
      </Card>

      <div>
        <Card className="mb-3 flex flex-wrap items-center gap-3 p-3">
          <div className="relative min-w-60 flex-1">
            <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
            <Input className="pl-8" placeholder={t("admin.searchPlaceholder")} value={q} onChange={(e) => setQ(e.target.value)} />
          </div>
          <label className="flex items-center gap-2 text-sm">
            <Checkbox checked={inactive} onCheckedChange={(v) => setInactive(v === true)} />
            {t("catalog.showInactive")}
          </label>
          {canPrices ? (
            <Button
              onClick={() => {
                setEditing(null);
                setServiceOpen(true);
              }}
            >
              <Plus /> {t("catalog.addService")}
            </Button>
          ) : null}
        </Card>
        <Card>
          {services.isLoading ? (
            <TableSkeleton />
          ) : services.isError ? (
            <ErrorBlock className="m-4" onRetry={() => services.refetch()} />
          ) : rows.length === 0 ? (
            <EmptyState className="m-4" title={t("catalog.servicesEmpty")} />
          ) : (
            <Table>
              <THead>
                <TR>
                  <TH className="w-20">{t("catalog.code")}</TH>
                  <TH>{t("catalog.name")}</TH>
                  <TH>{t("catalog.category")}</TH>
                  <TH className="text-right">{t("catalog.duration")}</TH>
                  <TH className="text-right">{t("catalog.basePrice")}</TH>
                  <TH className="w-24" />
                </TR>
              </THead>
              <TBody>
                {rows.map((s) => (
                  <TR key={s.id} className={s.isActive ? undefined : "opacity-60"}>
                    <TD className="font-mono text-xs">{s.code}</TD>
                    <TD>
                      <span className="font-medium">{s.name}</span>
                      {!s.isActive ? (
                        <Badge variant="muted" className="ml-2">
                          {t("admin.inactive")}
                        </Badge>
                      ) : null}
                    </TD>
                    <TD className="text-muted-foreground">{catName.get(s.categoryId) ?? "—"}</TD>
                    <TD className="text-right tabular-nums">{s.durationMin}</TD>
                    <TD className="whitespace-nowrap text-right tabular-nums">{formatMoney(s.price)}</TD>
                    <TD>
                      <div className="flex justify-end gap-1">
                        <Button size="icon-sm" variant="ghost" title={t("catalog.techCard")} onClick={() => setTechFor(s)}>
                          <FlaskConical />
                        </Button>
                        {canPrices ? (
                          <Button
                            size="icon-sm"
                            variant="ghost"
                            title={t("common.edit")}
                            onClick={() => {
                              setEditing(s);
                              setServiceOpen(true);
                            }}
                          >
                            <Pencil />
                          </Button>
                        ) : null}
                      </div>
                    </TD>
                  </TR>
                ))}
              </TBody>
            </Table>
          )}
        </Card>
      </div>

      <ServiceDialog open={serviceOpen} onOpenChange={setServiceOpen} service={editing} categories={flat} defaultCategory={categoryId} />
      <CategoryDialog open={catOpen} onOpenChange={setCatOpen} category={catEdit} categories={flat} />
      <TechCardDialog service={techFor} canEdit={canTech} onOpenChange={(o) => !o && setTechFor(null)} />
    </div>
  );
}

function ServiceDialog({
  open,
  onOpenChange,
  service,
  categories,
  defaultCategory,
}: {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  service: ServiceItem | null;
  categories: { cat: Category; depth: number }[];
  defaultCategory: string;
}) {
  const qc = useQueryClient();
  const [f, setF] = React.useState({ code: "", name: "", categoryId: "", durationMin: "30", isActive: true });
  React.useEffect(() => {
    if (!open) return;
    setF({
      code: service?.code ?? "",
      name: service?.name ?? "",
      categoryId: service?.categoryId ?? (defaultCategory || categories[0]?.cat.id || ""),
      durationMin: String(service?.durationMin ?? 30),
      isActive: service?.isActive ?? true,
    });
  }, [open, service, defaultCategory, categories]);

  const save = useMutation({
    mutationFn: () => {
      const body: Schemas["ServiceRequest"] = {
        code: f.code.trim(),
        name: f.name.trim(),
        categoryId: f.categoryId,
        durationMin: Number(f.durationMin) || 0,
        isActive: f.isActive,
      };
      return service ? api(`/services/${service.id}`, { method: "PATCH", body }) : api("/services", { method: "POST", body });
    },
    onSuccess: () => {
      toast.success(service ? t("catalog.serviceSaved") : t("catalog.serviceCreated"));
      void qc.invalidateQueries({ queryKey: ["services"] });
      onOpenChange(false);
    },
    onError: toastError,
  });
  const dur = Number(f.durationMin);
  const valid = f.code.trim() && f.name.trim() && f.categoryId && dur >= 5 && dur <= 600;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{service ? t("catalog.serviceTitle") : t("catalog.newServiceTitle")}</DialogTitle>
        </DialogHeader>
        <form
          className="grid gap-3 sm:grid-cols-3"
          onSubmit={(e) => {
            e.preventDefault();
            if (valid) save.mutate();
          }}
        >
          <Field label={t("catalog.code")}>
            <Input autoFocus value={f.code} onChange={(e) => setF({ ...f, code: e.target.value })} />
          </Field>
          <Field label={t("catalog.name")} className="sm:col-span-2">
            <Input value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} />
          </Field>
          <Field label={t("catalog.category")} className="sm:col-span-2">
            <NativeSelect value={f.categoryId} onChange={(e) => setF({ ...f, categoryId: e.target.value })}>
              {categories.map(({ cat, depth }) => (
                <option key={cat.id} value={cat.id}>
                  {"  ".repeat(depth)}
                  {cat.name}
                </option>
              ))}
            </NativeSelect>
          </Field>
          <Field label={t("catalog.duration")}>
            <Input type="number" min={5} max={600} step={5} value={f.durationMin} onChange={(e) => setF({ ...f, durationMin: e.target.value })} />
          </Field>
          <label className="flex items-center gap-2 text-sm sm:col-span-3">
            <Switch checked={f.isActive} onCheckedChange={(v) => setF({ ...f, isActive: v })} />
            {t("catalog.isActive")}
          </label>
          <DialogFooter className="sm:col-span-3">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" disabled={!valid} loading={save.isPending}>
              {service ? t("common.save") : t("common.create")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function CategoryDialog({
  open,
  onOpenChange,
  category,
  categories,
}: {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  category: Category | null;
  categories: { cat: Category; depth: number }[];
}) {
  const qc = useQueryClient();
  const [name, setName] = React.useState("");
  const [parentId, setParentId] = React.useState("");
  const [sort, setSort] = React.useState("0");
  React.useEffect(() => {
    if (!open) return;
    setName(category?.name ?? "");
    setParentId(category?.parentId ?? "");
    setSort(String(category?.sort ?? categories.length));
  }, [open, category, categories.length]);

  const save = useMutation({
    mutationFn: () => {
      const body: Schemas["ServiceCategoryRequest"] = { name: name.trim(), parentId: parentId || null, sort: Number(sort) || 0 };
      return category ? api(`/service-categories/${category.id}`, { method: "PATCH", body }) : api("/service-categories", { method: "POST", body });
    },
    onSuccess: () => {
      toast.success(t("catalog.categorySaved"));
      void qc.invalidateQueries({ queryKey: ["service-categories"] });
      onOpenChange(false);
    },
    onError: toastError,
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("catalog.categoryTitle")}</DialogTitle>
        </DialogHeader>
        <form
          className="grid gap-3 sm:grid-cols-3"
          onSubmit={(e) => {
            e.preventDefault();
            if (name.trim()) save.mutate();
          }}
        >
          <Field label={t("catalog.categoryName")} className="sm:col-span-3">
            <Input autoFocus value={name} onChange={(e) => setName(e.target.value)} />
          </Field>
          <Field label={t("catalog.parentCategory")} className="sm:col-span-2">
            <NativeSelect value={parentId} onChange={(e) => setParentId(e.target.value)}>
              <option value="">{t("catalog.noParent")}</option>
              {categories
                .filter(({ cat }) => cat.id !== category?.id)
                .map(({ cat, depth }) => (
                  <option key={cat.id} value={cat.id}>
                    {"  ".repeat(depth)}
                    {cat.name}
                  </option>
                ))}
            </NativeSelect>
          </Field>
          <Field label={t("catalog.sort")}>
            <Input type="number" value={sort} onChange={(e) => setSort(e.target.value)} />
          </Field>
          <DialogFooter className="sm:col-span-3">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" disabled={!name.trim()} loading={save.isPending}>
              {t("common.save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

type TechLine = { itemId: string; itemName: string; baseUnit: string; quantity: string };

function TechCardDialog({ service, canEdit, onOpenChange }: { service: ServiceItem | null; canEdit: boolean; onOpenChange: (o: boolean) => void }) {
  const qc = useQueryClient();
  const id = service?.id;
  const cards = useQuery({ queryKey: ["tech-cards", id], queryFn: () => api<Schemas["TechCardDto"][]>(`/services/${id}/tech-cards`), enabled: !!id });
  const current = React.useMemo(() => {
    const list = cards.data ?? [];
    return list.find((c) => c.isActive) ?? [...list].sort((a, b) => b.version - a.version)[0] ?? null;
  }, [cards.data]);
  const [lines, setLines] = React.useState<TechLine[]>([]);
  const [q, setQ] = React.useState("");
  const dq = useDebounced(q.trim());
  const items = useQuery({
    queryKey: ["items", "search", dq],
    queryFn: () => api<Schemas["PagedResultOfItemDto"]>("/items", { query: { q: dq, page_size: 10 } }),
    enabled: canEdit && dq.length >= 2,
  });

  React.useEffect(() => {
    setLines((current?.items ?? []).map((i) => ({ itemId: i.itemId, itemName: i.itemName, baseUnit: i.baseUnit, quantity: String(i.quantity) })));
    setQ("");
  }, [current, id]);

  const save = useMutation({
    mutationFn: () =>
      api(`/services/${id}/tech-cards`, {
        method: "POST",
        body: { items: lines.map((l) => ({ itemId: l.itemId, quantity: Number(l.quantity.replace(",", ".")) })) } satisfies Schemas["TechCardRequest"],
      }),
    onSuccess: () => {
      toast.success(t("catalog.techCardSaved"));
      void qc.invalidateQueries({ queryKey: ["tech-cards", id] });
    },
    onError: toastError,
  });

  const unit = (u: string) => {
    const k = `catalog.units.${u}`;
    return t(k) === k ? u : t(k);
  };
  const valid = lines.every((l) => Number(l.quantity.replace(",", ".")) > 0);
  const dirty =
    JSON.stringify(lines.map((l) => [l.itemId, Number(l.quantity.replace(",", "."))])) !==
    JSON.stringify((current?.items ?? []).map((i) => [i.itemId, i.quantity]));

  return (
    <Dialog open={!!service} onOpenChange={onOpenChange}>
      <DialogContent wide>
        <DialogHeader>
          <DialogTitle>{t("catalog.techCardTitle", { name: service?.name ?? "" })}</DialogTitle>
          <DialogDescription>
            {t("catalog.techCardHint")}
            {current ? ` · ${t("catalog.version", { version: current.version })}, ${formatDateTime(current.createdAt)}` : ""}
          </DialogDescription>
        </DialogHeader>
        {cards.isLoading ? (
          <TableSkeleton rows={3} cols={3} />
        ) : cards.isError ? (
          <ErrorBlock onRetry={() => cards.refetch()} />
        ) : (
          <div className="space-y-3">
            {lines.length === 0 ? (
              <p className="rounded-md border border-dashed p-4 text-center text-sm text-muted-foreground">{t("catalog.noMaterials")}</p>
            ) : (
              <div className="rounded-md border">
                <Table>
                  <THead>
                    <TR>
                      <TH>{t("catalog.material")}</TH>
                      <TH className="w-32 text-right">{t("catalog.quantity")}</TH>
                      <TH className="w-12">{t("catalog.unit")}</TH>
                      {canEdit ? <TH className="w-10" /> : null}
                    </TR>
                  </THead>
                  <TBody>
                    {lines.map((l, i) => (
                      <TR key={l.itemId}>
                        <TD>{l.itemName}</TD>
                        <TD className="text-right">
                          {canEdit ? (
                            <Input
                              inputMode="decimal"
                              className="h-8 text-right"
                              value={l.quantity}
                              onChange={(e) => setLines((p) => p.map((x, j) => (j === i ? { ...x, quantity: e.target.value.replace(/[^\d.,]/g, "") } : x)))}
                            />
                          ) : (
                            formatNumber(Number(l.quantity))
                          )}
                        </TD>
                        <TD className="text-muted-foreground">{unit(l.baseUnit)}</TD>
                        {canEdit ? (
                          <TD>
                            <Button size="icon-sm" variant="ghost" className="text-destructive" onClick={() => setLines((p) => p.filter((_, j) => j !== i))}>
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
            {canEdit ? (
              <div className="relative">
                <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
                <Input className="pl-8" placeholder={t("catalog.addMaterial")} value={q} onChange={(e) => setQ(e.target.value)} />
                {dq.length >= 2 ? (
                  <div className="absolute z-10 mt-1 max-h-60 w-full overflow-y-auto rounded-md border bg-popover shadow-md">
                    {items.isLoading ? (
                      <div className="p-2 text-sm text-muted-foreground">{t("common.loading")}</div>
                    ) : (items.data?.items ?? []).length === 0 ? (
                      <div className="p-2 text-sm text-muted-foreground">{t("catalog.nothingFound")}</div>
                    ) : (
                      items.data!.items.map((it) => {
                        const added = lines.some((l) => l.itemId === it.id);
                        return (
                          <button
                            key={it.id}
                            type="button"
                            disabled={added}
                            className="flex w-full items-center justify-between gap-2 px-3 py-1.5 text-left text-sm hover:bg-accent disabled:opacity-50"
                            onClick={() => {
                              setLines((p) => [...p, { itemId: it.id, itemName: it.name, baseUnit: it.baseUnit, quantity: "1" }]);
                              setQ("");
                            }}
                          >
                            <span>{it.name}</span>
                            <span className="font-mono text-xs text-muted-foreground">
                              {it.sku} · {unit(it.baseUnit)}
                            </span>
                          </button>
                        );
                      })
                    )}
                  </div>
                ) : null}
              </div>
            ) : null}
            {(cards.data ?? []).length > 1 ? (
              <p className="text-xs text-muted-foreground">
                {t("catalog.versions")}:{" "}
                {[...cards.data!]
                  .sort((a, b) => b.version - a.version)
                  .map((c) => `v${c.version} (${formatDateTime(c.createdAt)})${c.isActive ? ` — ${t("catalog.current").toLowerCase()}` : ""}`)
                  .join("; ")}
              </p>
            ) : null}
          </div>
        )}
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t("common.close")}
          </Button>
          {canEdit ? (
            <Button disabled={!dirty || !valid} loading={save.isPending} onClick={() => save.mutate()}>
              {t("common.save")}
            </Button>
          ) : null}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
