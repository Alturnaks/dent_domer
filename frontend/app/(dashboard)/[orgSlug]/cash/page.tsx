"use client";

import * as React from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { ArrowDownUp, Lock, Plus, Receipt, Undo2, Unlock, Wallet } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { EmptyState, PageHeader } from "@/components/ui/empty-state";
import { Input, NativeSelect } from "@/components/ui/input";
import { Skeleton, TableSkeleton } from "@/components/ui/skeleton";
import { Table, TBody, TD, TFoot, TH, THead, TR } from "@/components/ui/table";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { CashOperationDialog, CloseShiftDialog, ExpenseDialog, RefundDialog } from "@/components/cash/cash-dialogs";
import { OpenShiftDialog } from "@/components/cash/open-shift-form";
import { PaymentDialog } from "@/components/cash/payment-dialog";
import {
  cashKeys,
  dayRange,
  useAvailableBranches,
  useCashRegisters,
  useOpenShifts,
  type CashOperation,
  type CashShift,
  type Expense,
  type Payment,
} from "@/components/cash/shared";
import { api } from "@/lib/api-client";
import { useAuth, useBranch, useOrgHref } from "@/lib/auth";
import { formatDate, formatDateTime, formatMoney, formatTime, isoDate } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";

function Stat({ label, value, tone }: { label: string; value: number; tone?: "pos" | "neg" | "strong" }) {
  return (
    <div className="rounded-md border px-3 py-2">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className={`tabular text-base font-semibold ${tone === "neg" && value ? "text-destructive" : ""} ${tone === "strong" ? "text-lg" : ""}`}>
        {formatMoney(value)}
      </div>
    </div>
  );
}

type PaymentScope = { kind: "shift"; shiftId: string; label?: string } | { kind: "period" };

export default function CashPage() {
  const { can } = useAuth();
  const { href } = useOrgHref();
  const { branchId: currentBranch } = useBranch();
  const branches = useAvailableBranches();
  const [pickedBranch, setPickedBranch] = React.useState("");
  const branchId = currentBranch ?? (pickedBranch || branches[0]?.id || "");

  const openShifts = useOpenShifts(branchId);
  const registers = useCashRegisters(branchId, can(P.cashShift));
  const [selectedShiftId, setSelectedShiftId] = React.useState<string | null>(null);
  const shift: CashShift | null = React.useMemo(() => {
    const list = openShifts.data ?? [];
    return list.find((s) => s.id === selectedShiftId) ?? list[0] ?? null;
  }, [openShifts.data, selectedShiftId]);

  const [tab, setTab] = React.useState("payments");
  const [scope, setScope] = React.useState<PaymentScope | null>(null);
  const today = isoDate(new Date());
  const [from, setFrom] = React.useState(today);
  const [to, setTo] = React.useState(today);
  const effectiveScope: PaymentScope = scope ?? (shift ? { kind: "shift", shiftId: shift.id } : { kind: "period" });

  const [payOpen, setPayOpen] = React.useState(false);
  const [openShiftOpen, setOpenShiftOpen] = React.useState(false);
  const [closeOpen, setCloseOpen] = React.useState(false);
  const [opOpen, setOpOpen] = React.useState(false);
  const [expenseOpen, setExpenseOpen] = React.useState(false);
  const [refundFor, setRefundFor] = React.useState<Payment | null>(null);

  React.useEffect(() => {
    setScope(null);
    setSelectedShiftId(null);
  }, [branchId]);

  const range = dayRange(from, to);
  const payments = useQuery({
    queryKey: ["cash", "payments", branchId, effectiveScope.kind === "shift" ? effectiveScope.shiftId : `${from}_${to}`],
    queryFn: () =>
      api<Payment[]>("/payments", {
        query:
          effectiveScope.kind === "shift"
            ? { shift_id: effectiveScope.shiftId, limit: 500 }
            : { branch_id: branchId, from: range.from, to: range.to, limit: 500 },
      }),
    enabled: !!branchId && tab === "payments",
  });
  const operations = useQuery({
    queryKey: ["cash", "operations", shift?.id],
    queryFn: () => api<CashOperation[]>(`/cash-shifts/${shift!.id}/operations`),
    enabled: !!shift && tab === "operations",
  });
  const expenses = useQuery({
    queryKey: ["cash", "expenses", branchId, from, to],
    queryFn: () => api<Expense[]>("/expenses", { query: { branch_id: branchId, from: range.from, to: range.to } }),
    enabled: !!branchId && tab === "expenses",
  });
  const history = useQuery({
    queryKey: cashKeys.shifts(branchId),
    queryFn: () => api<CashShift[]>("/cash-shifts", { query: { branch_id: branchId, limit: 50 } }),
    enabled: !!branchId && tab === "history",
  });

  // Сколько уже возвращено по каждому платежу (для подсказки доступной суммы).
  const refundedBy = React.useMemo(() => {
    const m = new Map<string, number>();
    for (const p of payments.data ?? []) if (p.type === "Refund" && p.refundedPaymentId) m.set(p.refundedPaymentId, (m.get(p.refundedPaymentId) ?? 0) + p.amount);
    return m;
  }, [payments.data]);

  const paymentsIn = (payments.data ?? []).filter((p) => p.type !== "Refund" && !p.pendingApproval).reduce((s, p) => s + p.amount, 0);
  const paymentsOut = (payments.data ?? []).filter((p) => p.type === "Refund" && !p.pendingApproval).reduce((s, p) => s + p.amount, 0);

  const periodFilter = (
    <div className="flex flex-wrap items-center gap-2">
      <Input type="date" className="w-auto" value={from} max={to} onChange={(e) => setFrom(e.target.value || today)} />
      <span className="text-muted-foreground">—</span>
      <Input type="date" className="w-auto" value={to} min={from} onChange={(e) => setTo(e.target.value || today)} />
    </div>
  );

  if (!branchId) {
    return (
      <div>
        <PageHeader title={t("cash.title")} />
        {branches.length === 0 ? <Skeleton className="h-40 w-full" /> : <EmptyState title={t("cash.selectBranch")} />}
      </div>
    );
  }

  return (
    <div>
      <PageHeader
        title={t("cash.title")}
        description={
          !currentBranch && branches.length > 1 ? (
            <NativeSelect className="mt-1 h-8 w-auto" value={branchId} onChange={(e) => setPickedBranch(e.target.value)}>
              {branches.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.name}
                </option>
              ))}
            </NativeSelect>
          ) : undefined
        }
        actions={
          <>
            {shift && can(P.cashShift) ? (
              <Button variant="outline" onClick={() => setOpOpen(true)}>
                <ArrowDownUp /> {t("cash.operations.add")}
              </Button>
            ) : null}
            {can(P.cashExpense) ? (
              <Button variant="outline" onClick={() => setExpenseOpen(true)}>
                <Receipt /> {t("cash.expenses.add")}
              </Button>
            ) : null}
            {can(P.cashPayment) ? (
              <Button variant="success" onClick={() => setPayOpen(true)}>
                <Wallet /> {t("cash.pay.button")}
              </Button>
            ) : null}
          </>
        }
      />

      {/* Текущая смена */}
      {openShifts.isLoading ? (
        <Skeleton className="mb-4 h-36 w-full" />
      ) : shift ? (
        <Card className="mb-4">
          <CardHeader className="flex-row flex-wrap items-center justify-between gap-2 space-y-0">
            <div className="flex flex-wrap items-center gap-2">
              <CardTitle>{t("cash.shift.current")}</CardTitle>
              <Badge variant="success">{t("cash.shift.status.Open")}</Badge>
              {(openShifts.data ?? []).length > 1 ? (
                <NativeSelect className="h-8 w-auto" value={shift.id} onChange={(e) => setSelectedShiftId(e.target.value)}>
                  {(openShifts.data ?? []).map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.cashRegisterName}
                    </option>
                  ))}
                </NativeSelect>
              ) : (
                <span className="text-sm text-muted-foreground">{shift.cashRegisterName}</span>
              )}
              <span className="text-sm text-muted-foreground">
                · {t("cash.shift.openedAt")} {formatDateTime(shift.openedAt)} · {shift.openedByName}
              </span>
            </div>
            {can(P.cashShift) ? (
              <div className="flex gap-2">
                {(registers.data ?? []).length > (openShifts.data ?? []).length ? (
                  <Button size="sm" variant="outline" onClick={() => setOpenShiftOpen(true)} title={t("cash.shift.open")}>
                    <Plus />
                  </Button>
                ) : null}
                <Button size="sm" onClick={() => setCloseOpen(true)}>
                  <Lock /> {t("cash.shift.close")}
                </Button>
              </div>
            ) : null}
          </CardHeader>
          <CardContent className="grid grid-cols-2 gap-2 sm:grid-cols-4 lg:grid-cols-[repeat(auto-fit,minmax(8.5rem,1fr))]">
            <Stat label={t("cash.shift.opening")} value={shift.openingBalance} />
            <Stat label={t("cash.shift.cashIn")} value={shift.cashIn} />
            <Stat label={t("cash.shift.cardIn")} value={shift.cardIn} />
            <Stat label={t("cash.shift.otherIn")} value={shift.otherIn} />
            <Stat label={t("cash.shift.refunds")} value={shift.refunds} tone="neg" />
            <Stat label={t("cash.shift.expenses")} value={shift.expenses} tone="neg" />
            <Stat label={t("cash.shift.collections")} value={shift.collections} tone="neg" />
            {shift.deposits ? <Stat label={t("cash.shift.deposits")} value={shift.deposits} /> : null}
            <div className="rounded-md border border-primary/40 bg-primary/5 px-3 py-2">
              <div className="text-xs text-muted-foreground">{t("cash.shift.expectedNow")}</div>
              <div className="tabular text-lg font-semibold">{formatMoney(shift.expectedNow)}</div>
            </div>
          </CardContent>
        </Card>
      ) : (
        <EmptyState
          className="mb-4 p-6"
          icon={<Unlock className="h-8 w-8" />}
          title={t("cash.shift.noOpen")}
          description={t("cash.shift.noOpenHint")}
          action={
            can(P.cashShift) ? (
              <Button onClick={() => setOpenShiftOpen(true)}>
                <Unlock /> {t("cash.shift.open")}
              </Button>
            ) : undefined
          }
        />
      )}

      <Tabs value={tab} onValueChange={setTab}>
        <TabsList>
          {(["payments", "operations", "expenses", "history"] as const).map((k) => (
            <TabsTrigger key={k} value={k}>
              {t(`cash.tabs.${k}`)}
            </TabsTrigger>
          ))}
        </TabsList>

        {/* Платежи */}
        <TabsContent value="payments">
          <div className="mb-3 flex flex-wrap items-center gap-2">
            <NativeSelect
              className="h-9 w-auto"
              value={effectiveScope.kind === "shift" ? effectiveScope.shiftId : "period"}
              onChange={(e) => setScope(e.target.value === "period" ? { kind: "period" } : { kind: "shift", shiftId: e.target.value })}
            >
              {shift ? <option value={shift.id}>{t("cash.payments.scopeShift")}</option> : null}
              {effectiveScope.kind === "shift" && effectiveScope.shiftId !== shift?.id ? (
                <option value={effectiveScope.shiftId}>{effectiveScope.label ?? effectiveScope.shiftId}</option>
              ) : null}
              <option value="period">{t("cash.payments.scopePeriod")}</option>
            </NativeSelect>
            {effectiveScope.kind === "period" ? periodFilter : null}
          </div>
          <Card>
            {payments.isLoading ? (
              <TableSkeleton />
            ) : payments.isError ? (
              <EmptyState className="m-3" title={t("errors.INTERNAL_ERROR")} action={<Button variant="outline" onClick={() => payments.refetch()}>{t("common.retry")}</Button>} />
            ) : (payments.data ?? []).length === 0 ? (
              <p className="p-4 text-sm text-muted-foreground">{t("cash.payments.empty")}</p>
            ) : (
              <Table>
                <THead>
                  <TR>
                    <TH>{t("cash.payments.time")}</TH>
                    <TH>{t("cash.payments.patient")}</TH>
                    <TH>{t("cash.payments.type")}</TH>
                    <TH>{t("cash.payments.method")}</TH>
                    <TH className="text-right">{t("cash.payments.amount")}</TH>
                    <TH>{t("cash.payments.cashier")}</TH>
                    <TH>{t("common.comment")}</TH>
                    <TH />
                  </TR>
                </THead>
                <TBody>
                  {(payments.data ?? []).map((p) => {
                    const refund = p.type === "Refund";
                    const refundable = !refund && p.method !== "Balance" && p.amount - (refundedBy.get(p.id) ?? 0) > 0;
                    return (
                      <TR key={p.id} className={p.pendingApproval ? "opacity-70" : undefined}>
                        <TD className="whitespace-nowrap tabular">
                          {effectiveScope.kind === "period" && from !== to ? formatDateTime(p.createdAt) : formatTime(p.createdAt)}
                        </TD>
                        <TD>
                          <Link className="hover:underline" href={href(`patients/${p.patientId}`)}>
                            {p.patientName}
                          </Link>
                          {p.visitId ? (
                            <Link className="ml-2 text-xs text-primary hover:underline" href={href(`visits/${p.visitId}`)}>
                              {t("cash.payments.visit")}
                            </Link>
                          ) : null}
                        </TD>
                        <TD>
                          <span className="flex items-center gap-1">
                            <Badge variant={refund ? "destructive" : p.type === "Advance" ? "secondary" : "muted"}>{t(`cash.types.${p.type}`)}</Badge>
                            {p.pendingApproval ? <Badge variant="warning">{t("cash.payments.pending")}</Badge> : null}
                          </span>
                        </TD>
                        <TD>{t(`cash.methods.${p.method}`)}</TD>
                        <TD className={`tabular whitespace-nowrap text-right font-medium ${refund ? "text-destructive" : ""}`}>
                          {formatMoney(refund ? -p.amount : p.amount)}
                        </TD>
                        <TD className="text-muted-foreground">{p.createdByName}</TD>
                        <TD className="max-w-[16rem] truncate text-muted-foreground" title={p.comment ?? undefined}>
                          {p.comment}
                        </TD>
                        <TD className="text-right">
                          {refundable && shift && can(P.cashRefund) ? (
                            <Button size="sm" variant="ghost" onClick={() => setRefundFor(p)}>
                              <Undo2 /> {t("cash.refund.button")}
                            </Button>
                          ) : null}
                        </TD>
                      </TR>
                    );
                  })}
                </TBody>
                <TFoot>
                  <TR>
                    <TD colSpan={8} className="text-right text-sm">
                      {t("cash.payments.total", { in: formatMoney(paymentsIn), out: formatMoney(paymentsOut) })}
                    </TD>
                  </TR>
                </TFoot>
              </Table>
            )}
          </Card>
        </TabsContent>

        {/* Операции */}
        <TabsContent value="operations">
          <Card>
            {!shift ? (
              <p className="p-4 text-sm text-muted-foreground">{t("cash.operations.noShift")}</p>
            ) : operations.isLoading ? (
              <TableSkeleton rows={3} cols={4} />
            ) : (operations.data ?? []).length === 0 ? (
              <p className="p-4 text-sm text-muted-foreground">{t("cash.operations.empty")}</p>
            ) : (
              <Table>
                <THead>
                  <TR>
                    <TH>{t("cash.payments.time")}</TH>
                    <TH>{t("cash.operations.type")}</TH>
                    <TH className="text-right">{t("cash.operations.amount")}</TH>
                    <TH>{t("common.comment")}</TH>
                  </TR>
                </THead>
                <TBody>
                  {(operations.data ?? []).map((o) => (
                    <TR key={o.id}>
                      <TD className="tabular">{formatTime(o.createdAt)}</TD>
                      <TD>{t(`cash.operations.types.${o.type}`)}</TD>
                      <TD className={`tabular text-right ${o.type === "Collection" ? "text-destructive" : "text-success"}`}>
                        {formatMoney(o.type === "Collection" ? -o.amount : o.amount)}
                      </TD>
                      <TD className="text-muted-foreground">{o.comment}</TD>
                    </TR>
                  ))}
                </TBody>
              </Table>
            )}
          </Card>
        </TabsContent>

        {/* Расходы */}
        <TabsContent value="expenses">
          <div className="mb-3">{periodFilter}</div>
          <Card>
            {expenses.isLoading ? (
              <TableSkeleton rows={4} />
            ) : (expenses.data ?? []).length === 0 ? (
              <p className="p-4 text-sm text-muted-foreground">{t("cash.expenses.empty")}</p>
            ) : (
              <Table>
                <THead>
                  <TR>
                    <TH>{t("common.date")}</TH>
                    <TH>{t("cash.expenses.category")}</TH>
                    <TH>{t("cash.expenses.counterparty")}</TH>
                    <TH>{t("common.comment")}</TH>
                    <TH />
                    <TH className="text-right">{t("common.amount")}</TH>
                  </TR>
                </THead>
                <TBody>
                  {(expenses.data ?? []).map((x) => (
                    <TR key={x.id}>
                      <TD className="whitespace-nowrap">{formatDateTime(x.paidAt)}</TD>
                      <TD>{x.categoryName}</TD>
                      <TD>{x.counterparty}</TD>
                      <TD className="text-muted-foreground">
                        {x.comment}
                        {x.documentUrl ? (
                          <a className="ml-2 text-primary hover:underline" href={x.documentUrl} target="_blank" rel="noreferrer">
                            ↗
                          </a>
                        ) : null}
                      </TD>
                      <TD>{x.cashShiftId ? <Badge variant="muted">{t("cash.expenses.fromCashLabel")}</Badge> : null}</TD>
                      <TD className="tabular text-right">{formatMoney(x.amount)}</TD>
                    </TR>
                  ))}
                </TBody>
                <TFoot>
                  <TR>
                    <TD colSpan={6} className="text-right text-sm">
                      {t("cash.expenses.total", { amount: formatMoney((expenses.data ?? []).reduce((s, x) => s + x.amount, 0)) })}
                    </TD>
                  </TR>
                </TFoot>
              </Table>
            )}
          </Card>
        </TabsContent>

        {/* История смен */}
        <TabsContent value="history">
          <Card>
            {history.isLoading ? (
              <TableSkeleton />
            ) : (history.data ?? []).length === 0 ? (
              <p className="p-4 text-sm text-muted-foreground">{t("cash.history.empty")}</p>
            ) : (
              <Table>
                <THead>
                  <TR>
                    <TH>{t("cash.history.openedAt")}</TH>
                    <TH>{t("cash.history.closedAt")}</TH>
                    <TH>{t("cash.register")}</TH>
                    <TH>{t("common.status")}</TH>
                    <TH className="text-right">{t("cash.shift.opening")}</TH>
                    <TH className="text-right">{t("cash.shift.expected")}</TH>
                    <TH className="text-right">{t("cash.shift.actualShort")}</TH>
                    <TH className="text-right">{t("cash.shift.difference")}</TH>
                    <TH />
                  </TR>
                </THead>
                <TBody>
                  {(history.data ?? []).map((s) => (
                    <TR key={s.id}>
                      <TD className="whitespace-nowrap">
                        {formatDateTime(s.openedAt)}
                        <div className="text-xs text-muted-foreground">{s.openedByName}</div>
                      </TD>
                      <TD className="whitespace-nowrap">
                        {s.closedAt ? formatDateTime(s.closedAt) : "—"}
                        {s.closedByName ? <div className="text-xs text-muted-foreground">{s.closedByName}</div> : null}
                      </TD>
                      <TD>{s.cashRegisterName}</TD>
                      <TD>
                        <Badge variant={s.status === "Open" ? "success" : "muted"}>{t(`cash.shift.status.${s.status}`)}</Badge>
                      </TD>
                      <TD className="tabular text-right">{formatMoney(s.openingBalance)}</TD>
                      <TD className="tabular text-right">{formatMoney(s.status === "Open" ? s.expectedNow : s.closingBalanceExpected)}</TD>
                      <TD className="tabular text-right">{formatMoney(s.closingBalanceActual)}</TD>
                      <TD className={`tabular text-right font-medium ${s.difference ? "text-destructive" : ""}`}>{s.difference === null ? "—" : formatMoney(s.difference)}</TD>
                      <TD className="text-right">
                        <Button
                          size="sm"
                          variant="ghost"
                          onClick={() => {
                            setScope({ kind: "shift", shiftId: s.id, label: t("cash.payments.scopeSelectedShift", { date: formatDate(s.openedAt) }) });
                            setTab("payments");
                          }}
                        >
                          {t("cash.history.viewPayments")}
                        </Button>
                      </TD>
                    </TR>
                  ))}
                </TBody>
              </Table>
            )}
          </Card>
        </TabsContent>
      </Tabs>

      <PaymentDialog open={payOpen} onOpenChange={setPayOpen} branchId={branchId} />
      <OpenShiftDialog open={openShiftOpen} onOpenChange={setOpenShiftOpen} branchId={branchId} />
      {shift ? <CloseShiftDialog open={closeOpen} onOpenChange={setCloseOpen} shift={shift} /> : null}
      {shift ? <CashOperationDialog open={opOpen} onOpenChange={setOpOpen} shift={shift} /> : null}
      <ExpenseDialog open={expenseOpen} onOpenChange={setExpenseOpen} branchId={branchId} hasOpenShift={!!shift} />
      <RefundDialog
        open={refundFor !== null}
        onOpenChange={(o) => !o && setRefundFor(null)}
        payment={refundFor}
        refunded={refundFor ? (refundedBy.get(refundFor.id) ?? 0) : 0}
      />
    </div>
  );
}
