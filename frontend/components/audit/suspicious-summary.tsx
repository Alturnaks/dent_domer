"use client";

import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, ShieldCheck } from "lucide-react";
import { Card } from "@/components/ui/card";
import { api } from "@/lib/api-client";
import { formatDateTime } from "@/lib/format";
import { hasKey, t } from "@/lib/i18n";
import type { Schemas } from "@/lib/types";

type Summary = Schemas["SuspiciousSummaryDto"];

/** Название действия аудита: общий словарь, затем словарь подозрительных событий/зарплаты. */
export function auditActionName(a: string): string {
  if (hasKey(`audit.actions.${a}`)) return t(`audit.actions.${a}`);
  if (hasKey(`payroll.auditActions.${a}`)) return t(`payroll.auditActions.${a}`);
  return a;
}

export function auditEntityName(e: string): string {
  if (hasKey(`audit.entities.${e}`)) return t(`audit.entities.${e}`);
  if (hasKey(`payroll.auditEntities.${e}`)) return t(`payroll.auditEntities.${e}`);
  return e;
}

/** Сводка подозрительных событий за 7 дней по типам; клик — фильтр ленты по типу. */
export function SuspiciousSummary({ active, onPick }: { active: string; onPick: (action: string) => void }) {
  const q = useQuery({ queryKey: ["audit", "suspicious-summary"], queryFn: () => api<Summary>("/audit/suspicious-summary", { query: { days: 7 } }) });
  if (!q.data) return null;
  const s = q.data;
  if (s.total === 0) {
    return (
      <Card className="mb-3 flex items-center gap-2 p-3 text-sm text-muted-foreground">
        <ShieldCheck className="h-4 w-4 text-success" /> {t("payroll.suspicious.none")}
      </Card>
    );
  }
  return (
    <Card className="mb-3 p-3">
      <div className="mb-2 flex items-center gap-2 text-sm font-medium">
        <AlertTriangle className="h-4 w-4 text-destructive" /> {t("payroll.suspicious.title")}
        <span className="text-muted-foreground">· {t("payroll.suspicious.total", { count: s.total })}</span>
      </div>
      <div className="flex flex-wrap gap-2">
        {s.byAction.map((a) => (
          <button
            key={a.action}
            type="button"
            onClick={() => onPick(active === a.action ? "" : a.action)}
            title={t("payroll.suspicious.last", { date: formatDateTime(a.lastAt) })}
            className={`flex items-center gap-2 rounded-md border px-2.5 py-1.5 text-left text-sm transition-colors hover:bg-muted ${
              active === a.action ? "border-destructive bg-destructive/10" : ""
            }`}
          >
            <span>{auditActionName(a.action)}</span>
            <span className="tabular rounded-full bg-destructive/12 px-1.5 text-xs font-semibold text-destructive">{a.count}</span>
          </button>
        ))}
      </div>
    </Card>
  );
}
