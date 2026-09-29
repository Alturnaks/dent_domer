"use client";

// Общие помощники для административных разделов.
import * as React from "react";
import { toast } from "sonner";
import { AlertTriangle } from "lucide-react";
import { ApiError } from "@/lib/api-client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { t } from "@/lib/i18n";
import { cn } from "@/lib/utils";

/** Сообщение об ошибке: локализованный код + первое понятное сообщение валидации. */
export function apiErrorText(e: unknown): string {
  if (e instanceof ApiError) {
    let text = e.userMessage;
    if (e.code === "VALIDATION_FAILED" && e.details) {
      const first = Object.values(e.details)
        .flatMap((v) => (Array.isArray(v) ? v : []))
        .map((x) => (x && typeof x === "object" && "message" in x ? String((x as { message: unknown }).message) : ""))
        .find((m) => /[а-яё]/i.test(m));
      if (first) text = `${text}: ${first}`;
    }
    return text;
  }
  return t("errors.INTERNAL_ERROR");
}

export function toastError(e: unknown) {
  toast.error(apiErrorText(e));
}

/** Тенге (строка ввода) → тиыны; пустая строка → null. */
export function tengeToMinorOrNull(v: string): number | null {
  const s = v.replace(/\s/g, "").replace(",", ".");
  if (s === "") return null;
  const n = Number(s);
  return Number.isFinite(n) ? Math.round(n * 100) : null;
}

/** Тиыны → строка для поля ввода в тенге. */
export function minorToInput(v: number | null | undefined): string {
  if (v === null || v === undefined) return "";
  return String(v / 100);
}

/** "09:00:00" → "09:00". */
export function hhmm(v: string | null | undefined): string {
  return (v ?? "").slice(0, 5);
}

/** "09:00" → "09:00:00". */
export function hhmmss(v: string): string {
  return v.length === 5 ? `${v}:00` : v;
}

export function ErrorBlock({ onRetry, className }: { onRetry?: () => void; className?: string }) {
  return (
    <div className={cn("flex flex-col items-center gap-2 rounded-xl border border-dashed p-8 text-center text-sm", className)}>
      <AlertTriangle className="h-6 w-6 text-destructive" />
      <p>{t("admin.loadError")}</p>
      {onRetry ? (
        <Button size="sm" variant="outline" onClick={onRetry}>
          {t("common.retry")}
        </Button>
      ) : null}
    </div>
  );
}

export function NoAccess() {
  return <div className="rounded-xl border border-dashed p-10 text-center text-sm text-muted-foreground">{t("admin.noAccess")}</div>;
}

/** Поле ввода суммы в тенге. */
export function MoneyInput({
  value,
  onChange,
  className,
  placeholder,
  ...rest
}: { value: string; onChange: (v: string) => void } & Omit<React.InputHTMLAttributes<HTMLInputElement>, "value" | "onChange">) {
  return (
    <div className={cn("relative", className)}>
      <Input
        inputMode="decimal"
        className="pr-7 text-right tabular-nums"
        value={value}
        placeholder={placeholder}
        onChange={(e) => onChange(e.target.value.replace(/[^\d.,\s-]/g, ""))}
        {...rest}
      />
      <span className="pointer-events-none absolute right-2.5 top-2 text-sm text-muted-foreground">₸</span>
    </div>
  );
}

export function useDebounced<T>(value: T, ms = 300): T {
  const [v, setV] = React.useState(value);
  React.useEffect(() => {
    const id = setTimeout(() => setV(value), ms);
    return () => clearTimeout(id);
  }, [value, ms]);
  return v;
}
