"use client";

import * as React from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { AlertTriangle, ChevronLeft, ChevronRight, Gauge, History, Pencil, Plus, Search } from "lucide-react";
import { EmptyState, PageHeader } from "@/components/ui/empty-state";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect } from "@/components/ui/input";
import { Card } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Checkbox } from "@/components/ui/checkbox";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { TableSkeleton } from "@/components/ui/skeleton";
import { ItemFormDialog, StockLevelsDialog } from "@/components/inventory/item-form-dialog";
import { ItemCardDialog } from "@/components/inventory/item-card-dialog";
import { CategoriesPanel, SuppliersPanel } from "@/components/inventory/catalog-panels";
import { categoryOptions, unitLabel, useDebounced, useItemCategories, type Item } from "@/components/inventory/shared";
import { api, ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import type { Schemas } from "@/lib/types";

const PAGE_SIZE = 50;

export default function ItemsPage() {
  const { can } = useAuth();
  const canManage = can(P.itemsManage);
  const [tab, setTab] = React.useState("items");
  const [formOpen, setFormOpen] = React.useState(false);
  const [editing, setEditing] = React.useState<Item | null>(null);

  return (
    <div>
      <PageHeader
        title={t("inventory.items.title")}
        description={t("inventory.items.description")}
        actions={
          canManage && tab === "items" ? (
            <Button
              onClick={() => {
                setEditing(null);
                setFormOpen(true);
              }}
              data-testid="new-item"
            >
              <Plus /> {t("inventory.items.new")}
            </Button>
          ) : null
        }
      />
      <Tabs value={tab} onValueChange={setTab}>
        <TabsList>
          <TabsTrigger value="items">{t("inventory.items.tabItems")}</TabsTrigger>
          <TabsTrigger value="categories">{t("inventory.items.tabCategories")}</TabsTrigger>
          <TabsTrigger value="suppliers">{t("inventory.items.tabSuppliers")}</TabsTrigger>
        </TabsList>
        <TabsContent value="items">
          <ItemsTab
            canManage={canManage}
            canView={can(P.inventoryView)}
            onCreate={() => {
              setEditing(null);
              setFormOpen(true);
            }}
            onEdit={(it) => {
              setEditing(it);
              setFormOpen(true);
            }}
          />
        </TabsContent>
        <TabsContent value="categories">
          <CategoriesPanel canManage={canManage} />
        </TabsContent>
        <TabsContent value="suppliers">
          <SuppliersPanel canManage={canManage || can(P.purchaseOrder)} />
        </TabsContent>
      </Tabs>
      <ItemFormDialog open={formOpen} onOpenChange={setFormOpen} item={editing} />
    </div>
  );
}

function ItemsTab({ canManage, canView, onCreate, onEdit }: { canManage: boolean; canView: boolean; onCreate: () => void; onEdit: (it: Item) => void }) {
  const categories = useItemCategories();
  const [q, setQ] = React.useState("");
  const [categoryId, setCategoryId] = React.useState("");
  const [inactive, setInactive] = React.useState(false);
  const [page, setPage] = React.useState(1);
  const [levelsItem, setLevelsItem] = React.useState<Item | null>(null);
  const [cardItem, setCardItem] = React.useState<string | null>(null);
  const search = useDebounced(q.trim());
  React.useEffect(() => setPage(1), [search, categoryId, inactive]);

  const query = useQuery({
    queryKey: ["items", "list", search, categoryId, inactive, page],
    queryFn: () =>
      api<Schemas["PagedResultOfItemDto"]>("/items", {
        query: { q: search || undefined, category_id: categoryId || undefined, include_inactive: inactive || undefined, page, page_size: PAGE_SIZE },
      }),
    placeholderData: keepPreviousData,
  });
  const data = query.data;
  const rows = data?.items ?? [];
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;
  const catName = new Map((categories.data ?? []).map((c) => [c.id, c.name]));
  const filtered = !!(search || categoryId);

  return (
    <>
      <Card className="mb-3 flex flex-wrap items-center gap-3 p-3">
        <div className="relative min-w-60 flex-1">
          <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input className="pl-8" placeholder={t("inventory.items.searchPlaceholder")} value={q} onChange={(e) => setQ(e.target.value)} />
        </div>
        <NativeSelect className="w-56" value={categoryId} onChange={(e) => setCategoryId(e.target.value)} aria-label={t("inventory.items.category")}>
          <option value="">{t("inventory.items.allCategories")}</option>
          {categoryOptions(categories.data).map((c) => (
            <option key={c.id} value={c.id}>
              {c.label}
            </option>
          ))}
        </NativeSelect>
        <label className="flex items-center gap-2 text-sm">
          <Checkbox checked={inactive} onCheckedChange={(v) => setInactive(v === true)} />
          {t("inventory.items.showInactive")}
        </label>
      </Card>

      <Card>
        {query.isLoading ? (
          <TableSkeleton cols={6} />
        ) : query.isError ? (
          <EmptyState
            className="m-4"
            icon={<AlertTriangle className="h-8 w-8" />}
            title={query.error instanceof ApiError ? query.error.userMessage : t("errors.INTERNAL_ERROR")}
            action={<Button variant="outline" onClick={() => query.refetch()}>{t("common.retry")}</Button>}
          />
        ) : rows.length === 0 ? (
          <EmptyState
            className="m-4"
            title={filtered ? t("inventory.balances.emptyFiltered") : t("inventory.items.empty")}
            description={filtered ? undefined : t("inventory.items.emptyHint")}
            action={!filtered && canManage ? <Button onClick={onCreate}>{t("inventory.items.new")}</Button> : null}
          />
        ) : (
          <Table>
            <THead>
              <TR>
                <TH>{t("inventory.items.name")}</TH>
                <TH>{t("inventory.items.sku")}</TH>
                <TH>{t("inventory.items.category")}</TH>
                <TH>{t("inventory.items.baseUnit")}</TH>
                <TH>{t("inventory.items.tracking")}</TH>
                <TH className="w-28 text-right">{t("common.actions")}</TH>
              </TR>
            </THead>
            <TBody className={query.isFetching ? "opacity-60" : ""}>
              {rows.map((it) => (
                <TR key={it.id} className={canManage ? "cursor-pointer" : ""} onClick={() => canManage && onEdit(it)}>
                  <TD>
                    <div className="flex items-center gap-1.5 font-medium">
                      {it.name}
                      {!it.isActive ? <Badge variant="muted">{t("common.inactive")}</Badge> : null}
                    </div>
                    {it.manufacturer ? <div className="text-xs text-muted-foreground">{it.manufacturer}</div> : null}
                  </TD>
                  <TD className="tabular whitespace-nowrap">{it.sku}</TD>
                  <TD className="text-muted-foreground">{it.categoryId ? (catName.get(it.categoryId) ?? "—") : "—"}</TD>
                  <TD>
                    {unitLabel(it.baseUnit)}
                    {it.units.length ? <span className="text-xs text-muted-foreground"> +{it.units.map((u) => u.unitName).join(", ")}</span> : null}
                  </TD>
                  <TD>
                    <div className="flex flex-wrap gap-1">
                      {it.trackBatches ? <Badge variant="secondary">{t("inventory.items.trackBatches")}</Badge> : null}
                      {it.trackExpiry ? <Badge variant="secondary">{t("inventory.items.trackExpiry")}</Badge> : null}
                      {it.trackSerials ? <Badge variant="secondary">{t("inventory.items.trackSerials")}</Badge> : null}
                    </div>
                  </TD>
                  <TD className="text-right" onClick={(e) => e.stopPropagation()}>
                    <div className="flex justify-end gap-1">
                      {canView ? (
                        <Button variant="ghost" size="icon-sm" title={t("inventory.items.card")} aria-label={t("inventory.items.card")} onClick={() => setCardItem(it.id)}>
                          <History />
                        </Button>
                      ) : null}
                      {canManage ? (
                        <>
                          <Button variant="ghost" size="icon-sm" title={t("inventory.items.stockLevels")} aria-label={t("inventory.items.stockLevels")} onClick={() => setLevelsItem(it)}>
                            <Gauge />
                          </Button>
                          <Button variant="ghost" size="icon-sm" title={t("common.edit")} aria-label={t("common.edit")} onClick={() => onEdit(it)}>
                            <Pencil />
                          </Button>
                        </>
                      ) : null}
                    </div>
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
        )}
        {data && data.total > 0 ? (
          <div className="flex items-center justify-between gap-2 border-t p-3 text-sm">
            <span className="text-muted-foreground">{t("inventory.items.total", { n: data.total })}</span>
            {data.total > data.pageSize ? (
              <div className="flex items-center gap-2">
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
          </div>
        ) : null}
      </Card>
      <StockLevelsDialog item={levelsItem} onOpenChange={(o) => !o && setLevelsItem(null)} />
      <ItemCardDialog itemId={cardItem} onOpenChange={(o) => !o && setCardItem(null)} />
    </>
  );
}
