"use client";

import * as React from "react";
import { useParams } from "next/navigation";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, CalendarPlus, MessageCircle, Pencil, Star } from "lucide-react";
import { PageHeader } from "@/components/ui/empty-state";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { Skeleton, TableSkeleton } from "@/components/ui/skeleton";
import { PatientFormDialog } from "@/components/patients/patient-form-dialog";
import { AuditList } from "@/components/audit/audit-list";
import { api } from "@/lib/api-client";
import { useAuth, useOrgHref } from "@/lib/auth";
import { formatDate, formatDateTime, formatMoney, formatPhone, whatsappLink } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import { STATUS_COLORS } from "@/lib/status";
import type { AuditEntry, CursorPage, Patient, Schemas } from "@/lib/types";

function Row({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex justify-between gap-3 py-1 text-sm">
      <span className="text-muted-foreground">{label}</span>
      <span className="text-right">{value ?? "—"}</span>
    </div>
  );
}

export default function PatientCardPage() {
  const { id } = useParams<{ id: string }>();
  const { can } = useAuth();
  const { href } = useOrgHref();
  const [editOpen, setEditOpen] = React.useState(false);
  const [tab, setTab] = React.useState("overview");

  const patient = useQuery({ queryKey: ["patient", id], queryFn: () => api<Patient>(`/patients/${id}`) });
  const appointments = useQuery({ queryKey: ["patient", id, "appointments"], queryFn: () => api<Schemas["PatientAppointmentItem"][]>(`/patients/${id}/appointments`), enabled: tab === "appointments" || tab === "overview" });
  const visits = useQuery({ queryKey: ["patient", id, "visits"], queryFn: () => api<Schemas["PatientVisitItem"][]>(`/patients/${id}/visits`), enabled: tab === "visits" });
  const payments = useQuery({ queryKey: ["patient", id, "payments"], queryFn: () => api<Schemas["PatientPaymentItem"][]>(`/patients/${id}/payments`), enabled: tab === "payments" });
  const balance = useQuery({ queryKey: ["patient", id, "balance"], queryFn: () => api<Schemas["PatientBalanceDto"]>(`/patients/${id}/balance`), enabled: tab === "payments" });
  const history = useQuery({ queryKey: ["patient", id, "history"], queryFn: () => api<CursorPage<AuditEntry>>(`/patients/${id}/history`), enabled: tab === "history" });

  const p = patient.data;
  if (patient.isLoading || !p) {
    return (
      <div className="space-y-3">
        <Skeleton className="h-8 w-80" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }
  const wa = whatsappLink(p.phone);
  const upcoming = (appointments.data ?? []).filter((a) => new Date(a.startsAt) > new Date() && (a.status === "Scheduled" || a.status === "Confirmed"));

  return (
    <div>
      <Link href={href("patients")} className="mb-2 inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
        <ArrowLeft className="h-4 w-4" /> {t("patients.title")}
      </Link>
      <PageHeader
        title={p.fullName}
        description={
          <span className="flex flex-wrap items-center gap-2">
            {p.isVip ? (
              <Badge variant="warning">
                <Star className="h-3 w-3" /> VIP
              </Badge>
            ) : null}
            {p.tags.map((tg) => (
              <Badge key={tg} variant="muted">
                {tg}
              </Badge>
            ))}
            {p.mergedIntoId ? <Badge variant="destructive">{t("patients.merged_into")}</Badge> : null}
          </span>
        }
        actions={
          <>
            {wa ? (
              <Button variant="outline" asChild>
                <a href={wa} target="_blank" rel="noreferrer">
                  <MessageCircle /> {t("patients.writeWhatsapp")}
                </a>
              </Button>
            ) : null}
            {can(P.scheduleManage) ? (
              <Button variant="outline" asChild>
                <Link href={href(`schedule?patient=${p.id}`)}>
                  <CalendarPlus /> {t("patients.bookAppointment")}
                </Link>
              </Button>
            ) : null}
            {can(P.patientsEdit) ? (
              <Button onClick={() => setEditOpen(true)}>
                <Pencil /> {t("common.edit")}
              </Button>
            ) : null}
          </>
        }
      />

      <Tabs value={tab} onValueChange={setTab}>
        <TabsList>
          {(["overview", "appointments", "visits", "payments", "documents", "history"] as const).map((k) => (
            <TabsTrigger key={k} value={k}>
              {t(`patients.tabs.${k}`)}
            </TabsTrigger>
          ))}
        </TabsList>

        <TabsContent value="overview">
          <div className="grid gap-4 lg:grid-cols-3">
            <Card>
              <CardHeader>
                <CardTitle>Данные пациента</CardTitle>
              </CardHeader>
              <CardContent>
                <Row label={t("patients.phone")} value={formatPhone(p.phone)} />
                {p.phoneExtra ? <Row label={t("patients.phoneExtra")} value={formatPhone(p.phoneExtra)} /> : null}
                <Row label={t("patients.birthDate")} value={formatDate(p.birthDate)} />
                <Row label={t("patients.gender")} value={t(`patients.genders.${p.gender}`)} />
                <Row label={t("patients.iin")} value={p.iin} />
                <Row label={t("patients.email")} value={p.email} />
                <Row label={t("patients.address")} value={p.address} />
                <Row label={t("patients.source")} value={p.sourceName} />
                <Row label={t("patients.createdAt")} value={formatDate(p.createdAt)} />
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle>{t("patients.balance")}</CardTitle>
              </CardHeader>
              <CardContent>
                <div className={`mb-2 text-2xl font-semibold tabular ${p.balance < 0 ? "text-destructive" : p.balance > 0 ? "text-success" : ""}`}>
                  {formatMoney(p.balance)}
                </div>
                <Row label={t("patients.visitsCount")} value={p.visitsCount} />
                <Row label={t("patients.totalPaid")} value={formatMoney(p.totalPaid)} />
                <Row label={t("patients.lastVisit")} value={formatDate(p.lastVisitAt)} />
              </CardContent>
            </Card>
            <Card>
              <CardHeader>
                <CardTitle>{t("patients.nextAppointment")}</CardTitle>
              </CardHeader>
              <CardContent className="space-y-2">
                {upcoming.length === 0 ? <p className="text-sm text-muted-foreground">—</p> : null}
                {upcoming.slice(0, 5).map((a) => (
                  <div key={a.id} className="rounded-md border p-2 text-sm">
                    <div className="font-medium">{formatDateTime(a.startsAt)}</div>
                    <div className="text-muted-foreground">{a.doctorName}</div>
                  </div>
                ))}
                {p.notes ? <p className="whitespace-pre-wrap border-t pt-2 text-sm">{p.notes}</p> : null}
              </CardContent>
            </Card>
          </div>
        </TabsContent>

        <TabsContent value="appointments">
          <Card>
            {appointments.isLoading ? (
              <TableSkeleton />
            ) : (
              <Table>
                <THead>
                  <TR>
                    <TH>{t("common.date")}</TH>
                    <TH>{t("schedule.doctor")}</TH>
                    <TH>{t("common.status")}</TH>
                    <TH>{t("common.comment")}</TH>
                  </TR>
                </THead>
                <TBody>
                  {(appointments.data ?? []).map((a) => (
                    <TR key={a.id}>
                      <TD className="whitespace-nowrap">{formatDateTime(a.startsAt)}</TD>
                      <TD>{a.doctorName}</TD>
                      <TD>
                        <span className="rounded px-1.5 py-0.5 text-xs" style={{ background: STATUS_COLORS[a.status].bg, color: STATUS_COLORS[a.status].text }}>
                          {t(`schedule.status.${a.status}`)}
                        </span>
                      </TD>
                      <TD className="text-muted-foreground">{a.comment}</TD>
                    </TR>
                  ))}
                </TBody>
              </Table>
            )}
          </Card>
        </TabsContent>

        <TabsContent value="visits">
          <Card>
            {visits.isLoading ? (
              <TableSkeleton />
            ) : (visits.data ?? []).length === 0 ? (
              <p className="p-4 text-sm text-muted-foreground">{t("common.noData")}</p>
            ) : (
              <Table>
                <THead>
                  <TR>
                    <TH>{t("common.date")}</TH>
                    <TH>{t("schedule.doctor")}</TH>
                    <TH>{t("common.status")}</TH>
                    <TH className="text-right">{t("common.total")}</TH>
                    <TH className="text-right">{t("patients.totalPaid")}</TH>
                  </TR>
                </THead>
                <TBody>
                  {(visits.data ?? []).map((v) => (
                    <TR key={v.id}>
                      <TD>
                        <Link className="text-primary hover:underline" href={href(`visits/${v.id}`)}>
                          {formatDateTime(v.openedAt)}
                        </Link>
                      </TD>
                      <TD>{v.doctorName}</TD>
                      <TD>{v.status}</TD>
                      <TD className="tabular text-right">{formatMoney(v.total)}</TD>
                      <TD className="tabular text-right">{formatMoney(v.paidTotal)}</TD>
                    </TR>
                  ))}
                </TBody>
              </Table>
            )}
          </Card>
        </TabsContent>

        <TabsContent value="payments">
          <div className="mb-3 grid gap-3 sm:grid-cols-4">
            {balance.data
              ? (
                  [
                    ["patients.balance", balance.data.balance],
                    ["patients.totalBilled", balance.data.totalBilled],
                    ["patients.totalPaid", balance.data.totalPaid],
                    ["patients.debt", balance.data.debt],
                  ] as const
                ).map(([k, v]) => (
                  <Card key={k} className="p-3">
                    <div className="text-xs text-muted-foreground">{t(k)}</div>
                    <div className="text-lg font-semibold tabular">{formatMoney(v)}</div>
                  </Card>
                ))
              : null}
          </div>
          <Card>
            {(payments.data ?? []).length === 0 ? (
              <p className="p-4 text-sm text-muted-foreground">{t("common.noData")}</p>
            ) : (
              <Table>
                <THead>
                  <TR>
                    <TH>{t("common.date")}</TH>
                    <TH>Тип</TH>
                    <TH>Способ</TH>
                    <TH className="text-right">{t("common.amount")}</TH>
                  </TR>
                </THead>
                <TBody>
                  {(payments.data ?? []).map((x) => (
                    <TR key={x.id}>
                      <TD>{formatDateTime(x.createdAt)}</TD>
                      <TD>{x.type}</TD>
                      <TD>{x.method}</TD>
                      <TD className={`tabular text-right ${x.type === "Refund" ? "text-destructive" : ""}`}>{formatMoney(x.type === "Refund" ? -x.amount : x.amount)}</TD>
                    </TR>
                  ))}
                </TBody>
              </Table>
            )}
          </Card>
        </TabsContent>

        <TabsContent value="documents">
          <Card className="p-4 text-sm text-muted-foreground">{t("patients.documentsSoon")}</Card>
        </TabsContent>

        <TabsContent value="history">
          <Card>{history.isLoading ? <TableSkeleton /> : <AuditList items={history.data?.items ?? []} />}</Card>
        </TabsContent>
      </Tabs>

      <PatientFormDialog open={editOpen} onOpenChange={setEditOpen} patient={p} />
    </div>
  );
}
