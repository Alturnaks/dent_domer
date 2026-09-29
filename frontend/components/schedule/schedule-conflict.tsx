"use client";

// Согласованность расписания: если изменение (выходной, график, блокировка, увольнение, кресло, филиал)
// задевает записи пациентов, API отвечает 409 SCHEDULE_HAS_APPOINTMENTS со списком записей.
// Этот провайдер показывает список и повторяет запрос с выбранным решением.

import * as React from "react";
import { AlertTriangle } from "lucide-react";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth";
import { formatDate, formatPhone, formatTime } from "@/lib/format";
import { t } from "@/lib/i18n";
import type { Schemas } from "@/lib/types";

export type ConflictAction = NonNullable<Schemas["AffectedAppointmentsAction"]>;
/** Элемент details.appointments ошибки SCHEDULE_HAS_APPOINTMENTS (в OpenAPI не описан — это тело ошибки). */
type Affected = {
  id: string;
  branchId: string;
  startsAt: string;
  endsAt: string;
  doctorId: string;
  doctorName: string;
  patientId: string;
  patientName: string;
  patientPhone: string | null;
  reason: string;
};
type Runner = <T>(fn: (onConflict?: ConflictAction) => Promise<T>) => Promise<T>;

const Ctx = React.createContext<Runner>((fn) => fn());

type Pending = { items: Affected[]; message: string; resolve: (a: ConflictAction | null) => void };

export function ScheduleConflictProvider({ children }: { children: React.ReactNode }) {
  const [pending, setPending] = React.useState<Pending | null>(null);

  const run = React.useCallback<Runner>(async (fn) => {
    try {
      return await fn(undefined);
    } catch (e) {
      if (!(e instanceof ApiError) || e.code !== "SCHEDULE_HAS_APPOINTMENTS") throw e;
      const items = ((e.details?.appointments as Affected[] | undefined) ?? []).slice();
      const action = await new Promise<ConflictAction | null>((resolve) => setPending({ items, message: e.message, resolve }));
      if (action === null) throw new ApiError(409, "CHANGE_NOT_SAVED", t("scheduleConflict.notSaved"));
      return await fn(action);
    }
  }, []);

  const close = (a: ConflictAction | null) => {
    pending?.resolve(a);
    setPending(null);
  };

  return (
    <Ctx.Provider value={run}>
      {children}
      <Dialog open={pending !== null} onOpenChange={(o) => !o && close(null)}>
        <DialogContent wide>
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <AlertTriangle className="h-5 w-5 text-warning" />
              {t("scheduleConflict.title", { count: pending?.items.length ?? 0 })}
            </DialogTitle>
            <DialogDescription>{t("scheduleConflict.description")}</DialogDescription>
          </DialogHeader>
          {pending ? <AffectedTable items={pending.items} /> : null}
          <div className="space-y-1 rounded-lg bg-muted/50 p-3 text-xs text-muted-foreground">
            <p>
              <b>{t("scheduleConflict.cancelTitle")}</b> — {t("scheduleConflict.cancelHint")}
            </p>
            <p>
              <b>{t("scheduleConflict.keepTitle")}</b> — {t("scheduleConflict.keepHint")}
            </p>
          </div>
          <DialogFooter className="flex-wrap gap-2">
            <Button variant="outline" onClick={() => close(null)}>
              {t("scheduleConflict.abort")}
            </Button>
            <Button variant="secondary" onClick={() => close("Keep")}>
              {t("scheduleConflict.keepTitle")}
            </Button>
            <Button variant="destructive" onClick={() => close("CancelToWaitlist")}>
              {t("scheduleConflict.cancelTitle")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </Ctx.Provider>
  );
}

function AffectedTable({ items }: { items: Affected[] }) {
  const { me } = useAuth();
  const tz = me?.organization.timezone;
  const shown = items.slice(0, 50);
  return (
    <div className="max-h-80 overflow-y-auto rounded-lg border">
      <Table>
        <THead>
          <TR>
            <TH>{t("scheduleConflict.when")}</TH>
            <TH>{t("scheduleConflict.patient")}</TH>
            <TH>{t("scheduleConflict.doctor")}</TH>
            <TH>{t("scheduleConflict.reason")}</TH>
          </TR>
        </THead>
        <TBody>
          {shown.map((a) => (
            <TR key={a.id}>
              <TD className="whitespace-nowrap tabular">
                {formatDate(a.startsAt, tz)} {formatTime(a.startsAt, tz)}–{formatTime(a.endsAt, tz)}
              </TD>
              <TD>
                <div className="font-medium">{a.patientName}</div>
                <div className="text-xs text-muted-foreground">{formatPhone(a.patientPhone)}</div>
              </TD>
              <TD className="text-sm">{a.doctorName}</TD>
              <TD className="text-sm text-muted-foreground">{a.reason}</TD>
            </TR>
          ))}
        </TBody>
      </Table>
      {items.length > shown.length ? (
        <p className="p-2 text-center text-xs text-muted-foreground">{t("scheduleConflict.more", { count: items.length - shown.length })}</p>
      ) : null}
    </div>
  );
}

/** const run = useScheduleConflict(); await run((onConflict) => api(..., { body: { ...body, onConflict } })) */
export function useScheduleConflict() {
  return React.useContext(Ctx);
}
