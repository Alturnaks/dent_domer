"use client";

import * as React from "react";
import Link from "next/link";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { CalendarPlus, MessageCircle, MoreHorizontal, Plus } from "lucide-react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input, NativeSelect, Textarea } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { EmptyState } from "@/components/ui/empty-state";
import { TableSkeleton } from "@/components/ui/skeleton";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { api, ApiError } from "@/lib/api-client";
import { useAuth, useOrgHref } from "@/lib/auth";
import { useServices } from "@/lib/queries";
import { formatDate, formatPhone, whatsappLink } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import type { Schemas, Waitlist } from "@/lib/types";
import { PatientPicker, type PickedPatient } from "./patient-picker";

type WaitlistStatus = Schemas["WaitlistStatus"];
const STATUSES: WaitlistStatus[] = ["Waiting", "Offered", "Booked", "Cancelled"];
const VARIANT: Record<WaitlistStatus, "default" | "warning" | "success" | "muted"> = {
  Waiting: "default",
  Offered: "warning",
  Booked: "success",
  Cancelled: "muted",
};

export function toWaitlistRequest(w: Waitlist, patch: Partial<Schemas["WaitlistRequest"]> = {}): Schemas["WaitlistRequest"] {
  return {
    branchId: w.branchId,
    patientId: w.patientId,
    doctorId: w.doctorId,
    serviceId: w.serviceId,
    preferredFrom: w.preferredFrom,
    preferredTo: w.preferredTo,
    preferredTimes: w.preferredTimes,
    status: w.status,
    comment: w.comment,
    ...patch,
  };
}

export function WaitlistPanel({
  branchId,
  doctors,
  onBook,
}: {
  branchId: string;
  doctors: { id: string; title: string }[];
  onBook: (w: Waitlist) => void;
}) {
  const { can } = useAuth();
  const { href } = useOrgHref();
  const qc = useQueryClient();
  const manage = can(P.scheduleManage);
  const [status, setStatus] = React.useState<WaitlistStatus>("Waiting");
  const [edit, setEdit] = React.useState<Waitlist | "new" | null>(null);

  const list = useQuery({
    queryKey: ["waitlist", branchId, status],
    queryFn: () => api<Waitlist[]>("/waitlist", { query: { branch_id: branchId, status } }),
  });

  const setSt = useMutation({
    mutationFn: ({ w, s }: { w: Waitlist; s: WaitlistStatus }) => api<Waitlist>(`/waitlist/${w.id}`, { method: "PATCH", body: toWaitlistRequest(w, { status: s }) }),
    onSuccess: () => {
      toast.success(t("common.saved"));
      qc.invalidateQueries({ queryKey: ["waitlist"] });
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.userMessage : t("errors.INTERNAL_ERROR")),
  });

  const rows = list.data ?? [];

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="inline-flex h-9 items-center gap-1 rounded-lg bg-muted p-1">
          {STATUSES.map((s) => (
            <button
              key={s}
              type="button"
              onClick={() => setStatus(s)}
              className={`rounded-md px-3 py-1 text-sm font-medium ${status === s ? "bg-card text-foreground shadow-sm" : "text-muted-foreground"}`}
            >
              {t(`waitlist.status.${s}`)}
            </button>
          ))}
        </div>
        {manage ? (
          <Button onClick={() => setEdit("new")} data-testid="waitlist-add">
            <Plus /> {t("schedule.waitlistAdd")}
          </Button>
        ) : null}
      </div>
      <Card>
        {list.isLoading ? (
          <TableSkeleton rows={4} cols={5} />
        ) : list.isError ? (
          <EmptyState
            className="m-4"
            title={list.error instanceof ApiError ? list.error.userMessage : t("errors.INTERNAL_ERROR")}
            action={<Button variant="outline" onClick={() => list.refetch()}>{t("common.retry")}</Button>}
          />
        ) : rows.length === 0 ? (
          <EmptyState className="m-4" title={t("schedule.waitlistEmpty")} description={status === "Waiting" ? t("waitlist.emptyHint") : undefined} />
        ) : (
          <Table>
            <THead>
              <TR>
                <TH>{t("schedule.patient")}</TH>
                <TH>{t("schedule.doctor")}</TH>
                <TH>{t("waitlist.service")}</TH>
                <TH>{t("schedule.preferred")}</TH>
                <TH>{t("schedule.comment")}</TH>
                <TH>{t("waitlist.createdAt")}</TH>
                <TH className="w-px" />
              </TR>
            </THead>
            <TBody>
              {rows.map((w) => {
                const wa = whatsappLink(w.patientPhone);
                return (
                  <TR key={w.id}>
                    <TD>
                      <Link href={href(`patients/${w.patientId}`)} className="font-medium hover:underline">
                        {w.patientName}
                      </Link>
                      <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
                        {formatPhone(w.patientPhone)}
                        {wa ? (
                          <a href={wa} target="_blank" rel="noreferrer" className="text-success">
                            <MessageCircle className="h-3.5 w-3.5" />
                          </a>
                        ) : null}
                      </div>
                    </TD>
                    <TD>{w.doctorName ?? t("waitlist.anyDoctor")}</TD>
                    <TD>{w.serviceName ?? "—"}</TD>
                    <TD className="whitespace-nowrap">
                      {w.preferredFrom || w.preferredTo ? `${formatDate(w.preferredFrom)} – ${formatDate(w.preferredTo)}` : "—"}
                      {w.preferredTimes ? <div className="text-xs text-muted-foreground">{w.preferredTimes}</div> : null}
                    </TD>
                    <TD className="max-w-60 truncate text-sm" title={w.comment ?? ""}>
                      {w.comment ?? "—"}
                    </TD>
                    <TD className="whitespace-nowrap">
                      {formatDate(w.createdAt)}
                      {status !== "Waiting" ? (
                        <div>
                          <Badge variant={VARIANT[w.status]}>{t(`waitlist.status.${w.status}`)}</Badge>
                        </div>
                      ) : null}
                    </TD>
                    <TD>
                      {manage ? (
                        <div className="flex items-center gap-1">
                          {w.status === "Waiting" || w.status === "Offered" ? (
                            <Button size="sm" variant="outline" onClick={() => onBook(w)}>
                              <CalendarPlus /> {t("waitlist.book")}
                            </Button>
                          ) : null}
                          <DropdownMenu>
                            <DropdownMenuTrigger asChild>
                              <Button size="icon-sm" variant="ghost" aria-label={t("common.actions")}>
                                <MoreHorizontal />
                              </Button>
                            </DropdownMenuTrigger>
                            <DropdownMenuContent align="end">
                              <DropdownMenuItem onSelect={() => setEdit(w)}>{t("common.edit")}</DropdownMenuItem>
                              {STATUSES.filter((s) => s !== w.status).map((s) => (
                                <DropdownMenuItem key={s} onSelect={() => setSt.mutate({ w, s })}>
                                  {t(`waitlist.setStatus.${s}`)}
                                </DropdownMenuItem>
                              ))}
                            </DropdownMenuContent>
                          </DropdownMenu>
                        </div>
                      ) : null}
                    </TD>
                  </TR>
                );
              })}
            </TBody>
          </Table>
        )}
      </Card>
      <WaitlistDialog entry={edit} onOpenChange={(o) => !o && setEdit(null)} branchId={branchId} doctors={doctors} />
    </div>
  );
}

function WaitlistDialog({
  entry,
  onOpenChange,
  branchId,
  doctors,
}: {
  entry: Waitlist | "new" | null;
  onOpenChange: (o: boolean) => void;
  branchId: string;
  doctors: { id: string; title: string }[];
}) {
  const qc = useQueryClient();
  const services = useServices(branchId);
  const existing = entry && entry !== "new" ? entry : null;
  const [patient, setPatient] = React.useState<PickedPatient | null>(null);
  const [doctorId, setDoctorId] = React.useState("");
  const [serviceId, setServiceId] = React.useState("");
  const [from, setFrom] = React.useState("");
  const [to, setTo] = React.useState("");
  const [times, setTimes] = React.useState("");
  const [comment, setComment] = React.useState("");

  React.useEffect(() => {
    if (!entry) return;
    setPatient(existing ? { id: existing.patientId, fullName: existing.patientName, phone: existing.patientPhone } : null);
    setDoctorId(existing?.doctorId ?? "");
    setServiceId(existing?.serviceId ?? "");
    setFrom(existing?.preferredFrom ?? "");
    setTo(existing?.preferredTo ?? "");
    setTimes(existing?.preferredTimes ?? "");
    setComment(existing?.comment ?? "");
  }, [entry, existing]);

  const m = useMutation({
    mutationFn: () => {
      const body: Schemas["WaitlistRequest"] = {
        branchId: existing?.branchId ?? branchId,
        patientId: patient!.id,
        doctorId: doctorId || null,
        serviceId: serviceId || null,
        preferredFrom: from || null,
        preferredTo: to || null,
        preferredTimes: times.trim() || null,
        status: existing?.status ?? null,
        comment: comment.trim() || null,
      };
      return existing ? api<Waitlist>(`/waitlist/${existing.id}`, { method: "PATCH", body }) : api<Waitlist>("/waitlist", { method: "POST", body });
    },
    onSuccess: () => {
      toast.success(existing ? t("common.saved") : t("waitlist.added"));
      qc.invalidateQueries({ queryKey: ["waitlist"] });
      onOpenChange(false);
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.userMessage : t("errors.INTERNAL_ERROR")),
  });

  return (
    <Dialog open={!!entry} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{existing ? t("waitlist.editTitle") : t("schedule.waitlistAdd")}</DialogTitle>
        </DialogHeader>
        <form
          className="grid grid-cols-2 gap-3"
          onSubmit={(e) => {
            e.preventDefault();
            if (patient) m.mutate();
          }}
        >
          <Field label={t("schedule.patient")} className="col-span-2">
            {existing ? (
              <div className="rounded-md border bg-muted/40 px-3 py-2 text-sm font-medium">{existing.patientName}</div>
            ) : (
              <PatientPicker value={patient} onChange={setPatient} autoFocus />
            )}
          </Field>
          <Field label={t("schedule.doctor")}>
            <NativeSelect value={doctorId} onChange={(e) => setDoctorId(e.target.value)}>
              <option value="">{t("waitlist.anyDoctor")}</option>
              {doctors.map((d) => (
                <option key={d.id} value={d.id}>
                  {d.title}
                </option>
              ))}
            </NativeSelect>
          </Field>
          <Field label={t("waitlist.service")}>
            <NativeSelect value={serviceId} onChange={(e) => setServiceId(e.target.value)}>
              <option value="">—</option>
              {(services.data ?? [])
                .filter((s) => s.isActive || s.id === serviceId)
                .map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))}
            </NativeSelect>
          </Field>
          <Field label={t("waitlist.preferredFrom")}>
            <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          </Field>
          <Field label={t("waitlist.preferredTo")}>
            <Input type="date" value={to} min={from || undefined} onChange={(e) => setTo(e.target.value)} />
          </Field>
          <Field label={t("waitlist.preferredTimes")} className="col-span-2">
            <Input value={times} onChange={(e) => setTimes(e.target.value)} placeholder={t("waitlist.preferredTimesPlaceholder")} />
          </Field>
          <Field label={t("schedule.comment")} className="col-span-2">
            <Textarea rows={2} value={comment} onChange={(e) => setComment(e.target.value)} />
          </Field>
          <DialogFooter className="col-span-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" disabled={!patient} loading={m.isPending}>
              {t("common.save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
