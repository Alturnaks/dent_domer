"use client";

// Общие типы и запросы кассы. Типы — только из сгенерированной схемы.
import * as React from "react";
import { useQuery } from "@tanstack/react-query";
import { api, ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth";
import { useBranches } from "@/lib/queries";
import { t } from "@/lib/i18n";
import type { Schemas } from "@/lib/types";

export type CashShift = Schemas["CashShiftDto"];
export type CashRegister = Schemas["CashRegisterDto"];
export type CashOperation = Schemas["CashOperationDto"];
export type Payment = Schemas["PaymentDto"];
export type PaymentMethod = Schemas["PaymentMethod"];
export type Expense = Schemas["ExpenseDto"];
export type PatientBalance = Schemas["PatientBalanceDto"];

export const PAYMENT_METHODS: PaymentMethod[] = ["Cash", "Card", "KaspiQr", "Transfer", "Insurance", "Balance"];

export const cashKeys = {
  openShifts: (branchId: string | null | undefined) => ["cash", "shifts", "open", branchId ?? "none"] as const,
  shifts: (branchId: string | null | undefined) => ["cash", "shifts", "all", branchId ?? "none"] as const,
  registers: (branchId: string | null | undefined) => ["cash", "registers", branchId ?? "none"] as const,
};

/** Открытые смены филиала. */
export function useOpenShifts(branchId: string | null | undefined, enabled = true) {
  return useQuery({
    queryKey: cashKeys.openShifts(branchId),
    queryFn: () => api<CashShift[]>("/cash-shifts", { query: { branch_id: branchId, status: "Open" } }),
    enabled: enabled && !!branchId,
  });
}

export function useCashRegisters(branchId: string | null | undefined, enabled = true) {
  return useQuery({
    queryKey: cashKeys.registers(branchId),
    queryFn: () => api<CashRegister[]>("/cash-registers", { query: { branch_id: branchId } }),
    enabled: enabled && !!branchId,
    staleTime: 5 * 60_000,
  });
}

/** Филиалы, доступные пользователю (для владельца с доступом ко всем — полный список). */
export function useAvailableBranches() {
  const { me } = useAuth();
  const all = useBranches();
  const own = me?.branches ?? [];
  if (me?.allBranches && all.data) return all.data.map((b) => ({ id: b.id, name: b.name }));
  return own.map((b) => ({ id: b.id, name: b.name }));
}

/** Сообщение об ошибке API для тоста. */
export function apiErrorText(e: unknown): string {
  return e instanceof ApiError ? e.userMessage : t("errors.INTERNAL_ERROR");
}

/** Разбор суммы в тенге из поля ввода → тиыны (NaN → 0). */
export function parseMoneyInput(value: string): number {
  const n = Number(value.replace(/\s/g, "").replace(",", "."));
  return Number.isFinite(n) ? Math.round(n * 100) : 0;
}

/** Тиыны → строка для поля ввода. */
export function moneyInputValue(minor: number | null | undefined): string {
  if (!minor) return "";
  const v = minor / 100;
  return Number.isInteger(v) ? String(v) : v.toFixed(2);
}

/** Границы локальных дней [from, to) в ISO для фильтров, где to — исключительно. */
export function dayRange(fromDate: string, toDate: string): { from: string; to: string } {
  const from = new Date(`${fromDate}T00:00:00`);
  const to = new Date(`${toDate}T00:00:00`);
  to.setDate(to.getDate() + 1);
  return { from: from.toISOString(), to: to.toISOString() };
}

/** Поле ввода суммы в тенге. */
export const MoneyInput = React.forwardRef<HTMLInputElement, React.InputHTMLAttributes<HTMLInputElement>>(({ className, ...props }, ref) => (
  <input
    ref={ref}
    inputMode="decimal"
    autoComplete="off"
    className={
      "flex h-9 w-full rounded-md border border-input bg-card px-3 py-1 text-right text-sm tabular shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50 aria-[invalid=true]:border-destructive " +
      (className ?? "")
    }
    {...props}
  />
));
MoneyInput.displayName = "MoneyInput";
