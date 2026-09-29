"use client";

import * as React from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Check, X } from "lucide-react";
import { PageHeader, EmptyState } from "@/components/ui/empty-state";
import { Button } from "@/components/ui/button";
import { NativeSelect, Textarea } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Card } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { TableSkeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { ErrorBlock, NoAccess, toastError } from "@/components/admin/common";
import { api } from "@/lib/api-client";
import { useAuth } from "@/lib/auth";
import { useBranches } from "@/lib/queries";
import { formatDateTime, formatMoney, formatPercent } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import type { Schemas } from "@/lib/types";

type Approval = Schemas["ApprovalDto"];
type ApprovalType = Schemas["ApprovalType"];
type Status = Schemas["ApprovalStatus"];
const TYPES: ApprovalType[] = ["Discount", "Writeoff", "Refund", "ClosedVisitEdit", "PurchaseOrder", "TransferShortage", "PayrollPeriodChange"];

const APPROVAL_PERMS = [P.discountsApply, P.writeoff, P.cashRefund, P.visitsEditClosed, P.purchaseApprove, P.payrollManage];

/** Сумма/значение запроса: для скидки — проценты, для правок визитов — нет суммы, остальное — деньги (тиыны). */
function formatAmount(a: Approval): string {
  if (a.type === "Discount") return formatPercent(a.amount);
  if (a.type === "ClosedVisitEdit" || a.type === "PayrollPeriodChange") return a.amount ? formatMoney(a.amount) : "—";
  return formatMoney(a.amount);
}

const ID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-/i;

/** Краткое представление payload: только скалярные поля, без идентификаторов. */
function payloadEntries(p: unknown): [string, string][] {
  if (!p || typeof p !== "object" || Array.isArray(p)) return [];
  const out: [string, string][] = [];
  for (const [k, v] of Object.entries(p as Record<string, unknown>)) {
    if (v === null || v === undefined || typeof v === "object") continue;
    if (/id$/i.test(k) || (typeof v === "string" && ID_RE.test(v))) continue;
    const key = `approvals.payload.${k}`;
    const label = t(key) === key ? k : t(key);
    let value = String(v);
    if (typeof v === "number" && /amount|sum|total|price/i.test(k)) value = formatMoney(v);
    if (typeof v === "boolean") value = v ? t("common.yes") : t("common.no");
    out.push([label, value]);
  }
  return out;
}

export default function ApprovalsPage() {
  const { can } = useAuth();
  if (!can(...APPROVAL_PERMS)) return <NoAccess />;
  return (
    <div>
      <PageHeader title={t("approvals.title")} />
      <Tabs defaultValue="pending">
        <TabsList>
          <TabsTrigger value="pending">{t("approvals.tabs.pending")}</TabsTrigger>
          <TabsTrigger value="history">{t("approvals.tabs.history")}</TabsTrigger>
        </TabsList>
        <TabsContent value="pending">
          <ApprovalsList mode="pending" />
        </TabsContent>
        <TabsContent value="history">
          <ApprovalsList mode="history" />
        </TabsContent>
      </Tabs>
    </div>
  );
}

function ApprovalsList({ mode }: { mode: "pending" | "history" }) {
  const branches = useBranches();
  const [type, setType] = React.useState("");
  const [status, setStatus] = React.useState<"" | Status>("");
  const [decision, setDecision] = React.useState<{ a: Approval; approve: boolean } | null>(null);
  const query = useQuery({
    queryKey: ["approvals", mode === "pending" ? "Pending" : status || "all"],
    queryFn: () => api<Approval[]>("/approvals", { query: { status: mode === "pending" ? "Pending" : status || undefined } }),
    refetchInterval: mode === "pending" ? 60_000 : false,
  });
  const branchName = React.useMemo(() => new Map((branches.data ?? []).map((b) => [b.id, b.name])), [branches.data]);
  const rows = (query.data ?? []).filter((a) => (mode === "history" ? a.status !== "Pending" || status === "Pending" : true) && (!type || a.type === type));

  return (
    <>
      <Card className="mb-3 flex flex-wrap items-center gap-3 p-3">
        <NativeSelect className="w-60" value={type} onChange={(e) => setType(e.target.value)} aria-label={t("approvals.type")}>
          <option value="">{t("approvals.allTypes")}</option>
          {TYPES.map((x) => (
            <option key={x} value={x}>
              {t(`approvals.types.${x}`)}
            </option>
          ))}
        </NativeSelect>
        {mode === "history" ? (
          <NativeSelect className="w-44" value={status} onChange={(e) => setStatus(e.target.value as "" | Status)} aria-label={t("common.status")}>
            <option value="">{t("admin.all")}</option>
            <option value="Approved">{t("approvals.statuses.Approved")}</option>
            <option value="Rejected">{t("approvals.statuses.Rejected")}</option>
          </NativeSelect>
        ) : null}
      </Card>
      <Card>
        {query.isLoading ? (
          <TableSkeleton />
        ) : query.isError ? (
          <ErrorBlock className="m-4" onRetry={() => query.refetch()} />
        ) : rows.length === 0 ? (
          <EmptyState className="m-4" title={mode === "pending" ? t("approvals.empty") : t("approvals.emptyHistory")} />
        ) : (
          <Table>
            <THead>
              <TR>
                <TH>{t("approvals.createdAt")}</TH>
                <TH>{t("approvals.type")}</TH>
                <TH>{t("approvals.details")}</TH>
                <TH className="text-right">{t("approvals.amount")}</TH>
                <TH>{t("approvals.requestedBy")}</TH>
                <TH>{mode === "pending" ? "" : t("approvals.decision")}</TH>
              </TR>
            </THead>
            <TBody>
              {rows.map((a) => {
                const extra = payloadEntries(a.payload);
                return (
                  <TR key={a.id}>
                    <TD className="whitespace-nowrap text-xs">{formatDateTime(a.createdAt)}</TD>
                    <TD>
                      <Badge variant="secondary">{t(`approvals.types.${a.type}`)}</Badge>
                    </TD>
                    <TD className="max-w-md">
                      <div>{a.summary ?? "—"}</div>
                      {extra.length ? (
                        <div className="mt-0.5 text-xs text-muted-foreground">{extra.map(([k, v]) => `${k}: ${v}`).join(" · ")}</div>
                      ) : null}
                      {a.branchId ? <div className="text-xs text-muted-foreground">{branchName.get(a.branchId)}</div> : null}
                    </TD>
                    <TD className="whitespace-nowrap text-right tabular-nums">{formatAmount(a)}</TD>
                    <TD className="text-sm">{a.requestedByName}</TD>
                    <TD>
                      {a.status === "Pending" ? (
                        a.canDecide ? (
                          <div className="flex justify-end gap-1.5">
                            <Button size="sm" variant="success" onClick={() => setDecision({ a, approve: true })}>
                              <Check /> {t("approvals.approve")}
                            </Button>
                            <Button size="sm" variant="outline" className="text-destructive" onClick={() => setDecision({ a, approve: false })}>
                              <X /> {t("approvals.reject")}
                            </Button>
                          </div>
                        ) : (
                          <span className="text-xs text-muted-foreground">{t("approvals.cannotDecide")}</span>
                        )
                      ) : (
                        <div className="text-xs">
                          <Badge variant={a.status === "Approved" ? "success" : "destructive"}>{t(`approvals.statuses.${a.status}`)}</Badge>
                          <div className="mt-1 text-muted-foreground">
                            {a.decidedByName ?? "—"} · {formatDateTime(a.decidedAt)}
                          </div>
                          {a.comment ? <div className="mt-0.5 italic">«{a.comment}»</div> : null}
                        </div>
                      )}
                    </TD>
                  </TR>
                );
              })}
            </TBody>
          </Table>
        )}
      </Card>
      <DecisionDialog decision={decision} onClose={() => setDecision(null)} />
    </>
  );
}

function DecisionDialog({ decision, onClose }: { decision: { a: Approval; approve: boolean } | null; onClose: () => void }) {
  const qc = useQueryClient();
  const { reloadMe } = useAuth();
  const [comment, setComment] = React.useState("");
  React.useEffect(() => setComment(""), [decision]);
  const approve = decision?.approve ?? true;

  const decide = useMutation({
    mutationFn: () =>
      api<Approval>(`/approvals/${decision!.a.id}/${approve ? "approve" : "reject"}`, {
        method: "POST",
        body: { comment: comment.trim() || null } satisfies Schemas["ApprovalDecisionRequest"],
      }),
    onSuccess: () => {
      toast.success(approve ? t("approvals.approvedToast") : t("approvals.rejectedToast"));
      void qc.invalidateQueries({ queryKey: ["approvals"] });
      void qc.invalidateQueries({ queryKey: ["visit"] });
      void qc.invalidateQueries({ queryKey: ["visits"] });
      void reloadMe().catch(() => undefined);
      onClose();
    },
    onError: toastError,
  });

  const needComment = !approve;
  return (
    <Dialog open={!!decision} onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{approve ? t("approvals.approveTitle") : t("approvals.rejectTitle")}</DialogTitle>
          {decision ? (
            <DialogDescription>
              {t(`approvals.types.${decision.a.type}`)}: {decision.a.summary ?? "—"} ({decision.a.requestedByName})
            </DialogDescription>
          ) : null}
        </DialogHeader>
        <Field label={needComment ? t("approvals.commentRequired") : t("approvals.commentOptional")}>
          <Textarea autoFocus rows={3} value={comment} onChange={(e) => setComment(e.target.value)} />
        </Field>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            {t("common.cancel")}
          </Button>
          <Button
            variant={approve ? "success" : "destructive"}
            disabled={needComment && !comment.trim()}
            loading={decide.isPending}
            onClick={() => decide.mutate()}
          >
            {approve ? t("approvals.approve") : t("approvals.reject")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
