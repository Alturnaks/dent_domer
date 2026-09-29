"use client";

import * as React from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { api, ApiError } from "@/lib/api-client";
import { useChairs } from "@/lib/queries";
import { t } from "@/lib/i18n";
import type { Schemas, TimeBlock } from "@/lib/types";
import { hhmm, parseHhmm, toIso } from "./tz";

export function TimeBlockDialog({
  open,
  onOpenChange,
  branchId,
  tz,
  doctors,
  initial,
}: {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  branchId: string;
  tz: string;
  doctors: { id: string; title: string }[];
  initial: { doctorId?: string | null; date: string; minutes?: number | null };
}) {
  const qc = useQueryClient();
  const chairs = useChairs(branchId);
  const [doctorId, setDoctorId] = React.useState("");
  const [chairId, setChairId] = React.useState("");
  const [date, setDate] = React.useState("");
  const [from, setFrom] = React.useState("");
  const [to, setTo] = React.useState("");
  const [reason, setReason] = React.useState("");

  React.useEffect(() => {
    if (!open) return;
    setDoctorId(initial.doctorId ?? "");
    setChairId("");
    setDate(initial.date);
    const m = initial.minutes ?? 13 * 60;
    setFrom(hhmm(m));
    setTo(hhmm(m + 60));
    setReason("");
  }, [open, initial]);

  const a = parseHhmm(from);
  const b = parseHhmm(to);
  const valid = !!date && a !== null && b !== null && b > a;

  const m = useMutation({
    mutationFn: () => {
      const body: Schemas["TimeBlockRequest"] = {
        branchId,
        doctorId: doctorId || null,
        chairId: chairId || null,
        startsAt: toIso(date, a!, tz),
        endsAt: toIso(date, b!, tz),
        reason: reason.trim() || null,
      };
      return api<TimeBlock>("/time-blocks", { method: "POST", body });
    },
    onSuccess: () => {
      toast.success(t("schedule.blockCreated"));
      qc.invalidateQueries({ queryKey: ["calendar"] });
      qc.invalidateQueries({ queryKey: ["time-blocks"] });
      onOpenChange(false);
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.userMessage : t("errors.INTERNAL_ERROR")),
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("schedule.blockTitle")}</DialogTitle>
        </DialogHeader>
        <form
          className="grid grid-cols-2 gap-3"
          onSubmit={(e) => {
            e.preventDefault();
            if (valid) m.mutate();
          }}
        >
          <Field label={t("schedule.doctor")}>
            <NativeSelect value={doctorId} onChange={(e) => setDoctorId(e.target.value)}>
              <option value="">{t("schedule.allDoctors")}</option>
              {doctors.map((d) => (
                <option key={d.id} value={d.id}>
                  {d.title}
                </option>
              ))}
            </NativeSelect>
          </Field>
          <Field label={t("schedule.chair")}>
            <NativeSelect value={chairId} onChange={(e) => setChairId(e.target.value)}>
              <option value="">—</option>
              {(chairs.data ?? []).map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </NativeSelect>
          </Field>
          <Field label={t("common.date")} className="col-span-2">
            <Input type="date" value={date} onChange={(e) => setDate(e.target.value)} required />
          </Field>
          <Field label={t("doctorSchedules.from")}>
            <Input type="time" step={300} value={from} onChange={(e) => setFrom(e.target.value)} required />
          </Field>
          <Field label={t("doctorSchedules.to")} error={a !== null && b !== null && b <= a ? t("schedule.endBeforeStart") : undefined}>
            <Input type="time" step={300} value={to} onChange={(e) => setTo(e.target.value)} required />
          </Field>
          <Field label={t("schedule.blockReason")} className="col-span-2">
            <Input value={reason} onChange={(e) => setReason(e.target.value)} placeholder={t("schedule.blockReasonPlaceholder")} />
          </Field>
          <DialogFooter className="col-span-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" disabled={!valid} loading={m.isPending}>
              {t("common.create")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
