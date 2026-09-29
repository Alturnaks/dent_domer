"use client";

import * as React from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Pencil, Percent, Plus, Search } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Checkbox, Switch } from "@/components/ui/checkbox";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { TableSkeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { useConfirm } from "@/components/ui/confirm";
import { ErrorBlock, MoneyInput, minorToInput, tengeToMinorOrNull, toastError } from "@/components/admin/common";
import { flattenCategories } from "@/components/admin/catalog-services";
import { api } from "@/lib/api-client";
import { useAuth } from "@/lib/auth";
import { useBranches, useServiceCategories } from "@/lib/queries";
import { formatDate, formatMoney, isoDate } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import { cn } from "@/lib/utils";
import type { Schemas, ServiceItem } from "@/lib/types";

type PriceList = Schemas["PriceListDto"];
type PriceItem = Schemas["PriceListItemDto"];

export function PriceListsTab() {
  const { can } = useAuth();
  const canEdit = can(P.pricesManage);
  const confirm = useConfirm();
  const branches = useBranches();
  const lists = useQuery({ queryKey: ["price-lists"], queryFn: () => api<PriceList[]>("/price-lists") });
  const [selectedId, setSelectedId] = React.useState<string | null>(null);
  const [formOpen, setFormOpen] = React.useState(false);
  const [editing, setEditing] = React.useState<PriceList | null>(null);
  const dirtyRef = React.useRef(false);
  const branchName = React.useMemo(() => new Map((branches.data ?? []).map((b) => [b.id, b.name])), [branches.data]);

  React.useEffect(() => {
    if (!selectedId && lists.data?.length) setSelectedId(lists.data[0].id);
  }, [lists.data, selectedId]);

  const select = async (id: string) => {
    if (id === selectedId) return;
    if (dirtyRef.current && !(await confirm({ title: t("catalog.priceLists.discardTitle"), destructive: true }))) return;
    setSelectedId(id);
  };

  const selected = lists.data?.find((l) => l.id === selectedId) ?? null;

  return (
    <div className="grid gap-4 lg:grid-cols-[300px_1fr]">
      <Card className="h-fit">
        <CardHeader className="flex-row items-center justify-between space-y-0">
          <CardTitle>{t("catalog.tabs.priceLists")}</CardTitle>
          {canEdit ? (
            <Button
              size="sm"
              variant="outline"
              onClick={() => {
                setEditing(null);
                setFormOpen(true);
              }}
            >
              <Plus /> {t("common.add")}
            </Button>
          ) : null}
        </CardHeader>
        <CardContent className="p-2">
          {lists.isLoading ? (
            <TableSkeleton rows={3} cols={1} />
          ) : lists.isError ? (
            <ErrorBlock onRetry={() => lists.refetch()} />
          ) : (lists.data ?? []).length === 0 ? (
            <p className="p-3 text-sm text-muted-foreground">{t("catalog.priceLists.empty")}</p>
          ) : (
            <ul className="space-y-0.5">
              {lists.data!.map((l) => (
                <li key={l.id} className="group flex items-center">
                  <button
                    type="button"
                    onClick={() => void select(l.id)}
                    className={cn("flex-1 rounded-md px-2.5 py-2 text-left text-sm hover:bg-muted", l.id === selectedId && "bg-muted")}
                  >
                    <div className="flex items-center gap-2 font-medium">
                      {l.name}
                      {!l.isActive ? <Badge variant="muted">{t("admin.inactive")}</Badge> : null}
                    </div>
                    <div className="text-xs text-muted-foreground">
                      {l.branchId ? branchName.get(l.branchId) ?? "—" : t("catalog.priceLists.network")} · {t("catalog.priceLists.validFrom")} {formatDate(l.validFrom)} ·{" "}
                      {t("catalog.priceLists.items", { count: l.itemsCount })}
                    </div>
                  </button>
                  {canEdit ? (
                    <Button
                      size="icon-sm"
                      variant="ghost"
                      className="opacity-0 group-hover:opacity-100"
                      title={t("common.edit")}
                      onClick={() => {
                        setEditing(l);
                        setFormOpen(true);
                      }}
                    >
                      <Pencil />
                    </Button>
                  ) : null}
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>

      {selected ? (
        <PriceItemsEditor key={selected.id} list={selected} canEdit={canEdit} onDirtyChange={(d) => (dirtyRef.current = d)} />
      ) : (
        <Card className="p-10 text-center text-sm text-muted-foreground">{t("catalog.priceLists.select")}</Card>
      )}

      <PriceListDialog
        open={formOpen}
        onOpenChange={setFormOpen}
        list={editing}
        lists={lists.data ?? []}
        onSaved={(l) => {
          dirtyRef.current = false;
          setSelectedId(l.id);
        }}
      />
    </div>
  );
}

function PriceItemsEditor({ list, canEdit, onDirtyChange }: { list: PriceList; canEdit: boolean; onDirtyChange: (d: boolean) => void }) {
  const qc = useQueryClient();
  const cats = useServiceCategories();
  const items = useQuery({ queryKey: ["price-list-items", list.id], queryFn: () => api<PriceItem[]>(`/price-lists/${list.id}/items`) });
  const services = useQuery({
    queryKey: ["services", "catalog", "all-with-inactive"],
    queryFn: () => api<ServiceItem[]>("/services", { query: { include_inactive: true } }),
    enabled: canEdit,
    staleTime: 60_000,
  });
  const [edits, setEdits] = React.useState<Record<string, string>>({});
  const [q, setQ] = React.useState("");
  const [cat, setCat] = React.useState("");
  const [onlyInList, setOnlyInList] = React.useState(!canEdit);
  const [bulkOpen, setBulkOpen] = React.useState(false);

  const flat = React.useMemo(() => flattenCategories(cats.data ?? []), [cats.data]);
  const catName = React.useMemo(() => new Map((cats.data ?? []).map((c) => [c.id, c.name])), [cats.data]);
  const priceById = React.useMemo(() => new Map((items.data ?? []).map((i) => [i.serviceId, i.price])), [items.data]);

  // Строки: позиции прайса + (для редактора) услуги, которых нет в прайсе.
  const rows = React.useMemo(() => {
    const base: { serviceId: string; code: string; name: string; categoryId: string; price: number | null }[] = (items.data ?? []).map((i) => ({
      serviceId: i.serviceId,
      code: i.serviceCode,
      name: i.serviceName,
      categoryId: i.categoryId,
      price: i.price,
    }));
    if (canEdit && !onlyInList)
      for (const s of services.data ?? []) if (!priceById.has(s.id)) base.push({ serviceId: s.id, code: s.code, name: s.name, categoryId: s.categoryId, price: null });
    const needle = q.trim().toLowerCase();
    let catIds: Set<string> | null = null;
    if (cat) {
      catIds = new Set([cat]);
      let grew = true;
      while (grew) {
        grew = false;
        for (const c of cats.data ?? [])
          if (c.parentId && catIds.has(c.parentId) && !catIds.has(c.id)) {
            catIds.add(c.id);
            grew = true;
          }
      }
    }
    return base
      .filter((r) => (!catIds || catIds.has(r.categoryId)) && (!needle || r.name.toLowerCase().includes(needle) || r.code.toLowerCase().includes(needle)))
      .sort((a, b) => a.code.localeCompare(b.code, "ru", { numeric: true }));
  }, [items.data, services.data, priceById, canEdit, onlyInList, q, cat, cats.data]);

  const changed = React.useMemo(
    () =>
      Object.entries(edits)
        .map(([serviceId, v]) => ({ serviceId, price: tengeToMinorOrNull(v) }))
        .filter((x): x is { serviceId: string; price: number } => x.price !== null && x.price >= 0 && x.price !== priceById.get(x.serviceId)),
    [edits, priceById],
  );
  React.useEffect(() => onDirtyChange(changed.length > 0), [changed.length, onDirtyChange]);

  const invalidate = () => {
    void qc.invalidateQueries({ queryKey: ["price-list-items", list.id] });
    void qc.invalidateQueries({ queryKey: ["price-lists"] });
    void qc.invalidateQueries({ queryKey: ["services"] });
  };

  const save = useMutation({
    mutationFn: () => api<PriceItem[]>(`/price-lists/${list.id}/items`, { method: "PUT", body: changed satisfies Schemas["PriceListItemInput"][] }),
    onSuccess: () => {
      toast.success(t("catalog.priceLists.pricesSaved", { count: changed.length }));
      setEdits({});
      invalidate();
    },
    onError: toastError,
  });

  return (
    <Card>
      <div className="flex flex-wrap items-center gap-2 border-b p-3">
        <div className="relative min-w-48 flex-1">
          <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input className="pl-8" placeholder={t("admin.searchPlaceholder")} value={q} onChange={(e) => setQ(e.target.value)} />
        </div>
        <NativeSelect className="w-48" value={cat} onChange={(e) => setCat(e.target.value)} aria-label={t("catalog.category")}>
          <option value="">{t("catalog.allCategories")}</option>
          {flat.map(({ cat: c, depth }) => (
            <option key={c.id} value={c.id}>
              {"  ".repeat(depth)}
              {c.name}
            </option>
          ))}
        </NativeSelect>
        {canEdit ? (
          <>
            <label className="flex items-center gap-2 text-sm">
              <Checkbox checked={onlyInList} onCheckedChange={(v) => setOnlyInList(v === true)} />
              {t("catalog.priceLists.onlyInList")}
            </label>
            <Button variant="outline" onClick={() => setBulkOpen(true)}>
              <Percent /> {t("catalog.priceLists.bulk")}
            </Button>
            <Button disabled={changed.length === 0} loading={save.isPending} onClick={() => save.mutate()}>
              {t("common.save")}
              {changed.length ? ` (${changed.length})` : ""}
            </Button>
          </>
        ) : null}
      </div>
      {items.isLoading ? (
        <TableSkeleton />
      ) : items.isError ? (
        <ErrorBlock className="m-4" onRetry={() => items.refetch()} />
      ) : rows.length === 0 ? (
        <EmptyState className="m-4" title={t("catalog.servicesEmpty")} />
      ) : (
        <div className="max-h-[65vh] overflow-y-auto">
          <Table>
            <THead className="sticky top-0 z-[1] bg-card">
              <TR>
                <TH className="w-20">{t("catalog.code")}</TH>
                <TH>{t("catalog.name")}</TH>
                <TH>{t("catalog.category")}</TH>
                <TH className="w-44 text-right">{t("catalog.price")}</TH>
              </TR>
            </THead>
            <TBody>
              {rows.map((r) => {
                const value = edits[r.serviceId] ?? minorToInput(r.price);
                const isChanged = changed.some((c) => c.serviceId === r.serviceId);
                return (
                  <TR key={r.serviceId} className={isChanged ? "bg-warning/10" : undefined}>
                    <TD className="font-mono text-xs">{r.code}</TD>
                    <TD>
                      {r.name}
                      {r.price === null ? <span className="ml-2 text-xs text-muted-foreground">({t("catalog.priceLists.notInList")})</span> : null}
                    </TD>
                    <TD className="text-muted-foreground">{catName.get(r.categoryId) ?? "—"}</TD>
                    <TD className="py-1 text-right">
                      {canEdit ? (
                        <MoneyInput value={value} onChange={(v) => setEdits((p) => ({ ...p, [r.serviceId]: v }))} placeholder="—" />
                      ) : (
                        <span className="tabular-nums">{formatMoney(r.price)}</span>
                      )}
                    </TD>
                  </TR>
                );
              })}
            </TBody>
          </Table>
        </div>
      )}
      <BulkDialog open={bulkOpen} onOpenChange={setBulkOpen} list={list} categories={flat} onDone={invalidate} />
    </Card>
  );
}

function BulkDialog({
  open,
  onOpenChange,
  list,
  categories,
  onDone,
}: {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  list: PriceList;
  categories: ReturnType<typeof flattenCategories>;
  onDone: () => void;
}) {
  const [percent, setPercent] = React.useState("");
  const [categoryId, setCategoryId] = React.useState("");
  const [roundTo, setRoundTo] = React.useState("100");
  React.useEffect(() => {
    if (open) {
      setPercent("");
      setCategoryId("");
      setRoundTo("100");
    }
  }, [open]);
  const pct = Number(percent.replace(",", "."));
  const valid = percent.trim() !== "" && Number.isFinite(pct) && pct !== 0 && pct >= -90 && pct <= 500;

  const run = useMutation({
    mutationFn: () =>
      api<Schemas["BulkPriceUpdateResult"]>(`/price-lists/${list.id}/bulk-update`, {
        method: "POST",
        body: { percent: pct, categoryId: categoryId || null, roundTo: tengeToMinorOrNull(roundTo) || null } satisfies Schemas["BulkPriceUpdateRequest"],
      }),
    onSuccess: (r) => {
      toast.success(t("catalog.priceLists.bulkDone", { count: r.updated }));
      onDone();
      onOpenChange(false);
    },
    onError: toastError,
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>
            {t("catalog.priceLists.bulkTitle")}: {list.name}
          </DialogTitle>
          <DialogDescription>{t("catalog.priceLists.bulkHint")}</DialogDescription>
        </DialogHeader>
        <form
          className="grid gap-3 sm:grid-cols-2"
          onSubmit={(e) => {
            e.preventDefault();
            if (valid) run.mutate();
          }}
        >
          <Field label={t("catalog.priceLists.percent")}>
            <Input autoFocus inputMode="decimal" value={percent} onChange={(e) => setPercent(e.target.value.replace(/[^\d.,-]/g, ""))} />
          </Field>
          <Field label={t("catalog.priceLists.roundTo")}>
            <MoneyInput value={roundTo} onChange={setRoundTo} />
          </Field>
          <Field label={t("catalog.category")} className="sm:col-span-2">
            <NativeSelect value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
              <option value="">{t("catalog.allCategories")}</option>
              {categories.map(({ cat, depth }) => (
                <option key={cat.id} value={cat.id}>
                  {"  ".repeat(depth)}
                  {cat.name}
                </option>
              ))}
            </NativeSelect>
          </Field>
          <DialogFooter className="sm:col-span-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" disabled={!valid} loading={run.isPending}>
              {t("common.apply")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function PriceListDialog({
  open,
  onOpenChange,
  list,
  lists,
  onSaved,
}: {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  list: PriceList | null;
  lists: PriceList[];
  onSaved: (l: PriceList) => void;
}) {
  const qc = useQueryClient();
  const branches = useBranches();
  const [f, setF] = React.useState({ name: "", branchId: "", validFrom: isoDate(new Date()), isActive: true, copyFromId: "" });
  React.useEffect(() => {
    if (!open) return;
    setF({
      name: list?.name ?? "",
      branchId: list?.branchId ?? "",
      validFrom: list?.validFrom ?? isoDate(new Date()),
      isActive: list?.isActive ?? true,
      copyFromId: "",
    });
  }, [open, list]);

  const save = useMutation({
    mutationFn: () => {
      const body: Schemas["PriceListRequest"] = {
        name: f.name.trim(),
        branchId: f.branchId || null,
        validFrom: f.validFrom,
        isActive: f.isActive,
        copyFromId: list ? null : f.copyFromId || null,
      };
      return list ? api<PriceList>(`/price-lists/${list.id}`, { method: "PATCH", body }) : api<PriceList>("/price-lists", { method: "POST", body });
    },
    onSuccess: (l) => {
      toast.success(list ? t("catalog.priceLists.savedToast") : t("catalog.priceLists.createdToast"));
      void qc.invalidateQueries({ queryKey: ["price-lists"] });
      void qc.invalidateQueries({ queryKey: ["price-list-items"] });
      void qc.invalidateQueries({ queryKey: ["services"] });
      onSaved(l);
      onOpenChange(false);
    },
    onError: toastError,
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{list ? t("catalog.priceLists.title") : t("catalog.priceLists.add")}</DialogTitle>
        </DialogHeader>
        <form
          className="grid gap-3 sm:grid-cols-2"
          onSubmit={(e) => {
            e.preventDefault();
            if (f.name.trim() && f.validFrom) save.mutate();
          }}
        >
          <Field label={t("catalog.priceLists.name")} className="sm:col-span-2">
            <Input autoFocus value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} />
          </Field>
          <Field label={t("catalog.priceLists.branch")}>
            <NativeSelect value={f.branchId} onChange={(e) => setF({ ...f, branchId: e.target.value })}>
              <option value="">{t("catalog.priceLists.network")}</option>
              {branches.data?.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.name}
                </option>
              ))}
            </NativeSelect>
          </Field>
          <Field label={t("catalog.priceLists.validFrom")}>
            <Input type="date" value={f.validFrom} onChange={(e) => setF({ ...f, validFrom: e.target.value })} />
          </Field>
          {!list ? (
            <Field label={t("catalog.priceLists.copyFrom")} className="sm:col-span-2">
              <NativeSelect value={f.copyFromId} onChange={(e) => setF({ ...f, copyFromId: e.target.value })}>
                <option value="">{t("catalog.priceLists.noCopy")}</option>
                {lists.map((l) => (
                  <option key={l.id} value={l.id}>
                    {l.name}
                  </option>
                ))}
              </NativeSelect>
            </Field>
          ) : null}
          <label className="flex items-center gap-2 text-sm sm:col-span-2">
            <Switch checked={f.isActive} onCheckedChange={(v) => setF({ ...f, isActive: v })} />
            {t("catalog.priceLists.isActive")}
          </label>
          <DialogFooter className="sm:col-span-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" disabled={!f.name.trim() || !f.validFrom} loading={save.isPending}>
              {list ? t("common.save") : t("common.create")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
