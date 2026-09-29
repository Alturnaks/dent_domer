"use client";

import * as React from "react";
import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { AlertTriangle } from "lucide-react";
import { EmptyState, PageHeader } from "@/components/ui/empty-state";
import { Card } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect } from "@/components/ui/input";
import { Checkbox } from "@/components/ui/checkbox";
import { TableSkeleton } from "@/components/ui/skeleton";
import { DiffView } from "@/components/audit/audit-list";
import { auditActionName, auditEntityName, SuspiciousSummary } from "@/components/audit/suspicious-summary";
import { api } from "@/lib/api-client";
import { useAuth } from "@/lib/auth";
import { formatDateTime } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import type { AuditEntry, CursorPage, Staff } from "@/lib/types";

const ENTITY_TYPES = ["Visit", "Appointment", "Patient", "Payment", "CashShift", "Expense", "StockDocument", "Item", "Service", "PriceList", "Role", "Membership", "ApprovalRequest", "Organization", "Branch", "User", "PayrollScheme", "PayrollPeriod", "PayrollEntry"];

const entityName = auditEntityName;
const actionName = auditActionName;

export default function AuditPage() {
  const { can } = useAuth();
  const [entity, setEntity] = React.useState("");
  const [user, setUser] = React.useState("");
  const [from, setFrom] = React.useState("");
  const [to, setTo] = React.useState("");
  const [suspicious, setSuspicious] = React.useState(false);
  const [action, setAction] = React.useState("");
  const allowed = can(P.auditView);

  const staff = useQuery({ queryKey: ["staff", "audit-filter"], queryFn: () => api<Staff[]>("/staff", { query: { include_fired: true } }), enabled: allowed, staleTime: 5 * 60_000 });

  const query = useInfiniteQuery({
    queryKey: ["audit", entity, user, from, to, suspicious, action],
    enabled: allowed,
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) =>
      api<CursorPage<AuditEntry>>("/audit", {
        query: {
          entity_type: entity || undefined,
          user_id: user || undefined,
          from: from ? new Date(`${from}T00:00:00`).toISOString() : undefined,
          to: to ? new Date(`${to}T23:59:59`).toISOString() : undefined,
          suspicious: suspicious || undefined,
          action: action || undefined,
          cursor: pageParam ?? undefined,
          limit: 50,
        },
      }),
    getNextPageParam: (last) => last.nextCursor ?? null,
  });
  const rows = query.data?.pages.flatMap((p) => p.items) ?? [];

  if (!allowed) {
    return (
      <div>
        <PageHeader title={t("audit.title")} />
        <EmptyState title={t("errors.FORBIDDEN")} />
      </div>
    );
  }

  const reset = () => {
    setEntity("");
    setUser("");
    setFrom("");
    setTo("");
    setSuspicious(false);
    setAction("");
  };

  return (
    <div>
      <PageHeader title={t("audit.title")} description={t("audit.description")} />
      <SuspiciousSummary
        active={action}
        onPick={(a) => {
          setAction(a);
          if (a) setSuspicious(true);
        }}
      />
      <Card className="mb-3 flex flex-wrap items-center gap-3 p-3">
        <NativeSelect className="w-48" value={entity} onChange={(e) => setEntity(e.target.value)} aria-label={t("audit.entity")}>
          <option value="">{t("audit.allEntities")}</option>
          {ENTITY_TYPES.map((e) => (
            <option key={e} value={e}>
              {entityName(e)}
            </option>
          ))}
        </NativeSelect>
        <NativeSelect className="w-60" value={user} onChange={(e) => setUser(e.target.value)} aria-label={t("audit.user")}>
          <option value="">{t("audit.allUsers")}</option>
          {staff.data?.map((s) => (
            <option key={s.membershipId} value={s.userId}>
              {s.fullName}
            </option>
          ))}
        </NativeSelect>
        <label className="flex items-center gap-2 text-sm">
          {t("audit.from")}
          <Input type="date" className="w-40" value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label className="flex items-center gap-2 text-sm">
          {t("audit.to")}
          <Input type="date" className="w-40" value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
        <label className="flex items-center gap-2 text-sm">
          <Checkbox checked={suspicious} onCheckedChange={(v) => setSuspicious(v === true)} />
          {t("audit.suspiciousOnly")}
        </label>
        <Button variant="ghost" size="sm" onClick={reset}>
          {t("audit.reset")}
        </Button>
      </Card>

      <Card>
        {query.isLoading ? (
          <TableSkeleton />
        ) : rows.length === 0 ? (
          <EmptyState className="m-4" title={t("audit.empty")} />
        ) : (
          <ul className="divide-y">
            {rows.map((a) => (
              <li key={a.id} className={`p-3 ${a.isSuspicious ? "bg-destructive/5" : ""}`}>
                <div className="flex flex-wrap items-center gap-2 text-sm">
                  {a.isSuspicious ? <AlertTriangle className="h-4 w-4 text-destructive" /> : null}
                  <span className="font-medium">{actionName(a.action)}</span>
                  <Badge variant="muted">{entityName(a.entityType)}</Badge>
                  <span className="text-muted-foreground">{a.userName ?? t("audit.system")}</span>
                  {a.ip ? <span className="font-mono text-xs text-muted-foreground">{a.ip}</span> : null}
                  <span className="ml-auto text-xs text-muted-foreground tabular">{formatDateTime(a.createdAt)}</span>
                </div>
                {a.reason ? (
                  <p className="mt-1 text-xs">
                    {t("common.reason")}: {a.reason}
                  </p>
                ) : null}
                <DiffView diff={a.diff} />
              </li>
            ))}
          </ul>
        )}
        {query.hasNextPage ? (
          <div className="flex justify-center p-3">
            <Button variant="outline" loading={query.isFetchingNextPage} onClick={() => query.fetchNextPage()}>
              {t("audit.loadMore")}
            </Button>
          </div>
        ) : null}
      </Card>
    </div>
  );
}
