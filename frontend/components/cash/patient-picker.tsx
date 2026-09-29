"use client";

import * as React from "react";
import { useQuery } from "@tanstack/react-query";
import { Search } from "lucide-react";
import { api } from "@/lib/api-client";
import { formatDate, formatMoney, formatPhone } from "@/lib/format";
import { t } from "@/lib/i18n";
import type { PatientListItem } from "@/lib/types";

export type PickedPatient = { id: string; fullName: string; phone?: string | null };

/** Поиск пациента по ФИО/телефону/ИИН с выпадающим списком результатов. */
export function PatientPicker({ onPick, autoFocus }: { onPick: (p: PickedPatient) => void; autoFocus?: boolean }) {
  const [q, setQ] = React.useState("");
  const [debounced, setDebounced] = React.useState("");
  React.useEffect(() => {
    const id = setTimeout(() => setDebounced(q.trim()), 250);
    return () => clearTimeout(id);
  }, [q]);

  const enabled = debounced.length >= 2;
  const { data, isFetching } = useQuery({
    queryKey: ["patient-search", debounced],
    queryFn: () => api<{ items: PatientListItem[] }>("/patients", { query: { q: debounced, limit: 8 } }),
    enabled,
    retry: false,
  });

  return (
    <div className="rounded-md border">
      <div className="flex items-center gap-2 border-b px-3">
        <Search className="h-4 w-4 text-muted-foreground" />
        <input
          autoFocus={autoFocus}
          value={q}
          onChange={(e) => setQ(e.target.value)}
          placeholder={t("cash.pay.searchPatient")}
          className="h-10 flex-1 bg-transparent text-sm outline-none"
        />
      </div>
      <div className="max-h-60 overflow-y-auto p-1">
        {isFetching ? <p className="p-2 text-sm text-muted-foreground">{t("common.loading")}</p> : null}
        {(data?.items ?? []).map((p) => (
          <button
            type="button"
            key={p.id}
            className="flex w-full items-center justify-between gap-3 rounded-md px-3 py-2 text-left hover:bg-muted"
            onClick={() => onPick({ id: p.id, fullName: p.fullName, phone: p.phone })}
          >
            <span className="min-w-0">
              <span className="block truncate text-sm font-medium">{p.fullName}</span>
              <span className="block text-xs text-muted-foreground">
                {formatPhone(p.phone)} {p.birthDate ? `· ${formatDate(p.birthDate)}` : ""}
              </span>
            </span>
            {p.balance !== 0 ? (
              <span className={`shrink-0 text-xs tabular ${p.balance < 0 ? "text-destructive" : "text-success"}`}>{formatMoney(p.balance)}</span>
            ) : null}
          </button>
        ))}
        {enabled && !isFetching && data && data.items.length === 0 ? <p className="p-2 text-sm text-muted-foreground">{t("common.notFound")}</p> : null}
      </div>
    </div>
  );
}
