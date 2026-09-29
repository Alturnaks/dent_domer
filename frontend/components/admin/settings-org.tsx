"use client";

import * as React from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Switch } from "@/components/ui/checkbox";
import { TableSkeleton } from "@/components/ui/skeleton";
import { ErrorBlock, MoneyInput, hhmm, hhmmss, minorToInput, tengeToMinorOrNull, toastError } from "@/components/admin/common";
import { api } from "@/lib/api-client";
import { useAuth } from "@/lib/auth";
import { t } from "@/lib/i18n";
import type { Schemas } from "@/lib/types";

type Org = Schemas["OrgDto"];
type Settings = Schemas["OrganizationSettings"];

const TIMEZONES = ["Asia/Almaty", "Asia/Aqtobe", "Asia/Aqtau", "Asia/Atyrau", "Asia/Oral", "Asia/Qostanay", "Asia/Qyzylorda", "Asia/Tashkent", "Asia/Bishkek", "Europe/Moscow"];

type Draft = {
  name: string;
  timezone: string;
  logoUrl: string;
  slotMinutes: string;
  defaultOpen: string;
  defaultClose: string;
  noShowAfterMinutes: string;
  reminder24h: boolean;
  reminder2h: boolean;
  allowNegativeStock: boolean;
  payrollOnlyPaidVisits: boolean;
  purchaseOrderApprovalThreshold: string;
  suspiciousWriteoffAmount: string;
  suspiciousInventoryDiffAmount: string;
  dailySummaryTime: string;
};

function toDraft(o: Org): Draft {
  const s = o.settings;
  return {
    name: o.name,
    timezone: o.timezone,
    logoUrl: o.logoUrl ?? "",
    slotMinutes: String(s.slotMinutes ?? 15),
    defaultOpen: hhmm(s.defaultOpen),
    defaultClose: hhmm(s.defaultClose),
    noShowAfterMinutes: String(s.noShowAfterMinutes ?? 60),
    reminder24h: s.reminder24h ?? false,
    reminder2h: s.reminder2h ?? false,
    allowNegativeStock: s.allowNegativeStock ?? false,
    payrollOnlyPaidVisits: s.payrollOnlyPaidVisits ?? false,
    purchaseOrderApprovalThreshold: minorToInput(s.purchaseOrderApprovalThreshold),
    suspiciousWriteoffAmount: minorToInput(s.suspiciousWriteoffAmount),
    suspiciousInventoryDiffAmount: minorToInput(s.suspiciousInventoryDiffAmount),
    dailySummaryTime: hhmm(s.dailySummaryTime),
  };
}

function toRequest(d: Draft): Schemas["UpdateOrgRequest"] {
  const settings: Settings = {
    slotMinutes: Number(d.slotMinutes) || 15,
    defaultOpen: hhmmss(d.defaultOpen),
    defaultClose: hhmmss(d.defaultClose),
    noShowAfterMinutes: Number(d.noShowAfterMinutes) || 0,
    reminder24h: d.reminder24h,
    reminder2h: d.reminder2h,
    allowNegativeStock: d.allowNegativeStock,
    payrollOnlyPaidVisits: d.payrollOnlyPaidVisits,
    purchaseOrderApprovalThreshold: tengeToMinorOrNull(d.purchaseOrderApprovalThreshold) ?? 0,
    suspiciousWriteoffAmount: tengeToMinorOrNull(d.suspiciousWriteoffAmount) ?? 0,
    suspiciousInventoryDiffAmount: tengeToMinorOrNull(d.suspiciousInventoryDiffAmount) ?? 0,
    dailySummaryTime: hhmmss(d.dailySummaryTime),
  };
  return { name: d.name.trim(), timezone: d.timezone, logoUrl: d.logoUrl.trim() || null, settings };
}

export function OrgSettingsTab() {
  const qc = useQueryClient();
  const { reloadMe } = useAuth();
  const org = useQuery({ queryKey: ["org"], queryFn: () => api<Org>("/org"), refetchOnWindowFocus: false });
  const [d, setD] = React.useState<Draft | null>(null);

  React.useEffect(() => {
    if (org.data) setD(toDraft(org.data));
  }, [org.data]);

  const save = useMutation({
    mutationFn: () => api<Org>("/org", { method: "PATCH", body: toRequest(d!) }),
    onSuccess: (o) => {
      qc.setQueryData(["org"], o);
      toast.success(t("settings.org.savedToast"));
      void reloadMe().catch(() => undefined);
    },
    onError: toastError,
  });

  if (org.isLoading || (!d && !org.isError)) return <TableSkeleton rows={6} cols={2} />;
  if (org.isError || !d) return <ErrorBlock onRetry={() => org.refetch()} />;

  const set = <K extends keyof Draft>(k: K, v: Draft[K]) => setD((p) => (p ? { ...p, [k]: v } : p));
  const dirty = JSON.stringify(toRequest(d)) !== JSON.stringify(toRequest(toDraft(org.data!)));
  const tzOptions = TIMEZONES.includes(d.timezone) ? TIMEZONES : [d.timezone, ...TIMEZONES];

  const toggle = (k: "reminder24h" | "reminder2h" | "allowNegativeStock" | "payrollOnlyPaidVisits") => (
    <label className="flex items-center gap-2 text-sm">
      <Switch checked={d[k]} onCheckedChange={(v) => set(k, v)} />
      {t(`settings.org.${k}`)}
    </label>
  );

  return (
    <form
      className="space-y-4"
      onSubmit={(e) => {
        e.preventDefault();
        if (d.name.trim()) save.mutate();
      }}
    >
      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>{t("settings.org.main")}</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-3 sm:grid-cols-2">
            <Field label={t("settings.org.name")} className="sm:col-span-2" error={d.name.trim() ? undefined : t("common.required")}>
              <Input value={d.name} onChange={(e) => set("name", e.target.value)} />
            </Field>
            <Field label={t("settings.org.timezone")}>
              <NativeSelect value={d.timezone} onChange={(e) => set("timezone", e.target.value)}>
                {tzOptions.map((z) => (
                  <option key={z} value={z}>
                    {z}
                  </option>
                ))}
              </NativeSelect>
            </Field>
            <Field label={t("settings.org.currency")}>
              <Input value={org.data!.currency} disabled />
            </Field>
            <Field label={t("settings.org.logoUrl")} className="sm:col-span-2">
              <Input type="url" placeholder="https://…" value={d.logoUrl} onChange={(e) => set("logoUrl", e.target.value)} />
            </Field>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>{t("settings.org.schedule")}</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-3 sm:grid-cols-2">
            <Field label={t("settings.org.slotMinutes")}>
              <NativeSelect value={d.slotMinutes} onChange={(e) => set("slotMinutes", e.target.value)}>
                {[5, 10, 15, 20, 30, 60].map((m) => (
                  <option key={m} value={m}>
                    {m}
                  </option>
                ))}
              </NativeSelect>
            </Field>
            <Field label={t("settings.org.noShowAfterMinutes")}>
              <Input type="number" min={0} value={d.noShowAfterMinutes} onChange={(e) => set("noShowAfterMinutes", e.target.value)} />
            </Field>
            <Field label={t("settings.org.defaultOpen")}>
              <Input type="time" value={d.defaultOpen} onChange={(e) => set("defaultOpen", e.target.value)} />
            </Field>
            <Field label={t("settings.org.defaultClose")}>
              <Input type="time" value={d.defaultClose} onChange={(e) => set("defaultClose", e.target.value)} />
            </Field>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>{t("settings.org.reminders")}</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            {toggle("reminder24h")}
            {toggle("reminder2h")}
            <Field label={t("settings.org.dailySummaryTime")} className="max-w-48">
              <Input type="time" value={d.dailySummaryTime} onChange={(e) => set("dailySummaryTime", e.target.value)} />
            </Field>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>{t("settings.org.controls")}</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            {toggle("allowNegativeStock")}
            {toggle("payrollOnlyPaidVisits")}
            <div className="grid gap-3 sm:grid-cols-3">
              <Field label={t("settings.org.purchaseOrderApprovalThreshold")}>
                <MoneyInput value={d.purchaseOrderApprovalThreshold} onChange={(v) => set("purchaseOrderApprovalThreshold", v)} />
              </Field>
              <Field label={t("settings.org.suspiciousWriteoffAmount")}>
                <MoneyInput value={d.suspiciousWriteoffAmount} onChange={(v) => set("suspiciousWriteoffAmount", v)} />
              </Field>
              <Field label={t("settings.org.suspiciousInventoryDiffAmount")}>
                <MoneyInput value={d.suspiciousInventoryDiffAmount} onChange={(v) => set("suspiciousInventoryDiffAmount", v)} />
              </Field>
            </div>
          </CardContent>
        </Card>
      </div>
      <div className="flex justify-end gap-2">
        <Button type="button" variant="outline" disabled={!dirty} onClick={() => setD(toDraft(org.data!))}>
          {t("common.reset")}
        </Button>
        <Button type="submit" disabled={!dirty || !d.name.trim()} loading={save.isPending}>
          {t("admin.saveChanges")}
        </Button>
      </div>
    </form>
  );
}
