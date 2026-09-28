"use client";

import * as React from "react";
import { AlertTriangle } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { formatDateTime } from "@/lib/format";
import { hasKey, t } from "@/lib/i18n";
import type { AuditEntry } from "@/lib/types";

function renderValue(v: unknown): string {
  if (v === null || v === undefined || v === "") return "—";
  if (typeof v === "string") return /^\d{4}-\d{2}-\d{2}T/.test(v) ? formatDateTime(v) : v;
  if (typeof v === "boolean") return v ? t("common.yes") : t("common.no");
  if (Array.isArray(v)) return v.length ? v.map(renderValue).join(", ") : "—";
  if (typeof v === "object") return JSON.stringify(v);
  return String(v);
}

/** Показ diff «было / стало» из записи аудита. */
export function DiffView({ diff }: { diff: unknown }) {
  if (!diff || typeof diff !== "object") return null;
  const entries = Object.entries(diff as Record<string, unknown>);
  if (entries.length === 0) return null;
  return (
    <table className="mt-1 w-full text-xs">
      <tbody>
        {entries.map(([field, change]) => {
          const c = change as { old?: unknown; new?: unknown } | Record<string, unknown>;
          const isChange = c && typeof c === "object" && ("old" in c || "new" in c);
          return (
            <tr key={field} className="align-top">
              <td className="pr-2 font-mono text-muted-foreground">{field}</td>
              {isChange ? (
                <>
                  <td className="pr-2 text-destructive/80 line-through">{"old" in c ? renderValue(c.old) : ""}</td>
                  <td className="text-success">{renderValue((c as { new?: unknown }).new)}</td>
                </>
              ) : (
                <td colSpan={2}>{renderValue(c)}</td>
              )}
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}

export function AuditList({ items }: { items: AuditEntry[] }) {
  if (items.length === 0) return <p className="p-4 text-sm text-muted-foreground">{t("audit.empty")}</p>;
  return (
    <ul className="divide-y">
      {items.map((a) => (
        <li key={a.id} className="p-3">
          <div className="flex flex-wrap items-center gap-2 text-sm">
            {a.isSuspicious ? <AlertTriangle className="h-4 w-4 text-destructive" /> : null}
            <span className="font-medium">{hasKey(`audit.actions.${a.action}`) ? t(`audit.actions.${a.action}`) : a.action}</span>
            <Badge variant="muted">{a.entityType}</Badge>
            <span className="text-muted-foreground">{a.userName ?? t("audit.system")}</span>
            <span className="ml-auto text-xs text-muted-foreground">{formatDateTime(a.createdAt)}</span>
          </div>
          {a.reason ? <p className="mt-1 text-xs">{t("common.reason")}: {a.reason}</p> : null}
          <DiffView diff={a.diff} />
        </li>
      ))}
    </ul>
  );
}
