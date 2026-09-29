"use client";

import * as React from "react";
import { Search, X } from "lucide-react";
import { Input } from "@/components/ui/input";
import { formatMoney } from "@/lib/format";
import { t } from "@/lib/i18n";
import type { ServiceItem } from "@/lib/types";

/** Мультивыбор услуг прайса: поиск по названию/коду, выбранные — чипсами. */
export function ServicePicker({ services, value, onChange }: { services: ServiceItem[]; value: string[]; onChange: (ids: string[]) => void }) {
  const [q, setQ] = React.useState("");
  const [open, setOpen] = React.useState(false);
  const byId = React.useMemo(() => new Map(services.map((s) => [s.id, s])), [services]);
  const needle = q.trim().toLowerCase();
  const list = services
    .filter((s) => s.isActive && !value.includes(s.id))
    .filter((s) => !needle || s.name.toLowerCase().includes(needle) || (s.code ?? "").toLowerCase().includes(needle))
    .slice(0, 50);

  return (
    <div className="space-y-1.5">
      {value.length ? (
        <div className="flex flex-wrap gap-1">
          {value.map((id) => {
            const s = byId.get(id);
            return (
              <span key={id} className="inline-flex items-center gap-1 rounded-md border bg-muted/50 py-0.5 pl-2 pr-1 text-xs">
                {s?.name ?? id}
                {s ? <span className="text-muted-foreground">· {s.durationMin} {t("schedule.min")}</span> : null}
                <button type="button" className="rounded p-0.5 hover:bg-muted" onClick={() => onChange(value.filter((x) => x !== id))} aria-label={t("common.delete")}>
                  <X className="h-3 w-3" />
                </button>
              </span>
            );
          })}
        </div>
      ) : null}
      <div className="relative">
        <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
        <Input
          className="pl-8"
          placeholder={t("schedule.searchService")}
          value={q}
          data-testid="service-search-input"
          onChange={(e) => setQ(e.target.value)}
          onFocus={() => setOpen(true)}
          onBlur={() => setTimeout(() => setOpen(false), 150)}
        />
        {open ? (
          <div className="absolute inset-x-0 top-full z-10 mt-1 max-h-56 overflow-y-auto rounded-md border bg-popover p-1 shadow-md">
            {list.length === 0 ? <p className="p-2 text-sm text-muted-foreground">{t("common.notFound")}</p> : null}
            {list.map((s) => (
              <button
                key={s.id}
                type="button"
                className="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-muted"
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => {
                  onChange([...value, s.id]);
                  setQ("");
                }}
              >
                <span className="min-w-0 truncate">
                  {s.code ? <span className="mr-1 text-xs text-muted-foreground">{s.code}</span> : null}
                  {s.name}
                </span>
                <span className="shrink-0 text-xs text-muted-foreground">
                  {s.durationMin} {t("schedule.min")} · {formatMoney(s.price)}
                </span>
              </button>
            ))}
          </div>
        ) : null}
      </div>
    </div>
  );
}
