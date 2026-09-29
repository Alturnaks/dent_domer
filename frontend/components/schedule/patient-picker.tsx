"use client";

import * as React from "react";
import { useQuery } from "@tanstack/react-query";
import { Search, Star, UserPlus, X } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { PatientFormDialog } from "@/components/patients/patient-form-dialog";
import { api } from "@/lib/api-client";
import { useAuth } from "@/lib/auth";
import { formatDate, formatMoney, formatPhone } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import type { CursorPage, PatientListItem } from "@/lib/types";

export type PickedPatient = { id: string; fullName: string; phone?: string | null; birthDate?: string | null; isVip?: boolean; balance?: number };

/** Поиск пациента по ФИО/телефону с быстрым созданием нового. */
export function PatientPicker({ value, onChange, autoFocus }: { value: PickedPatient | null; onChange: (p: PickedPatient | null) => void; autoFocus?: boolean }) {
  const { can } = useAuth();
  const [q, setQ] = React.useState("");
  const [debounced, setDebounced] = React.useState("");
  const [createOpen, setCreateOpen] = React.useState(false);
  const [active, setActive] = React.useState(0);

  React.useEffect(() => {
    const id = setTimeout(() => setDebounced(q.trim()), 250);
    return () => clearTimeout(id);
  }, [q]);

  const enabled = !value && debounced.length >= 2;
  const res = useQuery({
    queryKey: ["patient-search", debounced],
    queryFn: () => api<CursorPage<PatientListItem>>("/patients", { query: { q: debounced, limit: 8 } }),
    enabled,
    retry: false,
  });
  const items = enabled ? (res.data?.items ?? []) : [];

  const pick = (p: PatientListItem) => {
    onChange({ id: p.id, fullName: p.fullName, phone: p.phone, birthDate: p.birthDate, isVip: p.isVip, balance: p.balance });
    setQ("");
  };

  if (value) {
    return (
      <div className="flex items-center justify-between gap-2 rounded-md border bg-muted/40 px-3 py-1.5" data-testid="picked-patient">
        <div className="min-w-0">
          <div className="flex items-center gap-1 truncate text-sm font-medium">
            {value.isVip ? <Star className="h-3.5 w-3.5 fill-warning text-warning" /> : null}
            {value.fullName}
          </div>
          <div className="text-xs text-muted-foreground">
            {formatPhone(value.phone)}
            {value.birthDate ? ` · ${formatDate(value.birthDate)}` : ""}
            {value.balance ? (
              <span className={value.balance < 0 ? "text-destructive" : "text-success"}>
                {" "}
                · {t("schedule.balance")}: {formatMoney(value.balance)}
              </span>
            ) : null}
          </div>
        </div>
        <Button type="button" variant="ghost" size="icon-sm" onClick={() => onChange(null)} aria-label={t("common.reset")}>
          <X />
        </Button>
      </div>
    );
  }

  return (
    <div className="relative">
      <div className="flex gap-2">
        <div className="relative flex-1">
          <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input
            autoFocus={autoFocus}
            className="pl-8"
            placeholder={t("schedule.searchPatient")}
            value={q}
            data-testid="patient-search-input"
            onChange={(e) => {
              setQ(e.target.value);
              setActive(0);
            }}
            onKeyDown={(e) => {
              if (e.key === "ArrowDown") {
                e.preventDefault();
                setActive((i) => Math.min(i + 1, items.length - 1));
              } else if (e.key === "ArrowUp") {
                e.preventDefault();
                setActive((i) => Math.max(i - 1, 0));
              } else if (e.key === "Enter" && items[active]) {
                e.preventDefault();
                pick(items[active]);
              }
            }}
          />
        </div>
        {can(P.patientsEdit, P.scheduleManage) ? (
          <Button type="button" variant="outline" onClick={() => setCreateOpen(true)}>
            <UserPlus /> {t("schedule.createPatient")}
          </Button>
        ) : null}
      </div>
      {enabled ? (
        <div className="absolute inset-x-0 top-full z-10 mt-1 max-h-64 overflow-y-auto rounded-md border bg-popover p-1 shadow-md">
          {res.isFetching && items.length === 0 ? <p className="p-2 text-sm text-muted-foreground">{t("common.loading")}</p> : null}
          {items.map((p, i) => (
            <button
              key={p.id}
              type="button"
              className={`flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left hover:bg-muted ${i === active ? "bg-muted" : ""}`}
              onMouseEnter={() => setActive(i)}
              onClick={() => pick(p)}
            >
              <span className="min-w-0">
                <span className="flex items-center gap-1 truncate text-sm font-medium">
                  {p.isVip ? <Star className="h-3 w-3 fill-warning text-warning" /> : null}
                  {p.fullName}
                </span>
                <span className="block text-xs text-muted-foreground">
                  {formatPhone(p.phone)} {p.birthDate ? `· ${formatDate(p.birthDate)}` : ""}
                </span>
              </span>
              {p.balance < 0 ? <span className="shrink-0 text-xs text-destructive">{formatMoney(p.balance)}</span> : null}
            </button>
          ))}
          {!res.isFetching && res.data && items.length === 0 ? (
            <div className="flex items-center justify-between gap-2 p-2 text-sm text-muted-foreground">
              {t("common.notFound")}
              {can(P.patientsEdit, P.scheduleManage) ? (
                <Button type="button" size="sm" variant="link" onClick={() => setCreateOpen(true)}>
                  {t("schedule.createPatient")}
                </Button>
              ) : null}
            </div>
          ) : null}
        </div>
      ) : null}
      <PatientFormDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        initialName={/\d/.test(q) ? undefined : q}
        onSaved={(p) => {
          onChange({ id: p.id, fullName: p.fullName, phone: p.phone, birthDate: p.birthDate, isVip: p.isVip, balance: 0 });
          setQ("");
        }}
      />
    </div>
  );
}
