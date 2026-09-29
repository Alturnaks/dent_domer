"use client";

import * as React from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { CalendarOff, Plus, Trash2 } from "lucide-react";
import { PageHeader, EmptyState } from "@/components/ui/empty-state";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input, NativeSelect } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Skeleton, TableSkeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { useConfirm } from "@/components/ui/confirm";
import { TimeBlockDialog } from "@/components/schedule/time-block-dialog";
import { addDays, hhmm, parseHhmm, todayIn, zoned } from "@/components/schedule/tz";
import { api, ApiError } from "@/lib/api-client";
import { useAuth, useBranch } from "@/lib/auth";
import { useBranches, useChairs, useDoctors } from "@/lib/queries";
import { formatDate } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import { cn } from "@/lib/utils";
import type { DoctorSchedule, ScheduleException, Schemas, TimeBlock } from "@/lib/types";

const WEEK_ORDER = [1, 2, 3, 4, 5, 6, 0];
type Interval = { from: string; to: string; chairId: string };
type DayRow = { working: boolean; intervals: Interval[] };

function errText(e: unknown) {
  return e instanceof ApiError ? e.userMessage : t("errors.INTERNAL_ERROR");
}
const toTime = (s: string) => (s.length === 5 ? `${s}:00` : s);
const dayName = (wd: number) => t(`doctorSchedules.days.${wd}`);

export default function DoctorSchedulesPage() {
  const { can, me } = useAuth();
  const { branchId: ctxBranch, timezone: tz } = useBranch();
  const branches = useBranches();
  const [pickedBranch, setPickedBranch] = React.useState<string | null>(null);
  const branchId = ctxBranch ?? pickedBranch ?? branches.data?.find((b) => b.isActive)?.id ?? me?.branches[0]?.id ?? null;
  const doctors = useDoctors(branchId);
  const [doctorId, setDoctorId] = React.useState<string>("");
  const active = (doctors.data ?? []).filter((d) => d.isActive);
  const selected = active.find((d) => d.membershipId === doctorId) ?? active[0] ?? null;

  if (!can(P.doctorSchedulesManage)) {
    return (
      <div>
        <PageHeader title={t("doctorSchedules.title")} />
        <EmptyState title={t("errors.FORBIDDEN")} />
      </div>
    );
  }

  return (
    <div>
      <PageHeader
        title={t("doctorSchedules.title")}
        actions={
          !ctxBranch && (branches.data?.length ?? 0) > 1 ? (
            <NativeSelect className="w-52" value={branchId ?? ""} onChange={(e) => setPickedBranch(e.target.value)} aria-label={t("common.branch")}>
              {branches.data!.filter((b) => b.isActive).map((b) => (
                <option key={b.id} value={b.id}>
                  {b.name}
                </option>
              ))}
            </NativeSelect>
          ) : null
        }
      />
      {!branchId ? (
        <EmptyState title={t("schedule.selectBranch")} />
      ) : doctors.isLoading ? (
        <Skeleton className="h-96 w-full" />
      ) : active.length === 0 ? (
        <EmptyState title={t("doctorSchedules.noDoctors")} />
      ) : (
        <div className="grid gap-4 lg:grid-cols-[16rem_1fr]">
          <Card className="h-fit p-1.5">
            {active.map((d) => (
              <button
                key={d.membershipId}
                type="button"
                onClick={() => setDoctorId(d.membershipId)}
                className={cn(
                  "flex w-full items-center gap-2 rounded-md px-2.5 py-2 text-left text-sm hover:bg-muted",
                  selected?.membershipId === d.membershipId && "bg-primary/10 font-medium text-primary",
                )}
              >
                <span className="h-2.5 w-2.5 shrink-0 rounded-full" style={{ background: d.color ?? "var(--muted-foreground)" }} />
                <span className="min-w-0">
                  <span className="block truncate">{d.fullName}</span>
                  {d.specialty ? <span className="block truncate text-xs font-normal text-muted-foreground">{d.specialty}</span> : null}
                </span>
              </button>
            ))}
          </Card>
          {selected ? <DoctorDetails key={`${branchId}:${selected.membershipId}`} branchId={branchId} doctorId={selected.membershipId} doctorName={selected.fullName} tz={tz} /> : null}
        </div>
      )}
    </div>
  );
}

function DoctorDetails({ branchId, doctorId, doctorName, tz }: { branchId: string; doctorId: string; doctorName: string; tz: string }) {
  return (
    <Tabs defaultValue="template">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-base font-semibold">{doctorName}</h2>
        <TabsList>
          <TabsTrigger value="template">{t("doctorSchedules.template")}</TabsTrigger>
          <TabsTrigger value="exceptions">{t("doctorSchedules.exceptions")}</TabsTrigger>
          <TabsTrigger value="blocks">{t("doctorSchedules.blocks")}</TabsTrigger>
        </TabsList>
      </div>
      <TabsContent value="template">
        <TemplateEditor branchId={branchId} doctorId={doctorId} tz={tz} />
      </TabsContent>
      <TabsContent value="exceptions">
        <Exceptions branchId={branchId} doctorId={doctorId} tz={tz} />
      </TabsContent>
      <TabsContent value="blocks">
        <Blocks branchId={branchId} doctorId={doctorId} doctorName={doctorName} tz={tz} />
      </TabsContent>
    </Tabs>
  );
}

// ---------- Недельный шаблон ----------

function currentTemplate(rows: DoctorSchedule[], today: string): { rows: DoctorSchedule[]; future: string | null } {
  const effective = rows.filter((r) => r.validFrom <= today && (!r.validTo || r.validTo >= today));
  const futureFroms = [...new Set(rows.filter((r) => r.validFrom > today).map((r) => r.validFrom))].sort();
  if (effective.length) return { rows: effective, future: futureFroms[0] ?? null };
  if (futureFroms.length) return { rows: rows.filter((r) => r.validFrom === futureFroms[0]), future: futureFroms[0] };
  return { rows: [], future: null };
}

function TemplateEditor({ branchId, doctorId, tz }: { branchId: string; doctorId: string; tz: string }) {
  const qc = useQueryClient();
  const chairs = useChairs(branchId);
  const today = todayIn(tz);
  const q = useQuery({
    queryKey: ["doctor-schedules", branchId, doctorId],
    queryFn: () => api<DoctorSchedule[]>("/doctor-schedules", { query: { branch_id: branchId, doctor_id: doctorId } }),
  });
  const [days, setDays] = React.useState<Record<number, DayRow> | null>(null);
  const [validFrom, setValidFrom] = React.useState(today);
  const [dirty, setDirty] = React.useState(false);
  const cur = React.useMemo(() => currentTemplate(q.data ?? [], today), [q.data, today]);

  React.useEffect(() => {
    if (!q.data) return;
    const next: Record<number, DayRow> = {};
    for (const wd of WEEK_ORDER) {
      const rows = cur.rows.filter((r) => r.weekday === wd).sort((a, b) => a.startTime.localeCompare(b.startTime));
      next[wd] = rows.length
        ? { working: true, intervals: rows.map((r) => ({ from: r.startTime.slice(0, 5), to: r.endTime.slice(0, 5), chairId: r.chairId ?? "" })) }
        : { working: false, intervals: [{ from: "09:00", to: "18:00", chairId: "" }] };
    }
    setDays(next);
    setDirty(false);
  }, [q.data, cur]);

  const update = (wd: number, fn: (d: DayRow) => DayRow) => {
    setDays((prev) => (prev ? { ...prev, [wd]: fn(prev[wd]) } : prev));
    setDirty(true);
  };

  const errors = React.useMemo(() => {
    const e: Record<number, string> = {};
    if (!days) return e;
    for (const wd of WEEK_ORDER) {
      const d = days[wd];
      if (!d.working) continue;
      const iv = d.intervals.map((i) => [parseHhmm(i.from), parseHhmm(i.to)] as const);
      if (iv.some(([a, b]) => a === null || b === null || b <= a)) e[wd] = t("schedule.endBeforeStart");
      else {
        const sorted = [...iv].sort((x, y) => x[0]! - y[0]!);
        for (let i = 1; i < sorted.length; i++) if (sorted[i][0]! < sorted[i - 1][1]!) e[wd] = t("doctorSchedules.overlap");
      }
    }
    return e;
  }, [days]);

  const save = useMutation({
    mutationFn: () => {
      const body: Schemas["DoctorWeekTemplateRequest"] = {
        membershipId: doctorId,
        branchId,
        validFrom: validFrom || null,
        days: WEEK_ORDER.flatMap((wd) =>
          days![wd].working ? days![wd].intervals.map((i) => ({ weekday: wd, startTime: toTime(i.from), endTime: toTime(i.to), chairId: i.chairId || null })) : [],
        ),
      };
      return api<DoctorSchedule[]>("/doctor-schedules/template", { method: "PUT", body });
    },
    onSuccess: () => {
      toast.success(t("doctorSchedules.saved"));
      qc.invalidateQueries({ queryKey: ["doctor-schedules"] });
      qc.invalidateQueries({ queryKey: ["calendar"] });
      qc.invalidateQueries({ queryKey: ["slots"] });
    },
    onError: (e) => toast.error(errText(e)),
  });

  if (q.isLoading || !days) return <TableSkeleton rows={7} cols={4} />;
  if (q.isError) return <EmptyState title={errText(q.error)} action={<Button variant="outline" onClick={() => q.refetch()}>{t("common.retry")}</Button>} />;

  const hours = WEEK_ORDER.reduce((sum, wd) => {
    const d = days[wd];
    if (!d.working) return sum;
    return sum + d.intervals.reduce((s, i) => s + Math.max(0, (parseHhmm(i.to) ?? 0) - (parseHhmm(i.from) ?? 0)), 0);
  }, 0);

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("doctorSchedules.template")}</CardTitle>
        <CardDescription>
          {cur.rows.length === 0 ? t("doctorSchedules.noSchedule") : t("doctorSchedules.currentFrom", { date: formatDate(cur.rows[0].validFrom) })}
          {cur.future && cur.rows[0]?.validFrom !== cur.future ? ` · ${t("doctorSchedules.futureFrom", { date: formatDate(cur.future) })}` : ""}
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-1">
        {WEEK_ORDER.map((wd) => {
          const d = days[wd];
          return (
            <div key={wd} className={cn("grid items-start gap-2 rounded-md px-2 py-1.5 sm:grid-cols-[9rem_1fr]", !d.working && "opacity-70")} data-testid={`template-day-${wd}`}>
              <label className="flex h-8 items-center gap-2 text-sm font-medium">
                <Checkbox checked={d.working} onCheckedChange={(v) => update(wd, (x) => ({ ...x, working: v === true }))} />
                {dayName(wd)}
              </label>
              {d.working ? (
                <div className="space-y-1.5">
                  {d.intervals.map((iv, i) => (
                    <div key={i} className="flex flex-wrap items-center gap-2">
                      <Input
                        type="time"
                        step={900}
                        className="h-8 w-28"
                        value={iv.from}
                        aria-label={t("doctorSchedules.from")}
                        onChange={(e) => update(wd, (x) => ({ ...x, intervals: x.intervals.map((y, j) => (j === i ? { ...y, from: e.target.value } : y)) }))}
                      />
                      <span className="text-muted-foreground">—</span>
                      <Input
                        type="time"
                        step={900}
                        className="h-8 w-28"
                        value={iv.to}
                        aria-label={t("doctorSchedules.to")}
                        onChange={(e) => update(wd, (x) => ({ ...x, intervals: x.intervals.map((y, j) => (j === i ? { ...y, to: e.target.value } : y)) }))}
                      />
                      <NativeSelect
                        className="h-8 w-36"
                        value={iv.chairId}
                        aria-label={t("schedule.chair")}
                        onChange={(e) => update(wd, (x) => ({ ...x, intervals: x.intervals.map((y, j) => (j === i ? { ...y, chairId: e.target.value } : y)) }))}
                      >
                        <option value="">{t("schedule.noChair")}</option>
                        {(chairs.data ?? []).filter((c) => c.isActive || c.id === iv.chairId).map((c) => (
                          <option key={c.id} value={c.id}>
                            {c.name}
                          </option>
                        ))}
                      </NativeSelect>
                      {d.intervals.length > 1 ? (
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon-sm"
                          aria-label={t("common.delete")}
                          onClick={() => update(wd, (x) => ({ ...x, intervals: x.intervals.filter((_, j) => j !== i) }))}
                        >
                          <Trash2 />
                        </Button>
                      ) : null}
                      {i === d.intervals.length - 1 ? (
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          onClick={() => {
                            const last = parseHhmm(iv.to) ?? 14 * 60;
                            update(wd, (x) => ({ ...x, intervals: [...x.intervals, { from: hhmm(last + 60), to: hhmm(Math.min(last + 240, 23 * 60)), chairId: iv.chairId }] }));
                          }}
                        >
                          <Plus /> {t("doctorSchedules.addInterval")}
                        </Button>
                      ) : null}
                    </div>
                  ))}
                  {errors[wd] ? <p className="text-xs text-destructive">{errors[wd]}</p> : null}
                </div>
              ) : (
                <span className="flex h-8 items-center text-sm text-muted-foreground">{t("doctorSchedules.dayOff")}</span>
              )}
            </div>
          );
        })}
        <div className="flex flex-wrap items-end justify-between gap-3 border-t pt-3">
          <div className="flex flex-wrap items-end gap-3">
            <Field label={t("doctorSchedules.validFrom")} hint={t("doctorSchedules.validFromHint")}>
              <Input type="date" className="w-44" value={validFrom} min={today} onChange={(e) => { setValidFrom(e.target.value); setDirty(true); }} />
            </Field>
            <p className="pb-6 text-sm text-muted-foreground">{t("doctorSchedules.hoursPerWeek", { h: Math.round((hours / 60) * 10) / 10 })}</p>
          </div>
          <div className="flex items-center gap-2 pb-6">
            {dirty ? <span className="text-xs text-muted-foreground">{t("common.unsavedChanges")}</span> : null}
            <Button onClick={() => save.mutate()} loading={save.isPending} disabled={Object.keys(errors).length > 0 || !validFrom} data-testid="save-template">
              {t("doctorSchedules.saveTemplate")}
            </Button>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

// ---------- Исключения ----------

const EXC_TYPES: Schemas["ScheduleExceptionType"][] = ["Vacation", "Sick", "DayOff", "ExtraShift"];
const EXC_VARIANT: Record<Schemas["ScheduleExceptionType"], "warning" | "destructive" | "muted" | "success"> = {
  Vacation: "warning",
  Sick: "destructive",
  DayOff: "muted",
  ExtraShift: "success",
};

function Exceptions({ branchId, doctorId, tz }: { branchId: string; doctorId: string; tz: string }) {
  const qc = useQueryClient();
  const today = todayIn(tz);
  const [showPast, setShowPast] = React.useState(false);
  const q = useQuery({
    queryKey: ["schedule-exceptions", doctorId, showPast],
    queryFn: () => api<ScheduleException[]>("/schedule-exceptions", { query: { doctor_id: doctorId, from: showPast ? addDays(today, -365) : today } }),
  });
  const [type, setType] = React.useState<Schemas["ScheduleExceptionType"]>("Vacation");
  const [dateFrom, setDateFrom] = React.useState(today);
  const [dateTo, setDateTo] = React.useState(today);
  const [allDay, setAllDay] = React.useState(true);
  const [from, setFrom] = React.useState("09:00");
  const [to, setTo] = React.useState("18:00");
  const [comment, setComment] = React.useState("");

  const needTimes = type === "ExtraShift" || !allDay;
  const timeErr = needTimes && ((parseHhmm(to) ?? 0) <= (parseHhmm(from) ?? 0));
  const dateErr = dateTo < dateFrom;

  const add = useMutation({
    mutationFn: () => {
      const body: Schemas["ScheduleExceptionRequest"] = {
        membershipId: doctorId,
        branchId,
        dateFrom,
        dateTo,
        type,
        startTime: needTimes ? toTime(from) : null,
        endTime: needTimes ? toTime(to) : null,
        comment: comment.trim() || null,
      };
      return api<ScheduleException>("/schedule-exceptions", { method: "POST", body });
    },
    onSuccess: () => {
      toast.success(t("doctorSchedules.exceptionAdded"));
      setComment("");
      qc.invalidateQueries({ queryKey: ["schedule-exceptions"] });
      qc.invalidateQueries({ queryKey: ["calendar"] });
      qc.invalidateQueries({ queryKey: ["slots"] });
    },
    onError: (e) => toast.error(errText(e)),
  });

  const rows = (q.data ?? []).filter((e) => !e.branchId || e.branchId === branchId);

  return (
    <div className="space-y-3">
      <Card>
        <CardHeader>
          <CardTitle>{t("doctorSchedules.addException")}</CardTitle>
        </CardHeader>
        <CardContent>
          <form
            className="grid gap-3 sm:grid-cols-4"
            onSubmit={(e) => {
              e.preventDefault();
              if (!timeErr && !dateErr) add.mutate();
            }}
          >
            <Field label={t("doctorSchedules.type")}>
              <NativeSelect value={type} onChange={(e) => setType(e.target.value as Schemas["ScheduleExceptionType"])}>
                {EXC_TYPES.map((x) => (
                  <option key={x} value={x}>
                    {t(`doctorSchedules.types.${x}`)}
                  </option>
                ))}
              </NativeSelect>
            </Field>
            <Field label={t("common.from")}>
              <Input type="date" value={dateFrom} onChange={(e) => { setDateFrom(e.target.value); if (dateTo < e.target.value) setDateTo(e.target.value); }} required />
            </Field>
            <Field label={t("common.to")} error={dateErr ? t("doctorSchedules.dateToError") : undefined}>
              <Input type="date" value={dateTo} min={dateFrom} onChange={(e) => setDateTo(e.target.value)} required />
            </Field>
            <div className="flex items-end pb-2">
              {type !== "ExtraShift" ? (
                <label className="flex items-center gap-2 text-sm">
                  <Checkbox checked={allDay} onCheckedChange={(v) => setAllDay(v === true)} />
                  {t("doctorSchedules.allDay")}
                </label>
              ) : null}
            </div>
            {needTimes ? (
              <>
                <Field label={t("doctorSchedules.from")}>
                  <Input type="time" step={900} value={from} onChange={(e) => setFrom(e.target.value)} />
                </Field>
                <Field label={t("doctorSchedules.to")} error={timeErr ? t("schedule.endBeforeStart") : undefined}>
                  <Input type="time" step={900} value={to} onChange={(e) => setTo(e.target.value)} />
                </Field>
              </>
            ) : null}
            <Field label={t("schedule.comment")} className={needTimes ? "sm:col-span-2" : "sm:col-span-4"}>
              <Input value={comment} onChange={(e) => setComment(e.target.value)} />
            </Field>
            <div className="flex justify-end sm:col-span-4">
              <Button type="submit" loading={add.isPending} disabled={timeErr || dateErr}>
                <Plus /> {t("common.add")}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
      <Card>
        <div className="flex items-center justify-between p-3 pb-0">
          <h3 className="text-sm font-semibold">{t("doctorSchedules.exceptions")}</h3>
          <label className="flex items-center gap-2 text-sm">
            <Checkbox checked={showPast} onCheckedChange={(v) => setShowPast(v === true)} />
            {t("doctorSchedules.showPast")}
          </label>
        </div>
        {q.isLoading ? (
          <TableSkeleton rows={3} cols={4} />
        ) : rows.length === 0 ? (
          <EmptyState className="m-3" icon={<CalendarOff className="h-8 w-8" />} title={t("doctorSchedules.noExceptions")} />
        ) : (
          <Table>
            <THead>
              <TR>
                <TH>{t("doctorSchedules.type")}</TH>
                <TH>{t("doctorSchedules.period")}</TH>
                <TH>{t("doctorSchedules.time")}</TH>
                <TH>{t("schedule.comment")}</TH>
              </TR>
            </THead>
            <TBody>
              {rows.map((e) => (
                <TR key={e.id}>
                  <TD>
                    <Badge variant={EXC_VARIANT[e.type]}>{t(`doctorSchedules.types.${e.type}`)}</Badge>
                  </TD>
                  <TD className="whitespace-nowrap">
                    {e.dateFrom === e.dateTo ? formatDate(e.dateFrom) : `${formatDate(e.dateFrom)} – ${formatDate(e.dateTo)}`}
                  </TD>
                  <TD className="whitespace-nowrap">{e.startTime && e.endTime ? `${e.startTime.slice(0, 5)}–${e.endTime.slice(0, 5)}` : t("doctorSchedules.allDay")}</TD>
                  <TD className="text-sm">{e.comment ?? "—"}</TD>
                </TR>
              ))}
            </TBody>
          </Table>
        )}
      </Card>
    </div>
  );
}

// ---------- Блокировки ----------

function Blocks({ branchId, doctorId, doctorName, tz }: { branchId: string; doctorId: string; doctorName: string; tz: string }) {
  const qc = useQueryClient();
  const confirm = useConfirm();
  const today = todayIn(tz);
  const [open, setOpen] = React.useState(false);
  const q = useQuery({
    queryKey: ["time-blocks", branchId, today],
    queryFn: () => api<TimeBlock[]>("/time-blocks", { query: { branch_id: branchId, from: `${today}T00:00:00Z`, to: `${addDays(today, 120)}T00:00:00Z` } }),
  });
  const del = useMutation({
    mutationFn: (b: TimeBlock) => api(`/time-blocks/${b.id}`, { method: "DELETE" }),
    onSuccess: () => {
      toast.success(t("schedule.blockDeleted"));
      qc.invalidateQueries({ queryKey: ["time-blocks"] });
      qc.invalidateQueries({ queryKey: ["calendar"] });
    },
    onError: (e) => toast.error(errText(e)),
  });
  const rows = (q.data ?? []).filter((b) => b.doctorId === doctorId || b.doctorId === null);
  const initial = React.useMemo(() => ({ doctorId, date: today, minutes: 13 * 60 }), [doctorId, today]);

  return (
    <Card>
      <div className="flex items-center justify-between p-3 pb-0">
        <div>
          <h3 className="text-sm font-semibold">{t("doctorSchedules.blocks")}</h3>
          <p className="text-xs text-muted-foreground">{t("doctorSchedules.blocksHint")}</p>
        </div>
        <Button size="sm" onClick={() => setOpen(true)}>
          <Plus /> {t("schedule.blockTitle")}
        </Button>
      </div>
      {q.isLoading ? (
        <TableSkeleton rows={3} cols={4} />
      ) : rows.length === 0 ? (
        <EmptyState className="m-3" title={t("doctorSchedules.noBlocks")} />
      ) : (
        <Table>
          <THead>
            <TR>
              <TH>{t("common.date")}</TH>
              <TH>{t("doctorSchedules.time")}</TH>
              <TH>{t("schedule.doctor")}</TH>
              <TH>{t("schedule.blockReason")}</TH>
              <TH className="w-px" />
            </TR>
          </THead>
          <TBody>
            {rows.map((b) => {
              const s = zoned(b.startsAt, tz);
              const e = zoned(b.endsAt, tz);
              return (
                <TR key={b.id}>
                  <TD className="whitespace-nowrap">
                    {formatDate(s.date)}
                    {e.date !== s.date ? ` – ${formatDate(e.date)}` : ""}
                  </TD>
                  <TD className="whitespace-nowrap tabular-nums">
                    {hhmm(s.minutes)}–{hhmm(e.minutes)}
                  </TD>
                  <TD>{b.doctorId ? doctorName : <Badge variant="muted">{t("schedule.allDoctors")}</Badge>}</TD>
                  <TD className="text-sm">{b.reason ?? "—"}</TD>
                  <TD>
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      aria-label={t("common.delete")}
                      onClick={async () => {
                        if (await confirm({ title: t("schedule.blockDeleteConfirm"), confirmText: t("common.delete"), destructive: true })) del.mutate(b);
                      }}
                    >
                      <Trash2 />
                    </Button>
                  </TD>
                </TR>
              );
            })}
          </TBody>
        </Table>
      )}
      <TimeBlockDialog open={open} onOpenChange={setOpen} branchId={branchId} tz={tz} doctors={[{ id: doctorId, title: doctorName }]} initial={initial} />
    </Card>
  );
}
