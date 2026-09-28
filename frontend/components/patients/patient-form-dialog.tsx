"use client";

import * as React from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect, Textarea } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { api, ApiError } from "@/lib/api-client";
import { useReference } from "@/lib/queries";
import { formatDate, formatPhone } from "@/lib/format";
import { t } from "@/lib/i18n";
import type { DuplicateCandidate, Patient, PatientRequest, Schemas } from "@/lib/types";

const schema = z.object({
  lastName: z.string().trim().min(1, t("common.required")),
  firstName: z.string().trim().min(1, t("common.required")),
  middleName: z.string().optional(),
  birthDate: z.string().optional(),
  gender: z.enum(["Unknown", "Male", "Female"]),
  iin: z.string().regex(/^(\d{12})?$|\*/, "ИИН — 12 цифр").optional(),
  phone: z.string().optional(),
  phoneExtra: z.string().optional(),
  email: z.string().email("Некорректный email").or(z.literal("")).optional(),
  address: z.string().optional(),
  sourceId: z.string().optional(),
  notes: z.string().optional(),
  tags: z.string().optional(),
  isVip: z.boolean(),
});
type Values = z.infer<typeof schema>;

function toRequest(v: Values, ignoreDuplicates: boolean): PatientRequest {
  return {
    lastName: v.lastName,
    firstName: v.firstName,
    middleName: v.middleName || null,
    birthDate: v.birthDate || null,
    gender: v.gender,
    iin: v.iin || null,
    phone: v.phone || null,
    phoneExtra: v.phoneExtra || null,
    email: v.email || null,
    address: v.address || null,
    sourceId: v.sourceId || null,
    notes: v.notes || null,
    tags: (v.tags ?? "").split(",").map((s) => s.trim()).filter(Boolean),
    isVip: v.isVip,
    ignoreDuplicates,
  };
}

export function PatientFormDialog({
  open,
  onOpenChange,
  patient,
  onSaved,
  initialName,
}: {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  patient?: Patient | null;
  onSaved?: (p: Patient) => void;
  initialName?: string;
}) {
  const qc = useQueryClient();
  const sources = useReference("/lead-sources");
  const [duplicates, setDuplicates] = React.useState<DuplicateCandidate[] | null>(null);
  const form = useForm<Values>({ resolver: zodResolver(schema) });

  React.useEffect(() => {
    if (!open) return;
    setDuplicates(null);
    const [last, first, middle] = (initialName ?? "").trim().split(/\s+/);
    form.reset({
      lastName: patient?.lastName ?? last ?? "",
      firstName: patient?.firstName ?? first ?? "",
      middleName: patient?.middleName ?? middle ?? "",
      birthDate: patient?.birthDate ?? "",
      gender: patient?.gender ?? "Unknown",
      iin: patient?.iin ?? "",
      phone: patient?.phone ?? "",
      phoneExtra: patient?.phoneExtra ?? "",
      email: patient?.email ?? "",
      address: patient?.address ?? "",
      sourceId: patient?.sourceId ?? "",
      notes: patient?.notes ?? "",
      tags: (patient?.tags ?? []).join(", "),
      isVip: patient?.isVip ?? false,
    });
  }, [open, patient, initialName, form]);

  const save = useMutation({
    mutationFn: async ({ values, ignore }: { values: Values; ignore: boolean }) => {
      if (patient) return api<Patient>(`/patients/${patient.id}`, { method: "PATCH", body: toRequest(values, true) });
      const res = await api<Schemas["CreatePatientResult"]>("/patients", { method: "POST", body: toRequest(values, ignore) });
      return res.patient as Patient;
    },
    onSuccess: (p) => {
      toast.success(patient ? t("patients.saved") : t("patients.created"));
      qc.invalidateQueries({ queryKey: ["patients"] });
      qc.invalidateQueries({ queryKey: ["patient", p.id] });
      onSaved?.(p);
      onOpenChange(false);
    },
    onError: (e) => {
      if (e instanceof ApiError && (e.code === "PATIENT_DUPLICATE" || e.code === "IIN_TAKEN")) {
        setDuplicates((e.details?.duplicates as DuplicateCandidate[]) ?? []);
        if (e.code === "IIN_TAKEN") toast.error(e.userMessage);
        return;
      }
      if (e instanceof ApiError && e.code === "VALIDATION_FAILED" && e.details) {
        for (const [field, errs] of Object.entries(e.details)) {
          const msg = Array.isArray(errs) ? (errs[0] as { message: string }).message : String(errs);
          form.setError(field as keyof Values, { message: msg });
        }
        return;
      }
      toast.error(e instanceof ApiError ? e.userMessage : t("errors.INTERNAL_ERROR"));
    },
  });

  const submit = (ignore: boolean) => form.handleSubmit((values) => save.mutate({ values, ignore }))();
  const err = form.formState.errors;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent wide>
        <DialogHeader>
          <DialogTitle>{patient ? t("patients.edit") : t("patients.new")}</DialogTitle>
        </DialogHeader>
        <form
          className="grid gap-3 sm:grid-cols-3"
          onSubmit={(e) => {
            e.preventDefault();
            void submit(false);
          }}
        >
          <Field label={t("patients.lastName")} error={err.lastName?.message}>
            <Input autoFocus {...form.register("lastName")} />
          </Field>
          <Field label={t("patients.firstName")} error={err.firstName?.message}>
            <Input {...form.register("firstName")} />
          </Field>
          <Field label={t("patients.middleName")}>
            <Input {...form.register("middleName")} />
          </Field>
          <Field label={t("patients.phone")} error={err.phone?.message}>
            <Input type="tel" placeholder="+7 7__ ___-__-__" {...form.register("phone")} />
          </Field>
          <Field label={t("patients.birthDate")} error={err.birthDate?.message}>
            <Input type="date" {...form.register("birthDate")} />
          </Field>
          <Field label={t("patients.gender")}>
            <NativeSelect {...form.register("gender")}>
              {(["Unknown", "Male", "Female"] as const).map((g) => (
                <option key={g} value={g}>
                  {t(`patients.genders.${g}`)}
                </option>
              ))}
            </NativeSelect>
          </Field>
          <Field label={t("patients.iin")} error={err.iin?.message}>
            <Input inputMode="numeric" maxLength={12} {...form.register("iin")} />
          </Field>
          <Field label={t("patients.phoneExtra")}>
            <Input type="tel" {...form.register("phoneExtra")} />
          </Field>
          <Field label={t("patients.email")} error={err.email?.message}>
            <Input type="email" {...form.register("email")} />
          </Field>
          <Field label={t("patients.source")}>
            <NativeSelect {...form.register("sourceId")}>
              <option value="">—</option>
              {sources.data?.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </NativeSelect>
          </Field>
          <Field label={t("patients.tags")} hint={t("patients.tagsHint")} className="sm:col-span-2">
            <Input {...form.register("tags")} />
          </Field>
          <Field label={t("patients.address")} className="sm:col-span-2">
            <Input {...form.register("address")} />
          </Field>
          <label className="flex items-center gap-2 self-end pb-2 text-sm">
            <Checkbox checked={form.watch("isVip")} onCheckedChange={(v) => form.setValue("isVip", v === true)} />
            {t("patients.vip")}
          </label>
          <Field label={t("patients.notes")} className="sm:col-span-3">
            <Textarea rows={2} {...form.register("notes")} />
          </Field>

          {duplicates ? (
            <div className="rounded-lg border border-warning bg-warning/10 p-3 sm:col-span-3" data-testid="duplicates">
              <p className="text-sm font-medium">{t("patients.duplicatesFound")}</p>
              <p className="mb-2 text-xs text-muted-foreground">{t("patients.duplicatesHint")}</p>
              <ul className="space-y-1">
                {duplicates.map((d) => (
                  <li key={d.id} className="flex items-center justify-between gap-2 text-sm">
                    <span>
                      {d.fullName} · {formatPhone(d.phone)} {d.birthDate ? `· ${formatDate(d.birthDate)}` : ""}{" "}
                      <span className="text-xs text-muted-foreground">({t(`patients.reasons.${d.reason}`)})</span>
                    </span>
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      onClick={async () => {
                        const p = await api<Patient>(`/patients/${d.id}`);
                        onSaved?.(p);
                        onOpenChange(false);
                      }}
                    >
                      {t("patients.openExisting")}
                    </Button>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}

          <DialogFooter className="sm:col-span-3">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t("common.cancel")}
            </Button>
            {duplicates && !patient ? (
              <Button type="button" variant="secondary" loading={save.isPending} onClick={() => void submit(true)}>
                {t("patients.createAnyway")}
              </Button>
            ) : null}
            <Button type="submit" loading={save.isPending}>
              {t("common.save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
