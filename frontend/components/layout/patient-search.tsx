"use client";

import * as React from "react";
import { useRouter } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { Search } from "lucide-react";
import { Dialog, DialogContent, DialogTitle } from "@/components/ui/dialog";
import { api } from "@/lib/api-client";
import { useAuth } from "@/lib/auth";
import { t } from "@/lib/i18n";
import { formatDate, formatPhone } from "@/lib/format";

type PatientHit = { id: string; fullName: string; phone?: string | null; birthDate?: string | null };

/** Глобальный поиск пациента (Ctrl+K). */
export function PatientSearchDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (o: boolean) => void }) {
  const [q, setQ] = React.useState("");
  const [debounced, setDebounced] = React.useState("");
  const { me, can } = useAuth();
  const router = useRouter();

  React.useEffect(() => {
    const id = setTimeout(() => setDebounced(q.trim()), 250);
    return () => clearTimeout(id);
  }, [q]);
  React.useEffect(() => {
    if (!open) setQ("");
  }, [open]);

  const enabled = open && debounced.length >= 2 && can("patients.view");
  const { data, isFetching } = useQuery({
    queryKey: ["patient-search", debounced],
    queryFn: () => api<{ items: PatientHit[] }>("/patients", { query: { q: debounced, limit: 10 } }),
    enabled,
    retry: false,
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="top-[20%] translate-y-0 gap-0 p-0">
        <DialogTitle className="sr-only">{t("header.searchPatient")}</DialogTitle>
        <div className="flex items-center gap-2 border-b px-3">
          <Search className="h-4 w-4 text-muted-foreground" />
          <input
            autoFocus
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder={t("header.searchPatient")}
            className="h-12 flex-1 bg-transparent text-sm outline-none"
          />
        </div>
        <div className="max-h-80 overflow-y-auto p-1">
          {isFetching ? <p className="p-3 text-sm text-muted-foreground">{t("common.loading")}</p> : null}
          {data?.items.map((p) => (
            <button
              key={p.id}
              className="flex w-full flex-col items-start rounded-md px-3 py-2 text-left hover:bg-muted"
              onClick={() => {
                onOpenChange(false);
                router.push(`/${me?.organization.slug}/patients/${p.id}`);
              }}
            >
              <span className="text-sm font-medium">{p.fullName}</span>
              <span className="text-xs text-muted-foreground">
                {formatPhone(p.phone)} {p.birthDate ? `· ${formatDate(p.birthDate)}` : ""}
              </span>
            </button>
          ))}
          {enabled && !isFetching && data && data.items.length === 0 ? (
            <p className="p-3 text-sm text-muted-foreground">{t("common.notFound")}</p>
          ) : null}
        </div>
      </DialogContent>
    </Dialog>
  );
}
