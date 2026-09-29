"use client";

import * as React from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { AlertTriangle } from "lucide-react";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect, Textarea } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { api, ApiError, newIdempotencyKey } from "@/lib/api-client";
import { useChairs, useServices } from "@/lib/queries";
import { formatMoney } from "@/lib/format";
import { t } from "@/lib/i18n";
import type { Appointment, Schemas } from "@/lib/types";
import { PatientPicker, type PickedPatient } from "./patient-picker";
import { ServicePicker } from "./service-picker";
import { hhmm, parseHhmm, toIso, zoned } from "./tz";

export type CreateInitial = {
  doctorId?: string | null;
  date: string;
  minutes?: number | null;
  chairId?: string | null;
  patient?: PickedPatient | null;
  serviceIds?: string[];
  comment?: string | null;
};

type Problem =
  | { kind: "slot"; message: string; at?: string }
  | { kind: "notWorking"; message: string; working: { start: string; end: string }[]; canForce: boolean }
  | { kind: "other"; message: string };

const SOURCES = ["Admin", "Phone", "WalkIn", "Online"] as const;

export function AppointmentCreateDialog({
  open,
  onOpenChange,
  branchId,
  tz,
  doctors,
  initial,
  defaultChair,
  onCreated,
}: {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  branchId: string;
  tz: string;
  doctors: { id: string; title: string }[];
  initial: CreateInitial | null;
  defaultChair?: (doctorId: string, date: string, minutes: number) => string | null;
  onCreated?: (a: Appointment) => void;
}) {
  const qc = useQueryClient();
  const services = useServices(branchId);
  const chairs = useChairs(branchId);

  const [patient, setPatient] = React.useState<PickedPatient | null>(null);
  const [doctorId, setDoctorId] = React.useState("");
  const [chairId, setChairId] = React.useState("");
  const [date, setDate] = React.useState("");
  const [time, setTime] = React.useState("");
  const [serviceIds, setServiceIds] = React.useState<string[]>([]);
  const [duration, setDuration] = React.useState(30);
  const [durationTouched, setDurationTouched] = React.useState(false);
  const [source, setSource] = React.useState<(typeof SOURCES)[number]>("Admin");
  const [comment, setComment] = React.useState("");
  const [force, setForce] = React.useState(false);
  const [problem, setProblem] = React.useState<Problem | null>(null);
  const [idemKey, setIdemKey] = React.useState(newIdempotencyKey);

  React.useEffect(() => {
    if (!open || !initial) return;
    const d = initial.doctorId ?? doctors[0]?.id ?? "";
    setPatient(initial.patient ?? null);
    setDoctorId(d);
    setDate(initial.date);
    setTime(initial.minutes != null ? hhmm(initial.minutes) : "");
    setChairId(initial.chairId ?? (d && initial.minutes != null ? (defaultChair?.(d, initial.date, initial.minutes) ?? "") : ""));
    setServiceIds(initial.serviceIds ?? []);
    setDuration(30);
    setDurationTouched(false);
    setSource("Admin");
    setComment(initial.comment ?? "");
    setForce(false);
    setProblem(null);
    setIdemKey(newIdempotencyKey());
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, initial]);

  const byId = React.useMemo(() => new Map((services.data ?? []).map((s) => [s.id, s])), [services.data]);
  const autoDuration = serviceIds.reduce((sum, id) => sum + (byId.get(id)?.durationMin ?? 0), 0);
  const planned = serviceIds.reduce((sum, id) => sum + (byId.get(id)?.price ?? 0), 0);
  React.useEffect(() => {
    if (!durationTouched) setDuration(autoDuration > 0 ? Math.max(15, Math.ceil(autoDuration / 5) * 5) : 30);
  }, [autoDuration, durationTouched]);

  const slots = useQuery({
    queryKey: ["slots", branchId, doctorId, date, serviceIds],
    queryFn: () => api<Schemas["AvailableSlot"][]>("/slots/available", { query: { branch_id: branchId, doctor_id: doctorId, date, service_ids: serviceIds } }),
    enabled: open && !!doctorId && !!date,
    staleTime: 30_000,
  });
  const suggested = (slots.data ?? []).map((s) => zoned(s.start, tz)).filter((z) => z.date === date);

  const startMin = parseHhmm(time);
  const create = useMutation({
    mutationFn: () => {
      const startsAt = toIso(date, startMin!, tz);
      const endsAt = toIso(date, startMin! + duration, tz);
      const body: Schemas["CreateAppointmentRequest"] = {
        branchId,
        patientId: patient!.id,
        doctorId,
        chairId: chairId || null,
        startsAt,
        endsAt,
        services: serviceIds.map((serviceId) => ({ serviceId, qty: 1 })),
        source,
        comment: comment.trim() || null,
        force: force || null,
      };
      return api<Appointment>("/appointments", { method: "POST", body, idempotencyKey: idemKey });
    },
    onSuccess: (a) => {
      toast.success(t("schedule.created"));
      qc.invalidateQueries({ queryKey: ["calendar"] });
      qc.invalidateQueries({ queryKey: ["slots"] });
      qc.invalidateQueries({ queryKey: ["patient", a.patientId] });
      onCreated?.(a);
      onOpenChange(false);
    },
    onError: (e) => {
      setIdemKey(newIdempotencyKey());
      if (e instanceof ApiError && e.code === "SLOT_CONFLICT") {
        const c = e.details?.conflict as { startsAt?: string; endsAt?: string } | undefined;
        setProblem({
          kind: "slot",
          message: e.userMessage,
          at: c?.startsAt && c.endsAt ? `${hhmm(zoned(c.startsAt, tz).minutes)}–${hhmm(zoned(c.endsAt, tz).minutes)}` : undefined,
        });
        return;
      }
      if (e instanceof ApiError && e.code === "DOCTOR_NOT_WORKING") {
        setProblem({
          kind: "notWorking",
          message: e.userMessage,
          working: (e.details?.working as { start: string; end: string }[]) ?? [],
          canForce: e.details?.canForce === true,
        });
        return;
      }
      setProblem({ kind: "other", message: e instanceof ApiError ? e.userMessage : t("errors.INTERNAL_ERROR") });
    },
  });

  const valid = !!patient && !!doctorId && !!date && startMin !== null && duration > 0;
  const activeChairs = (chairs.data ?? []).filter((c) => c.isActive);

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent wide data-testid="appointment-create-dialog">
        <DialogHeader>
          <DialogTitle>{t("schedule.newAppointment")}</DialogTitle>
        </DialogHeader>
        <form
          className="grid gap-3 sm:grid-cols-6"
          onSubmit={(e) => {
            e.preventDefault();
            if (valid) create.mutate();
          }}
        >
          <Field label={t("schedule.patient")} className="sm:col-span-6">
            <PatientPicker value={patient} onChange={setPatient} autoFocus={!initial?.patient} />
          </Field>
          <Field label={t("schedule.doctor")} className="sm:col-span-3">
            <NativeSelect
              value={doctorId}
              onChange={(e) => {
                setDoctorId(e.target.value);
                if (startMin !== null) setChairId(defaultChair?.(e.target.value, date, startMin) ?? chairId);
                setProblem(null);
              }}
              data-testid="doctor-select"
            >
              <option value="" disabled>
                —
              </option>
              {doctors.map((d) => (
                <option key={d.id} value={d.id}>
                  {d.title}
                </option>
              ))}
            </NativeSelect>
          </Field>
          <Field label={t("schedule.chair")} className="sm:col-span-3">
            <NativeSelect value={chairId} onChange={(e) => setChairId(e.target.value)}>
              <option value="">{t("schedule.noChair")}</option>
              {activeChairs.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </NativeSelect>
          </Field>
          <Field label={t("common.date")} className="sm:col-span-2">
            <Input
              type="date"
              value={date}
              onChange={(e) => {
                setDate(e.target.value);
                setProblem(null);
              }}
              required
            />
          </Field>
          <Field label={t("schedule.start")} className="sm:col-span-2">
            <Input
              type="time"
              step={300}
              value={time}
              onChange={(e) => {
                setTime(e.target.value);
                setProblem(null);
              }}
              required
              data-testid="time-input"
            />
          </Field>
          <Field
            label={t("schedule.duration")}
            className="sm:col-span-2"
            hint={startMin !== null ? `${t("schedule.end")}: ${hhmm(startMin + duration)}` : undefined}
          >
            <Input
              type="number"
              min={5}
              step={5}
              value={duration}
              onChange={(e) => {
                setDuration(Math.max(5, Number(e.target.value) || 0));
                setDurationTouched(true);
              }}
            />
          </Field>
          {doctorId && date ? (
            <div className="sm:col-span-6">
              <p className="mb-1 text-xs text-muted-foreground">{t("schedule.freeSlots")}</p>
              {slots.isLoading ? (
                <p className="text-xs text-muted-foreground">{t("common.loading")}</p>
              ) : suggested.length === 0 ? (
                <p className="text-xs text-muted-foreground">{t("schedule.noFreeSlots")}</p>
              ) : (
                <div className="flex flex-wrap gap-1">
                  {suggested.slice(0, 24).map((z) => (
                    <button
                      key={z.minutes}
                      type="button"
                      onClick={() => {
                        setTime(hhmm(z.minutes));
                        setProblem(null);
                        if (!chairId) setChairId(defaultChair?.(doctorId, date, z.minutes) ?? "");
                      }}
                      className={`rounded-md border px-2 py-0.5 text-xs tabular-nums hover:bg-muted ${hhmm(z.minutes) === time ? "border-primary bg-primary/10 text-primary" : ""}`}
                    >
                      {hhmm(z.minutes)}
                    </button>
                  ))}
                </div>
              )}
            </div>
          ) : null}
          <Field label={t("schedule.services")} className="sm:col-span-6" hint={planned ? `${t("schedule.planned")}: ${formatMoney(planned)}` : undefined}>
            <ServicePicker services={services.data ?? []} value={serviceIds} onChange={setServiceIds} />
          </Field>
          <Field label={t("schedule.source")} className="sm:col-span-2">
            <NativeSelect value={source} onChange={(e) => setSource(e.target.value as (typeof SOURCES)[number])}>
              {SOURCES.map((s) => (
                <option key={s} value={s}>
                  {t(`schedule.sources.${s}`)}
                </option>
              ))}
            </NativeSelect>
          </Field>
          <Field label={t("schedule.comment")} className="sm:col-span-4">
            <Textarea rows={1} className="min-h-9" value={comment} onChange={(e) => setComment(e.target.value)} />
          </Field>

          {problem ? (
            <div className="flex gap-2 rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm sm:col-span-6" data-testid="create-problem">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-destructive" />
              <div className="space-y-1">
                <p className="font-medium text-destructive">
                  {problem.message}
                  {problem.kind === "slot" && problem.at ? ` (${problem.at})` : ""}
                </p>
                {problem.kind === "slot" ? <p className="text-xs text-muted-foreground">{t("schedule.slotConflictHint")}</p> : null}
                {problem.kind === "notWorking" ? (
                  <>
                    <p className="text-xs text-muted-foreground">
                      {problem.working.length
                        ? `${t("schedule.workingHours")}: ${problem.working.map((w) => `${hhmm(zoned(w.start, tz).minutes)}–${hhmm(zoned(w.end, tz).minutes)}`).join(", ")}`
                        : t("schedule.dayOff")}
                    </p>
                    {problem.canForce ? (
                      <label className="flex items-center gap-2 text-sm">
                        <Checkbox checked={force} onCheckedChange={(v) => setForce(v === true)} />
                        {t("schedule.force")}
                        <span className="text-xs text-muted-foreground">— {t("schedule.forceHint")}</span>
                      </label>
                    ) : null}
                  </>
                ) : null}
              </div>
            </div>
          ) : null}

          <DialogFooter className="sm:col-span-6">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" disabled={!valid} loading={create.isPending} data-testid="create-appointment-submit">
              {t("common.create")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
