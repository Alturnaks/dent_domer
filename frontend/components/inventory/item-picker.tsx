"use client";

import * as React from "react";
import { useQuery } from "@tanstack/react-query";
import { Loader2, Search } from "lucide-react";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api-client";
import { t } from "@/lib/i18n";
import { cn } from "@/lib/utils";
import type { Schemas } from "@/lib/types";
import { type Item, unitLabel, useDebounced } from "./shared";

/** Поиск товара с выпадающим списком (клавиатура: ↑ ↓ Enter Esc). */
export function ItemPicker({ onSelect, className, autoFocus }: { onSelect: (item: Item) => void; className?: string; autoFocus?: boolean }) {
  const [q, setQ] = React.useState("");
  const [open, setOpen] = React.useState(false);
  const [active, setActive] = React.useState(0);
  const debounced = useDebounced(q.trim(), 250);
  const ref = React.useRef<HTMLDivElement>(null);

  const query = useQuery({
    queryKey: ["items", "picker", debounced],
    queryFn: () => api<Schemas["PagedResultOfItemDto"]>("/items", { query: { q: debounced, page_size: 20 } }),
    enabled: open,
    staleTime: 30_000,
  });
  const items = query.data?.items ?? [];

  React.useEffect(() => {
    const onDoc = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", onDoc);
    return () => document.removeEventListener("mousedown", onDoc);
  }, []);

  React.useEffect(() => setActive(0), [debounced]);

  const pick = (item: Item) => {
    onSelect(item);
    setQ("");
    setOpen(false);
  };

  return (
    <div ref={ref} className={cn("relative", className)}>
      <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
      <Input
        className="pl-8"
        value={q}
        autoFocus={autoFocus}
        placeholder={t("inventory.picker.placeholder")}
        onFocus={() => setOpen(true)}
        onChange={(e) => {
          setQ(e.target.value);
          setOpen(true);
        }}
        onKeyDown={(e) => {
          if (e.key === "ArrowDown") {
            e.preventDefault();
            setActive((a) => Math.min(a + 1, items.length - 1));
          } else if (e.key === "ArrowUp") {
            e.preventDefault();
            setActive((a) => Math.max(a - 1, 0));
          } else if (e.key === "Enter") {
            e.preventDefault();
            if (items[active]) pick(items[active]);
          } else if (e.key === "Escape") setOpen(false);
        }}
      />
      {open ? (
        <div className="absolute z-40 mt-1 max-h-72 w-full overflow-y-auto rounded-md border bg-popover p-1 text-sm shadow-lg">
          {query.isLoading ? (
            <div className="flex items-center gap-2 p-2 text-muted-foreground">
              <Loader2 className="h-4 w-4 animate-spin" /> {t("common.loading")}
            </div>
          ) : items.length === 0 ? (
            <div className="p-2 text-muted-foreground">{t("inventory.picker.nothing")}</div>
          ) : (
            items.map((it, i) => (
              <button
                type="button"
                key={it.id}
                onMouseEnter={() => setActive(i)}
                onClick={() => pick(it)}
                className={cn("flex w-full items-center justify-between gap-3 rounded-sm px-2 py-1.5 text-left", i === active && "bg-accent")}
              >
                <span className="min-w-0 truncate">
                  {it.name}
                  {!it.isActive ? <span className="ml-1 text-xs text-muted-foreground">({t("inventory.picker.inactive")})</span> : null}
                </span>
                <span className="shrink-0 text-xs text-muted-foreground">
                  {it.sku} · {unitLabel(it.baseUnit)}
                </span>
              </button>
            ))
          )}
        </div>
      ) : null}
    </div>
  );
}
