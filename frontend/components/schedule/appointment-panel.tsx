"use client";

import * as React from "react";
import Link from "next/link";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  ArrowRightLeft,
  Bell,
  CheckCircle2,
  ClipboardList,
  DoorOpen,
  MessageCircle,
  Pencil,
  Star,
  UserRound,
  UserX,
  XCircle,
  Armchair,
  Flag,
} from "lucide-react";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, DialogDescription } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect, Textarea } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { useConfirm } from "@/components/ui/confirm";
import { api, ApiError } from "@/lib/api-client";
import { useAuth, useOrgHref } from "@/lib/auth";
import { useChairs, useReference, useServices } from "@/lib/queries";
import { formatDateTime, formatMoney, formatPhone, whatsappLink } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import { STATUS_COLORS } from "@/lib/status";
import type { Appointment, AppointmentStatus, Schemas } from "@/lib/types";
import { ServicePicker } from "./service-picker";
import { hhmm, parseHhmm, toIso, zoned } from "./tz";

export function StatusBadge({ status }: { status: AppointmentStatus }) {
  const c = STATUS_COLORS[status];
  return (
    <span className="inline-flex items-center rounded-full border px-2 py-0.5 text-xs font-medium" style={{ background: c.bg, borderColor: c.border, color: c.text }}>
      {t(`schedule.status.${status}`)}
    </span>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex justify-between gap-3 py-1 text-sm">
      <span className="shrink-0 text-muted-foreground">{label}</span>
      <span className="min-w-0 text-right">{children ?? "—"}</span>
    </div>
  );
}

/** Guid.Empty — «снять кресло» (null в API означает «не менять»). */
const EMPTY_GUID = "00000000-0000-0000-0000-000000000000";

function errText(e: unknown) {
  return e instanceof ApiError ? e.userMessage : t("errors.INTERNAL_ERROR");
}

export function AppointmentPanel({
  appointment,
  onOpenChange,
  tz,
  doctors,
}: {
  appointment: Appointment | null;
  onOpenChange: (o: boolean) => void;
  tz: string;
  doctors: { id: string; title: string }[];
}) {
  const { can } = useAuth();
  const { href, push } = useOrgHref();
  const qc = useQueryClient();
  const confirm = useConfirm();
  const [a, setA] = React.useState<Appointment | null>(appointment);
  const [mode, setMode] = React.useState<"view" | "cancel" | "move" | "edit">("view");

  React.useEffect(() => {
    setA(appointment);
    setMode("view");
  }, [appointment]);

  const onUpdated = (next: Appointment, msg?: string) => {
    setA(next);
    setMode("view");
    qc.invalidateQueries({ queryKey: ["calendar"] });
    qc.invalidateQueries({ queryKey: ["patient", next.patientId] });
    if (msg) toast.success(msg);
  };
  const onError = (e: unknown) => {
    toast.error(errText(e));
    if (e instanceof ApiError && e.code === "CONCURRENCY_CONFLICT" && a) {
      void api<Appointment>(`/appointments/${a.id}`).then(setA);
      qc.invalidateQueries({ queryKey: ["calendar"] });
    }
  };

  const status = useMutation({
    mutationFn: (s: AppointmentStatus) =>
      api<Appointment>(`/appointments/${a!.id}/status`, { method: "POST", body: { status: s, reasonId: null, comment: null, version: a!.version } }),
    onSuccess: (x) => onUpdated(x, t("schedule.updated")),
    onError,
  });
  const arrive = useMutation({
    mutationFn: () => api<Schemas["VisitDto"]>(`/appointments/${a!.id}/arrive`, { method: "POST" }),
    onSuccess: (v) => {
      qc.invalidateQueries({ queryKey: ["calendar"] });
      toast.success(t("schedule.visitOpened"));
      onOpenChange(false);
      push(`visits/${v.id}`);
    },
    onError,
  });
  const remind = useMutation({
    mutationFn: () => api<{ sent: boolean }>(`/appointments/${a!.id}/remind`, { method: "POST" }),
    onSuccess: (r) => {
      if (r.sent) toast.success(t("schedule.reminderSent"));
      else toast.warning(t("schedule.reminderFailed"));
      // Отправка напоминания меняет версию записи — подтягиваем свежую.
      void api<Appointment>(`/appointments/${a!.id}`).then(setA).catch(() => undefined);
      qc.invalidateQueries({ queryKey: ["calendar"] });
    },
    onError,
  });

  if (!a) return null;
  const manage = can(P.scheduleManage);
  const canStatus = can(P.scheduleManage, P.visitsComplete);
  const canArrive = can(P.scheduleManage, P.visitsEditOpen);
  const s = a.status;
  const pending = status.isPending || arrive.isPending || remind.isPending;
  const start = zoned(a.startsAt, tz);
  const end = zoned(a.endsAt, tz);
  const wa = whatsappLink(a.patientPhone);

  return (
    <Dialog open={!!appointment} onOpenChange={onOpenChange}>
      <DialogContent
        className="left-auto right-0 top-0 h-full max-h-screen w-full max-w-md translate-x-0 translate-y-0 content-start rounded-none border-y-0 border-r-0 sm:w-[28rem]"
        data-testid="appointment-panel"
      >
        <DialogHeader className="pr-6">
          <DialogTitle className="flex items-center gap-1.5">
            {a.patientIsVip ? <Star className="h-4 w-4 fill-warning text-warning" /> : null}
            {a.patientName}
          </DialogTitle>
          <DialogDescription asChild>
            <div className="flex flex-wrap items-center gap-2">
              <StatusBadge status={s} />
              <span className="tabular-nums">
                {start.date.split("-").reverse().join(".")} · {hhmm(start.minutes)}–{hhmm(end.minutes)}
              </span>
            </div>
          </DialogDescription>
        </DialogHeader>

        {mode === "view" ? (
          <>
            <div className="divide-y rounded-lg border px-3">
              <Row label={t("schedule.doctor")}>{a.doctorName}</Row>
              <Row label={t("schedule.chair")}>{a.chairName ?? "—"}</Row>
              <Row label={t("patients.phone")}>
                <span className="inline-flex items-center gap-2">
                  {formatPhone(a.patientPhone)}
                  {wa ? (
                    <a href={wa} target="_blank" rel="noreferrer" className="text-success hover:underline" title={t("schedule.actions.write")}>
                      <MessageCircle className="h-4 w-4" />
                    </a>
                  ) : null}
                </span>
              </Row>
              <Row label={t("schedule.balance")}>
                <span className={a.patientBalance < 0 ? "font-medium text-destructive" : a.patientBalance > 0 ? "text-success" : ""}>
                  {a.patientBalance === 0 ? "0" : formatMoney(a.patientBalance)}
                </span>
              </Row>
              <Row label={t("schedule.source")}>{t(`schedule.sources.${a.source}`)}</Row>
              {a.confirmedAt ? (
                <Row label={t("schedule.confirmedAt")}>
                  {formatDateTime(a.confirmedAt, tz)} {a.confirmedVia ? `· ${a.confirmedVia}` : ""}
                </Row>
              ) : null}
              {s === "Cancelled" && a.cancelComment ? <Row label={t("schedule.cancelReason")}>{a.cancelComment}</Row> : null}
            </div>

            <div>
              <p className="mb-1 text-xs font-medium uppercase tracking-wide text-muted-foreground">{t("schedule.services")}</p>
              {a.services.length ? (
                <ul className="space-y-1 text-sm">
                  {a.services.map((x) => (
                    <li key={x.serviceId} className="flex justify-between gap-2">
                      <span>
                        {x.name}
                        {x.qty > 1 ? ` ×${x.qty}` : ""} <span className="text-xs text-muted-foreground">· {x.durationMin} {t("schedule.min")}</span>
                      </span>
                      <span className="tabular-nums">{formatMoney(x.plannedPrice * x.qty)}</span>
                    </li>
                  ))}
                  <li className="flex justify-between gap-2 border-t pt-1 font-medium">
                    <span>{t("schedule.planned")}</span>
                    <span className="tabular-nums">{formatMoney(a.plannedTotal)}</span>
                  </li>
                </ul>
              ) : (
                <p className="text-sm text-muted-foreground">{t("schedule.noServices")}</p>
              )}
            </div>

            {a.comment ? (
              <div className="rounded-lg bg-muted/50 p-2 text-sm">
                <span className="text-xs text-muted-foreground">{t("schedule.comment")}: </span>
                {a.comment}
              </div>
            ) : null}

            <div className="flex flex-wrap gap-2">
              {canArrive && (s === "Scheduled" || s === "Confirmed" || s === "NoShow") ? (
                <Button variant="success" loading={arrive.isPending} disabled={pending} onClick={() => arrive.mutate()} data-testid="arrive-btn">
                  <DoorOpen /> {t("schedule.actions.arrived")}
                </Button>
              ) : null}
              {a.visitId ? (
                <Button asChild>
                  <Link href={href(`visits/${a.visitId}`)}>
                    <ClipboardList /> {t("schedule.actions.openVisit")}
                  </Link>
                </Button>
              ) : null}
              {manage && s === "Scheduled" ? (
                <Button variant="outline" disabled={pending} onClick={() => status.mutate("Confirmed")}>
                  <CheckCircle2 /> {t("schedule.actions.confirm")}
                </Button>
              ) : null}
              {canStatus && s === "Arrived" ? (
                <Button variant="outline" disabled={pending} onClick={() => status.mutate("InChair")}>
                  <Armchair /> {t("schedule.actions.inChair")}
                </Button>
              ) : null}
              {canStatus && (s === "Arrived" || s === "InChair") ? (
                <Button
                  variant="outline"
                  disabled={pending}
                  onClick={async () => {
                    if (await confirm({ title: t("schedule.completeConfirm"), description: t("schedule.completeHint") })) status.mutate("Completed");
                  }}
                >
                  <Flag /> {t("schedule.actions.complete")}
                </Button>
              ) : null}
            </div>

            <div className="flex flex-wrap gap-2 border-t pt-3">
              {manage && (s === "Scheduled" || s === "Confirmed") ? (
                <>
                  <Button size="sm" variant="outline" onClick={() => setMode("move")}>
                    <ArrowRightLeft /> {t("schedule.actions.move")}
                  </Button>
                  <Button size="sm" variant="outline" onClick={() => setMode("edit")}>
                    <Pencil /> {t("common.edit")}
                  </Button>
                  <Button size="sm" variant="outline" disabled={pending || !a.patientPhone} loading={remind.isPending} onClick={() => remind.mutate()}>
                    <Bell /> {t("schedule.actions.remind")}
                  </Button>
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={pending}
                    onClick={async () => {
                      if (await confirm({ title: t("schedule.noShowConfirm"), confirmText: t("schedule.actions.noShow"), destructive: true })) status.mutate("NoShow");
                    }}
                  >
                    <UserX /> {t("schedule.actions.noShow")}
                  </Button>
                  <Button size="sm" variant="outline" className="text-destructive" onClick={() => setMode("cancel")}>
                    <XCircle /> {t("schedule.actions.cancel")}
                  </Button>
                </>
              ) : null}
              {can(P.patientsView) ? (
                <Button size="sm" variant="ghost" asChild>
                  <Link href={href(`patients/${a.patientId}`)}>
                    <UserRound /> {t("schedule.actions.openPatient")}
                  </Link>
                </Button>
              ) : null}
            </div>
          </>
        ) : mode === "cancel" ? (
          <CancelForm a={a} onDone={(x) => onUpdated(x, t("schedule.cancelled"))} onBack={() => setMode("view")} onError={onError} />
        ) : mode === "move" ? (
          <MoveForm a={a} tz={tz} doctors={doctors} onDone={(x) => onUpdated(x, t("schedule.moved"))} onBack={() => setMode("view")} onError={onError} />
        ) : (
          <EditForm a={a} onDone={(x) => onUpdated(x, t("schedule.updated"))} onBack={() => setMode("view")} onError={onError} />
        )}
      </DialogContent>
    </Dialog>
  );
}

function CancelForm({ a, onDone, onBack, onError }: { a: Appointment; onDone: (a: Appointment) => void; onBack: () => void; onError: (e: unknown) => void }) {
  const reasons = useReference("/cancel-reasons");
  const list = (reasons.data ?? []).filter((r) => !r.type || r.type === "Cancel");
  const [reasonId, setReasonId] = React.useState("");
  const [comment, setComment] = React.useState("");
  const m = useMutation({
    mutationFn: () =>
      api<Appointment>(`/appointments/${a.id}/status`, { method: "POST", body: { status: "Cancelled", reasonId, comment: comment.trim() || null, version: a.version } }),
    onSuccess: onDone,
    onError,
  });
  return (
    <form
      className="space-y-3"
      onSubmit={(e) => {
        e.preventDefault();
        if (reasonId) m.mutate();
      }}
    >
      <h3 className="text-sm font-semibold">{t("schedule.cancelTitle")}</h3>
      <Field label={t("schedule.cancelReason")}>
        <NativeSelect value={reasonId} onChange={(e) => setReasonId(e.target.value)} required data-testid="cancel-reason">
          <option value="" disabled>
            —
          </option>
          {list.map((r) => (
            <option key={r.id} value={r.id}>
              {r.name}
            </option>
          ))}
        </NativeSelect>
      </Field>
      <Field label={t("schedule.comment")}>
        <Textarea rows={2} value={comment} onChange={(e) => setComment(e.target.value)} />
      </Field>
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onBack}>
          {t("common.back")}
        </Button>
        <Button type="submit" variant="destructive" disabled={!reasonId} loading={m.isPending}>
          {t("schedule.actions.cancel")}
        </Button>
      </DialogFooter>
    </form>
  );
}

function MoveForm({
  a,
  tz,
  doctors,
  onDone,
  onBack,
  onError,
}: {
  a: Appointment;
  tz: string;
  doctors: { id: string; title: string }[];
  onDone: (a: Appointment) => void;
  onBack: () => void;
  onError: (e: unknown) => void;
}) {
  const chairs = useChairs(a.branchId);
  const reasons = useReference("/cancel-reasons");
  const list = (reasons.data ?? []).filter((r) => r.type === "Reschedule");
  const s = zoned(a.startsAt, tz);
  const e = zoned(a.endsAt, tz);
  const dur = Math.max(5, (e.date > s.date ? 1440 : e.minutes) - s.minutes);
  const [date, setDate] = React.useState(s.date);
  const [time, setTime] = React.useState(hhmm(s.minutes));
  const [duration, setDuration] = React.useState(dur);
  const [doctorId, setDoctorId] = React.useState(a.doctorId);
  const [chairId, setChairId] = React.useState(a.chairId ?? "");
  const [reasonId, setReasonId] = React.useState("");
  const [problem, setProblem] = React.useState<string | null>(null);
  const start = parseHhmm(time);

  const m = useMutation({
    mutationFn: () =>
      api<Appointment>(`/appointments/${a.id}/move`, {
        method: "POST",
        body: {
          startsAt: toIso(date, start!, tz),
          endsAt: toIso(date, start! + duration, tz),
          doctorId,
          chairId: chairId || EMPTY_GUID,
          reasonId: reasonId || null,
          force: null,
          version: a.version,
        },
      }),
    onSuccess: onDone,
    onError: (err) => {
      if (err instanceof ApiError && (err.code === "SLOT_CONFLICT" || err.code === "DOCTOR_NOT_WORKING")) setProblem(err.userMessage);
      else onError(err);
    },
  });

  const docList = doctors.some((d) => d.id === a.doctorId) ? doctors : [{ id: a.doctorId, title: a.doctorName }, ...doctors];

  return (
    <form
      className="grid grid-cols-2 gap-3"
      onSubmit={(ev) => {
        ev.preventDefault();
        if (start !== null) m.mutate();
      }}
    >
      <h3 className="col-span-2 text-sm font-semibold">{t("schedule.moveTitle")}</h3>
      <Field label={t("common.date")}>
        <Input type="date" value={date} onChange={(ev) => setDate(ev.target.value)} required />
      </Field>
      <Field label={t("schedule.start")}>
        <Input type="time" step={300} value={time} onChange={(ev) => setTime(ev.target.value)} required />
      </Field>
      <Field label={t("schedule.doctor")}>
        <NativeSelect value={doctorId} onChange={(ev) => setDoctorId(ev.target.value)}>
          {docList.map((d) => (
            <option key={d.id} value={d.id}>
              {d.title}
            </option>
          ))}
        </NativeSelect>
      </Field>
      <Field label={t("schedule.duration")}>
        <Input type="number" min={5} step={5} value={duration} onChange={(ev) => setDuration(Math.max(5, Number(ev.target.value) || 0))} />
      </Field>
      <Field label={t("schedule.chair")} className="col-span-2">
        <NativeSelect value={chairId} onChange={(ev) => setChairId(ev.target.value)}>
          <option value="">{t("schedule.noChair")}</option>
          {(chairs.data ?? [])
            .filter((c) => c.isActive || c.id === a.chairId)
            .map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
        </NativeSelect>
      </Field>
      <Field label={t("schedule.moveReason")} className="col-span-2">
        <NativeSelect value={reasonId} onChange={(ev) => setReasonId(ev.target.value)}>
          <option value="">—</option>
          {list.map((r) => (
            <option key={r.id} value={r.id}>
              {r.name}
            </option>
          ))}
        </NativeSelect>
      </Field>
      <p className="col-span-2 text-xs text-muted-foreground">{t("schedule.moveResetsConfirm")}</p>
      {problem ? <p className="col-span-2 rounded-md bg-destructive/10 p-2 text-sm text-destructive">{problem}</p> : null}
      <DialogFooter className="col-span-2">
        <Button type="button" variant="outline" onClick={onBack}>
          {t("common.back")}
        </Button>
        <Button type="submit" loading={m.isPending} disabled={start === null}>
          {t("schedule.actions.move")}
        </Button>
      </DialogFooter>
    </form>
  );
}

function EditForm({ a, onDone, onBack, onError }: { a: Appointment; onDone: (a: Appointment) => void; onBack: () => void; onError: (e: unknown) => void }) {
  const services = useServices(a.branchId);
  const chairs = useChairs(a.branchId);
  const [serviceIds, setServiceIds] = React.useState(a.services.map((s) => s.serviceId));
  const [chairId, setChairId] = React.useState(a.chairId ?? "");
  const [comment, setComment] = React.useState(a.comment ?? "");
  const m = useMutation({
    mutationFn: () =>
      api<Appointment>(`/appointments/${a.id}`, {
        method: "PATCH",
        body: {
          comment: comment.trim(),
          services: serviceIds.map((serviceId) => ({ serviceId, qty: a.services.find((x) => x.serviceId === serviceId)?.qty ?? 1 })),
          chairId: chairId || EMPTY_GUID,
          version: a.version,
        },
      }),
    onSuccess: onDone,
    onError,
  });
  // Услуги, которых нет в текущем прайсе, всё равно показываем в чипсах.
  const all = React.useMemo(() => {
    const list = [...(services.data ?? [])];
    for (const s of a.services)
      if (!list.some((x) => x.id === s.serviceId))
        list.push({ id: s.serviceId, name: s.name, durationMin: s.durationMin, price: s.plannedPrice, code: "", categoryId: "", isActive: false });
    return list;
  }, [services.data, a.services]);
  return (
    <form
      className="space-y-3"
      onSubmit={(e) => {
        e.preventDefault();
        m.mutate();
      }}
    >
      <h3 className="text-sm font-semibold">{t("schedule.editTitle")}</h3>
      <Field label={t("schedule.services")}>
        <ServicePicker services={all} value={serviceIds} onChange={setServiceIds} />
      </Field>
      <Field label={t("schedule.chair")}>
        <NativeSelect value={chairId} onChange={(e) => setChairId(e.target.value)}>
          <option value="">{t("schedule.noChair")}</option>
          {(chairs.data ?? [])
            .filter((c) => c.isActive || c.id === a.chairId)
            .map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
        </NativeSelect>
      </Field>
      <Field label={t("schedule.comment")}>
        <Textarea rows={2} value={comment} onChange={(e) => setComment(e.target.value)} />
      </Field>
      <p className="text-xs text-muted-foreground">{t("schedule.editHint")}</p>
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onBack}>
          {t("common.back")}
        </Button>
        <Button type="submit" loading={m.isPending}>
          {t("common.save")}
        </Button>
      </DialogFooter>
    </form>
  );
}
