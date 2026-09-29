"use client";

import * as React from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { AlertTriangle, ArrowLeft, Ban, CheckCircle2, Pencil, Percent, Plus, Trash2, Wallet, Wrench } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { useConfirm } from "@/components/ui/confirm";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input, Textarea } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { EmptyState, PageHeader } from "@/components/ui/empty-state";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { PaymentDialog } from "@/components/cash/payment-dialog";
import { apiErrorText } from "@/components/cash/shared";
import { VisitCorrectionDialog } from "@/components/visits/correction-dialog";
import { VisitItemDialog } from "@/components/visits/item-dialog";
import { VisitMaterialsTab } from "@/components/visits/materials-tab";
import { api, ApiError } from "@/lib/api-client";
import { useAuth, useOrgHref } from "@/lib/auth";
import { formatDate, formatDateTime, formatMoney, formatPhone, formatPercent } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import type { Schemas } from "@/lib/types";

type Visit = Schemas["VisitDto"];
type VisitItem = Schemas["VisitItemDto"];

const STATUS_VARIANT: Record<Schemas["VisitStatus"], "warning" | "success" | "destructive"> = { Open: "warning", Closed: "success", Cancelled: "destructive" };

function Row({ label, value, strong, tone }: { label: string; value: React.ReactNode; strong?: boolean; tone?: string }) {
  return (
    <div className={`flex justify-between gap-3 py-1 text-sm ${strong ? "font-semibold" : ""}`}>
      <span className={strong ? "" : "text-muted-foreground"}>{label}</span>
      <span className={`tabular text-right ${tone ?? ""}`}>{value}</span>
    </div>
  );
}

export default function VisitPage() {
  const { id } = useParams<{ id: string }>();
  const qc = useQueryClient();
  const confirm = useConfirm();
  const { can } = useAuth();
  const { href } = useOrgHref();

  const visit = useQuery({ queryKey: ["visit", id], queryFn: () => api<Visit>(`/visits/${id}`), retry: (n, e) => !(e instanceof ApiError && e.status < 500) && n < 2 });
  const [tab, setTab] = React.useState("items");
  const [itemDialog, setItemDialog] = React.useState<{ open: boolean; item: VisitItem | null }>({ open: false, item: null });
  const [payOpen, setPayOpen] = React.useState(false);
  const [cancelOpen, setCancelOpen] = React.useState(false);
  const [correctionOpen, setCorrectionOpen] = React.useState(false);
  const [discountOpen, setDiscountOpen] = React.useState(false);

  const setVisit = (v: Visit) => qc.setQueryData(["visit", id], v);
  const onError = (e: unknown) => {
    toast.error(apiErrorText(e));
    if (e instanceof ApiError && (e.status === 409 || e.code === "CONCURRENCY_CONFLICT")) qc.invalidateQueries({ queryKey: ["visit", id] });
  };

  const removeItem = useMutation({
    mutationFn: (itemId: string) => api<Visit>(`/visits/${id}/items/${itemId}`, { method: "DELETE" }),
    onSuccess: (v) => {
      setVisit(v);
      qc.invalidateQueries({ queryKey: ["visit", id, "materials"] });
      toast.success(t("visits.items.removed"));
    },
    onError,
  });
  const closeVisit = useMutation({
    mutationFn: (v: Visit) => api<Visit>(`/visits/${id}/close`, { method: "POST", body: { version: v.version } }),
    onSuccess: (v) => {
      setVisit(v);
      qc.invalidateQueries({ queryKey: ["patient", v.patientId] });
      qc.invalidateQueries({ queryKey: ["calendar"] });
      toast.success(t("visits.actions.closed"));
      if (v.debt > 0 && can(P.cashPayment)) setPayOpen(true);
    },
    onError,
  });

  if (visit.isLoading) {
    return (
      <div className="space-y-3">
        <Skeleton className="h-8 w-80" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }
  if (visit.isError || !visit.data) {
    return (
      <EmptyState
        title={visit.error instanceof ApiError && visit.error.status === 404 ? t("visits.notFound") : apiErrorText(visit.error)}
        action={
          <Button variant="outline" onClick={() => visit.refetch()}>
            {t("common.retry")}
          </Button>
        }
      />
    );
  }

  const v = visit.data;
  const editable = v.status === "Open" && v.canEdit;
  const canDiscount = editable && can(P.discountsApply);
  const pending = v.approvalState !== "None";
  const canClose = v.status === "Open" && can(P.visitsComplete);
  const canCancel = can(P.visitsCancel) && (v.status === "Open" || (v.status === "Closed" && can(P.visitsEditClosed)));
  const canCorrect = v.status === "Closed" && v.canCorrect && v.approvalState === "None";
  const canPay = v.status !== "Cancelled" && v.debt > 0 && can(P.cashPayment) && v.status === "Closed";

  const askClose = async () => {
    const ok = await confirm({ title: t("visits.actions.closeConfirm"), description: t("visits.actions.closeDescription", { amount: formatMoney(v.total - v.paidTotal) }), confirmText: t("visits.actions.close") });
    if (ok) closeVisit.mutate(v);
  };
  const askRemove = async (item: VisitItem) => {
    const ok = await confirm({ title: t("visits.items.removeConfirm", { name: item.serviceName }), destructive: true, confirmText: t("common.delete") });
    if (ok) removeItem.mutate(item.id);
  };

  return (
    <div>
      {can(P.patientsView) ? (
        <Link href={href(`patients/${v.patientId}`)} className="mb-2 inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="h-4 w-4" /> {t("visits.backToPatient")}
        </Link>
      ) : null}
      <PageHeader
        title={t("visits.titleFrom", { date: formatDate(v.openedAt) })}
        description={
          <span className="flex flex-wrap items-center gap-2">
            <Badge variant={STATUS_VARIANT[v.status]}>{t(`visits.status.${v.status}`)}</Badge>
            {pending ? <Badge variant="warning">{t(`visits.approval.${v.approvalState}`)}</Badge> : null}
            {v.shiftClosed && v.status === "Closed" ? <Badge variant="muted">{t("visits.shiftClosed")}</Badge> : null}
          </span>
        }
        actions={
          <>
            {canCancel ? (
              <Button variant="outline" onClick={() => setCancelOpen(true)}>
                <Ban /> {t("visits.actions.cancel")}
              </Button>
            ) : null}
            {canCorrect ? (
              <Button variant="outline" onClick={() => setCorrectionOpen(true)}>
                <Wrench /> {t("visits.actions.correct")}
              </Button>
            ) : null}
            {canDiscount && v.items.length > 0 ? (
              <Button variant="outline" onClick={() => setDiscountOpen(true)}>
                <Percent /> {t("visits.actions.discount")}
              </Button>
            ) : null}
            {canClose ? (
              <Button onClick={askClose} loading={closeVisit.isPending} disabled={pending || v.items.length === 0}>
                <CheckCircle2 /> {t("visits.actions.close")}
              </Button>
            ) : null}
            {canPay ? (
              <Button variant="success" onClick={() => setPayOpen(true)}>
                <Wallet /> {t("visits.actions.pay")}
              </Button>
            ) : null}
          </>
        }
      />

      {pending && v.status === "Open" ? (
        <div className="mb-4 flex items-start gap-2 rounded-md border border-warning/50 bg-warning/10 p-3 text-sm">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-[oklch(0.5_0.12_70)]" />
          <span>{t("visits.approvalHint")}</span>
        </div>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-[1fr_20rem]">
        <div className="min-w-0">
          <Tabs value={tab} onValueChange={setTab}>
            <TabsList>
              <TabsTrigger value="items">
                {t("visits.tabs.items")} ({v.items.length})
              </TabsTrigger>
              <TabsTrigger value="materials">{t("visits.tabs.materials")}</TabsTrigger>
              <TabsTrigger value="payments">
                {t("visits.tabs.payments")} ({v.payments.length})
              </TabsTrigger>
            </TabsList>

            <TabsContent value="items">
              <Card>
                {v.items.length === 0 ? (
                  <EmptyState
                    className="m-3 border-0"
                    title={t("visits.items.empty")}
                    description={editable ? t("visits.items.emptyHint") : undefined}
                    action={
                      editable ? (
                        <Button onClick={() => setItemDialog({ open: true, item: null })}>
                          <Plus /> {t("visits.items.add")}
                        </Button>
                      ) : undefined
                    }
                  />
                ) : (
                  <>
                    <Table>
                      <THead>
                        <TR>
                          <TH>{t("visits.items.service")}</TH>
                          <TH>{t("visits.items.tooth")}</TH>
                          <TH className="text-right">{t("visits.items.qty")}</TH>
                          <TH className="text-right">{t("visits.items.price")}</TH>
                          <TH className="text-right">{t("visits.items.discount")}</TH>
                          <TH className="text-right">{t("visits.items.total")}</TH>
                          {editable ? <TH /> : null}
                        </TR>
                      </THead>
                      <TBody>
                        {v.items.map((i) => (
                          <TR key={i.id}>
                            <TD>
                              <div>{i.serviceName}</div>
                              <div className="text-xs text-muted-foreground">{i.serviceCode}</div>
                            </TD>
                            <TD className="tabular">{i.toothNumbers.length ? i.toothNumbers.join(", ") : "—"}</TD>
                            <TD className="tabular text-right">{i.qty}</TD>
                            <TD className="tabular whitespace-nowrap text-right">{formatMoney(i.unitPrice)}</TD>
                            <TD className="tabular whitespace-nowrap text-right">
                              {i.discountPct ? (
                                <span className="inline-flex flex-col items-end">
                                  <span>
                                    {formatPercent(i.discountPct)} · {formatMoney(i.discountAmount)}
                                  </span>
                                  {i.discountPendingApproval ? <Badge variant="warning">{t("visits.items.pending")}</Badge> : null}
                                </span>
                              ) : (
                                "—"
                              )}
                            </TD>
                            <TD className="tabular whitespace-nowrap text-right font-medium">{formatMoney(i.total)}</TD>
                            {editable ? (
                              <TD className="whitespace-nowrap text-right">
                                <Button size="icon-sm" variant="ghost" title={t("common.edit")} onClick={() => setItemDialog({ open: true, item: i })}>
                                  <Pencil />
                                </Button>
                                <Button size="icon-sm" variant="ghost" title={t("common.delete")} onClick={() => askRemove(i)} disabled={removeItem.isPending}>
                                  <Trash2 />
                                </Button>
                              </TD>
                            ) : null}
                          </TR>
                        ))}
                      </TBody>
                    </Table>
                    {editable ? (
                      <div className="border-t p-3">
                        <Button size="sm" variant="outline" onClick={() => setItemDialog({ open: true, item: null })}>
                          <Plus /> {t("visits.items.add")}
                        </Button>
                      </div>
                    ) : null}
                  </>
                )}
              </Card>
            </TabsContent>

            <TabsContent value="materials">
              <VisitMaterialsTab visit={v} editable={editable} />
            </TabsContent>

            <TabsContent value="payments">
              <Card>
                {v.payments.length === 0 ? (
                  <p className="p-4 text-sm text-muted-foreground">{t("visits.payments.empty")}</p>
                ) : (
                  <Table>
                    <THead>
                      <TR>
                        <TH>{t("common.date")}</TH>
                        <TH>{t("cash.payments.type")}</TH>
                        <TH>{t("cash.payments.method")}</TH>
                        <TH>{t("common.comment")}</TH>
                        <TH className="text-right">{t("common.amount")}</TH>
                      </TR>
                    </THead>
                    <TBody>
                      {v.payments.map((p) => (
                        <TR key={p.id}>
                          <TD className="whitespace-nowrap">{formatDateTime(p.createdAt)}</TD>
                          <TD>{t(`cash.types.${p.type}`)}</TD>
                          <TD>{t(`cash.methods.${p.method}`)}</TD>
                          <TD className="text-muted-foreground">{p.comment}</TD>
                          <TD className={`tabular text-right ${p.type === "Refund" ? "text-destructive" : ""}`}>{formatMoney(p.type === "Refund" ? -p.amount : p.amount)}</TD>
                        </TR>
                      ))}
                    </TBody>
                  </Table>
                )}
              </Card>
            </TabsContent>
          </Tabs>
        </div>

        <div className="space-y-4">
          <Card>
            <CardHeader>
              <CardTitle>{t("visits.patient")}</CardTitle>
            </CardHeader>
            <CardContent>
              {can(P.patientsView) ? (
                <Link className="font-medium text-primary hover:underline" href={href(`patients/${v.patientId}`)}>
                  {v.patientName}
                </Link>
              ) : (
                <div className="font-medium">{v.patientName}</div>
              )}
              {v.patientPhone ? <div className="text-sm text-muted-foreground">{formatPhone(v.patientPhone)}</div> : null}
              <Row
                label={t("visits.patientBalance")}
                value={formatMoney(v.patientBalance)}
                tone={v.patientBalance < 0 ? "text-destructive" : v.patientBalance > 0 ? "text-success" : ""}
              />
              <div className="mt-2 border-t pt-2">
                <Row label={t("visits.doctor")} value={v.doctorName} />
                <Row label={t("visits.openedAt")} value={formatDateTime(v.openedAt)} />
                {v.closedAt ? <Row label={t("visits.closedAt")} value={formatDateTime(v.closedAt)} /> : null}
              </div>
            </CardContent>
          </Card>
          <Card>
            <CardContent className="pt-4">
              <Row label={t("visits.totals.subtotal")} value={formatMoney(v.subtotal)} />
              {v.discountTotal ? <Row label={t("visits.totals.discount")} value={`− ${formatMoney(v.discountTotal)}`} /> : null}
              <Row label={t("visits.totals.total")} value={formatMoney(v.total)} strong />
              <Row label={t("visits.totals.paid")} value={formatMoney(v.paidTotal)} />
              <div className="mt-1 border-t pt-1">
                <Row label={t("visits.totals.debt")} value={formatMoney(v.debt)} strong tone={v.debt > 0 ? "text-destructive" : ""} />
              </div>
              {canPay ? (
                <Button className="mt-3 w-full" variant="success" onClick={() => setPayOpen(true)}>
                  <Wallet /> {t("visits.actions.pay")}
                </Button>
              ) : null}
            </CardContent>
          </Card>
        </div>
      </div>

      {editable ? <VisitItemDialog open={itemDialog.open} onOpenChange={(o) => setItemDialog((s) => ({ ...s, open: o }))} visit={v} item={itemDialog.item} /> : null}
      <PaymentDialog
        open={payOpen}
        onOpenChange={setPayOpen}
        patient={{ id: v.patientId, fullName: v.patientName, phone: v.patientPhone }}
        visit={{ id: v.id, openedAt: v.openedAt, debt: v.debt, branchId: v.branchId }}
        branchId={v.branchId}
      />
      <CancelVisitDialog open={cancelOpen} onOpenChange={setCancelOpen} visit={v} />
      {canCorrect ? <VisitCorrectionDialog open={correctionOpen} onOpenChange={setCorrectionOpen} visit={v} /> : null}
      {canDiscount ? <VisitDiscountDialog open={discountOpen} onOpenChange={setDiscountOpen} visit={v} /> : null}
    </div>
  );
}

function CancelVisitDialog({ open, onOpenChange, visit }: { open: boolean; onOpenChange: (o: boolean) => void; visit: Visit }) {
  const qc = useQueryClient();
  const [reason, setReason] = React.useState("");
  React.useEffect(() => {
    if (open) setReason("");
  }, [open]);
  const mutation = useMutation({
    mutationFn: () => api<Visit>(`/visits/${visit.id}/cancel`, { method: "POST", body: { reason: reason.trim() } }),
    onSuccess: (v) => {
      qc.setQueryData(["visit", visit.id], v);
      qc.invalidateQueries({ queryKey: ["patient", v.patientId] });
      qc.invalidateQueries({ queryKey: ["calendar"] });
      toast.success(t("visits.actions.cancelled"));
      onOpenChange(false);
    },
    onError: (e) => toast.error(apiErrorText(e)),
  });
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("visits.actions.cancelTitle")}</DialogTitle>
          {visit.status === "Closed" ? <DialogDescription>{t("visits.actions.cancelClosedHint")}</DialogDescription> : null}
        </DialogHeader>
        <Field label={t("common.reason")}>
          <Textarea autoFocus value={reason} onChange={(e) => setReason(e.target.value)} rows={3} maxLength={500} />
        </Field>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t("common.cancel")}
          </Button>
          <Button variant="destructive" loading={mutation.isPending} disabled={!reason.trim()} onClick={() => mutation.mutate()}>
            {t("visits.actions.cancel")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function VisitDiscountDialog({ open, onOpenChange, visit }: { open: boolean; onOpenChange: (o: boolean) => void; visit: Visit }) {
  const qc = useQueryClient();
  const { me } = useAuth();
  const [pct, setPct] = React.useState("");
  React.useEffect(() => {
    if (open) setPct("");
  }, [open]);
  const n = Number(pct.replace(",", "."));
  const valid = pct.trim() !== "" && Number.isFinite(n) && n >= 0 && n <= 100;
  const mutation = useMutation({
    mutationFn: () => api<Visit>(`/visits/${visit.id}`, { method: "PATCH", body: { assistantId: null, discountPct: n, version: visit.version } }),
    onSuccess: (v) => {
      qc.setQueryData(["visit", visit.id], v);
      if (v.approvalState === "PendingDiscount") toast.warning(t("visits.items.discountRequested"));
      else toast.success(t("common.saved"));
      onOpenChange(false);
    },
    onError: (e) => {
      toast.error(apiErrorText(e));
      qc.invalidateQueries({ queryKey: ["visit", visit.id] });
    },
  });
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("visits.items.visitDiscountTitle")}</DialogTitle>
          <DialogDescription>{t("visits.items.visitDiscountHint")}</DialogDescription>
        </DialogHeader>
        <form
          id="visit-discount-form"
          onSubmit={(e) => {
            e.preventDefault();
            if (valid) mutation.mutate();
          }}
        >
          <Field label={t("visits.items.discountPct")} hint={t("visits.items.discountLimit", { pct: me?.limits.maxDiscountPct ?? 0 })} error={pct && !valid ? t("errors.DISCOUNT_INVALID") : undefined}>
            <Input autoFocus inputMode="decimal" value={pct} onChange={(e) => setPct(e.target.value)} placeholder="0" />
          </Field>
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t("common.cancel")}
          </Button>
          <Button type="submit" form="visit-discount-form" loading={mutation.isPending} disabled={!valid}>
            {t("common.apply")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
