"use client";

import * as React from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { AlertTriangle, Ban, CalendarClock, ChevronLeft, ChevronRight, Plus, RefreshCw } from "lucide-react";
import { PageHeader, EmptyState } from "@/components/ui/empty-state";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect } from "@/components/ui/input";
import { Checkbox } from "@/components/ui/checkbox";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useConfirm } from "@/components/ui/confirm";
import { CalendarGrid, needsReschedule, type GridColumn } from "@/components/schedule/calendar-grid";
import { AppointmentCreateDialog, type CreateInitial } from "@/components/schedule/appointment-create-dialog";
import { AppointmentPanel } from "@/components/schedule/appointment-panel";
import { TimeBlockDialog } from "@/components/schedule/time-block-dialog";
import { WaitlistPanel, toWaitlistRequest } from "@/components/schedule/waitlist-panel";
import { addDays, formatDayShort, formatDayTitle, hhmm, startOfWeek, todayIn, toIso, zoned } from "@/components/schedule/tz";
import { formatDateTime } from "@/lib/format";
import { api, ApiError } from "@/lib/api-client";
import { useAuth, useBranch, useOrgHref } from "@/lib/auth";
import { useBranches } from "@/lib/queries";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import { STATUS_COLORS } from "@/lib/status";
import type { Appointment, AppointmentStatus, CalendarResponse, Patient, TimeBlock, Waitlist } from "@/lib/types";

type View = "day" | "week";

export default function SchedulePage() {
  return (
    <React.Suspense fallback={<Skeleton className="h-96 w-full" />}>
      <ScheduleInner />
    </React.Suspense>
  );
}

function ScheduleInner() {
  const { can, me } = useAuth();
  const { href } = useOrgHref();
  const { branchId: ctxBranch, timezone: tz } = useBranch();
  const branches = useBranches();
  const qc = useQueryClient();
  const confirm = useConfirm();
  const search = useSearchParams();

  const [pickedBranch, setPickedBranch] = React.useState<string | null>(null);
  const branchId = ctxBranch ?? pickedBranch ?? branches.data?.find((b) => b.isActive)?.id ?? me?.branches[0]?.id ?? null;

  const today = todayIn(tz);
  const [tab, setTab] = React.useState("calendar");
  const [view, setView] = React.useState<View>("day");
  const [date, setDate] = React.useState(today);
  const [weekDoctor, setWeekDoctor] = React.useState<string>("");
  const [showCancelled, setShowCancelled] = React.useState(false);
  const [createInit, setCreateInit] = React.useState<CreateInitial | null>(null);
  const [createOpen, setCreateOpen] = React.useState(false);
  const [selected, setSelected] = React.useState<Appointment | null>(null);
  const [blockInit, setBlockInit] = React.useState<{ doctorId?: string | null; date: string; minutes?: number | null } | null>(null);
  const [bookingWaitlist, setBookingWaitlist] = React.useState<Waitlist | null>(null);

  const manage = can(P.scheduleManage);
  const canBlock = can(P.scheduleManage, P.doctorSchedulesManage);

  const weekStart = startOfWeek(date);
  const from = view === "day" ? date : weekStart;
  const to = view === "day" ? date : addDays(weekStart, 6);

  // Список врачей берём из календаря (бэкенд сам ограничивает для schedule.view_own).
  const dayCal = useQuery({
    queryKey: ["calendar", branchId, "day", date],
    queryFn: () => api<CalendarResponse>("/calendar", { query: { branch_id: branchId!, from: date, to: date, view: "day" } }),
    enabled: !!branchId && view === "day",
    refetchInterval: 60_000,
  });
  const resourcesQuery = useQuery({
    queryKey: ["calendar", branchId, "resources", weekStart],
    queryFn: () => api<CalendarResponse>("/calendar", { query: { branch_id: branchId!, from: weekStart, to: weekStart, view: "day" } }),
    enabled: !!branchId && view === "week",
    staleTime: 5 * 60_000,
  });
  const rawResources = view === "day" ? dayCal.data?.resources : resourcesQuery.data?.resources;
  const resources = React.useMemo(() => rawResources ?? [], [rawResources]);
  const effectiveWeekDoctor = resources.some((r) => r.id === weekDoctor) ? weekDoctor : (resources[0]?.id ?? "");
  const weekCal = useQuery({
    queryKey: ["calendar", branchId, "week", weekStart, effectiveWeekDoctor],
    queryFn: () =>
      api<CalendarResponse>("/calendar", { query: { branch_id: branchId!, from, to, view: "week", doctor_ids: [effectiveWeekDoctor] } }),
    enabled: !!branchId && view === "week" && !!effectiveWeekDoctor,
    refetchInterval: 60_000,
  });
  const cal = view === "day" ? dayCal : weekCal;
  const data = cal.data;
  const misplaced = React.useMemo(
    () => (data ? data.appointments.filter((a) => needsReschedule(a, data)).sort((x, y) => x.startsAt.localeCompare(y.startsAt)) : []),
    [data],
  );
  const doctorsList = React.useMemo(() => resources.map((r) => ({ id: r.id, title: r.title })), [resources]);

  // Предзаполнение из карточки пациента: /schedule?patient=<id>
  const patientParam = search.get("patient");
  const prefillDone = React.useRef<string | null>(null);
  React.useEffect(() => {
    if (!patientParam || !manage || prefillDone.current === patientParam) return;
    prefillDone.current = patientParam;
    api<Patient>(`/patients/${patientParam}`)
      .then((p) => {
        setCreateInit({ date, patient: { id: p.id, fullName: p.fullName, phone: p.phone, birthDate: p.birthDate, isVip: p.isVip } });
        setCreateOpen(true);
      })
      .catch(() => undefined);
  }, [patientParam, manage, date]);

  // Горячие клавиши
  React.useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const el = e.target as HTMLElement | null;
      if (el && (el.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(el.tagName))) return;
      if (e.ctrlKey || e.metaKey || e.altKey || createOpen || selected || blockInit || tab !== "calendar") return;
      const step = view === "day" ? 1 : 7;
      if (e.key === "ArrowLeft") setDate((d) => addDays(d, -step));
      else if (e.key === "ArrowRight") setDate((d) => addDays(d, step));
      else if (e.key === "t" || e.key === "е" || e.key === "T") setDate(todayIn(tz));
      else if ((e.key === "n" || e.key === "т") && manage) {
        setCreateInit({ date, doctorId: view === "week" ? effectiveWeekDoctor : null });
        setCreateOpen(true);
      } else return;
      e.preventDefault();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [view, date, tz, manage, createOpen, selected, blockInit, tab, effectiveWeekDoctor]);

  const columns: GridColumn[] = React.useMemo(() => {
    if (!data) return [];
    const excNote = (doctorId: string, d: string) => {
      const e = data.exceptions.find((x) => x.membershipId === doctorId && x.dateFrom <= d && x.dateTo >= d);
      return e ? t(`doctorSchedules.types.${e.type}`) : null;
    };
    const notWorking = (doctorId: string, d: string) => !data.working.some((w) => w.doctorId === doctorId && zoned(w.start, tz).date === d);
    if (view === "day") {
      return data.resources.map((r) => ({
        key: r.id,
        doctorId: r.id,
        date,
        title: r.title,
        subtitle: r.specialty,
        color: r.color,
        note: excNote(r.id, date) ?? (notWorking(r.id, date) ? t("schedule.dayOff") : null),
      }));
    }
    const r = data.resources.find((x) => x.id === effectiveWeekDoctor) ?? resources.find((x) => x.id === effectiveWeekDoctor);
    return Array.from({ length: 7 }, (_, i) => {
      const d = addDays(weekStart, i);
      const count = data.appointments.filter((a) => a.doctorId === effectiveWeekDoctor && zoned(a.startsAt, tz).date === d && a.status !== "Cancelled").length;
      return {
        key: d,
        doctorId: effectiveWeekDoctor,
        date: d,
        title: formatDayShort(d),
        subtitle: count ? t("schedule.appointmentsCount", { n: count }) : null,
        color: r?.color,
        note: excNote(effectiveWeekDoctor, d) ?? (notWorking(effectiveWeekDoctor, d) ? t("schedule.dayOff") : null),
        highlight: d === today,
      };
    });
  }, [data, view, date, weekStart, effectiveWeekDoctor, resources, tz, today]);

  // Диапазон сетки: 08:00–21:00, расширяем по рабочим интервалам и записям.
  const [startMin, endMin] = React.useMemo(() => {
    let s = 8 * 60;
    let e = 21 * 60;
    if (data) {
      for (const w of data.working) {
        const a = zoned(w.start, tz);
        const b = zoned(w.end, tz);
        s = Math.min(s, a.minutes);
        e = Math.max(e, b.date > a.date ? 1440 : b.minutes);
      }
      for (const x of data.appointments) {
        const a = zoned(x.startsAt, tz);
        const b = zoned(x.endsAt, tz);
        s = Math.min(s, a.minutes);
        e = Math.max(e, b.date > a.date ? 1440 : b.minutes);
      }
    }
    return [Math.floor(s / 60) * 60, Math.min(1440, Math.ceil(e / 60) * 60)];
  }, [data, tz]);

  const defaultChair = React.useCallback(
    (doctorId: string, d: string, minutes: number) => {
      const src = data?.working ?? [];
      const hit = src.find((w) => {
        if (w.doctorId !== doctorId) return false;
        const a = zoned(w.start, tz);
        const b = zoned(w.end, tz);
        return a.date === d && a.minutes <= minutes && (b.date > d || b.minutes > minutes);
      });
      return hit?.chairId ?? src.find((w) => w.doctorId === doctorId && zoned(w.start, tz).date === d)?.chairId ?? null;
    },
    [data, tz],
  );

  const move = useMutation({
    mutationFn: ({ a, col, minutes }: { a: Appointment; col: GridColumn; minutes: number }) => {
      const dur = Date.parse(a.endsAt) - Date.parse(a.startsAt);
      const startsAt = toIso(col.date, minutes, tz);
      return api<Appointment>(`/appointments/${a.id}/move`, {
        method: "POST",
        body: {
          startsAt,
          endsAt: new Date(Date.parse(startsAt) + dur).toISOString(),
          doctorId: col.doctorId,
          chairId: col.doctorId !== a.doctorId ? defaultChair(col.doctorId, col.date, minutes) : null,
          reasonId: null,
          force: null,
          version: a.version,
        },
      });
    },
    onSuccess: () => toast.success(t("schedule.moved")),
    onError: (e) => toast.error(e instanceof ApiError && e.code === "SLOT_CONFLICT" ? t("schedule.slotConflict") : e instanceof ApiError ? e.userMessage : t("errors.INTERNAL_ERROR")),
    onSettled: () => qc.invalidateQueries({ queryKey: ["calendar"] }),
  });

  const deleteBlock = useMutation({
    mutationFn: (b: TimeBlock) => api(`/time-blocks/${b.id}`, { method: "DELETE" }),
    onSuccess: () => {
      toast.success(t("schedule.blockDeleted"));
      qc.invalidateQueries({ queryKey: ["calendar"] });
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.userMessage : t("errors.INTERNAL_ERROR")),
  });

  const onCreated = async () => {
    if (bookingWaitlist) {
      const w = bookingWaitlist;
      setBookingWaitlist(null);
      try {
        await api(`/waitlist/${w.id}`, { method: "PATCH", body: toWaitlistRequest(w, { status: "Booked" }) });
        qc.invalidateQueries({ queryKey: ["waitlist"] });
      } catch {
        /* запись создана; статус листа ожидания можно поменять вручную */
      }
    }
  };

  const stats = React.useMemo(() => {
    const m = new Map<AppointmentStatus, number>();
    for (const a of data?.appointments ?? []) m.set(a.status, (m.get(a.status) ?? 0) + 1);
    return m;
  }, [data]);

  const branchSelector =
    !ctxBranch && (branches.data?.length ?? 0) > 1 ? (
      <NativeSelect className="w-52" value={branchId ?? ""} onChange={(e) => setPickedBranch(e.target.value)} aria-label={t("common.branch")}>
        {branches.data!.filter((b) => b.isActive).map((b) => (
          <option key={b.id} value={b.id}>
            {b.name}
          </option>
        ))}
      </NativeSelect>
    ) : null;

  if (!branchId) {
    return (
      <div>
        <PageHeader title={t("schedule.title")} />
        {branches.isLoading ? <Skeleton className="h-96 w-full" /> : <EmptyState title={t("schedule.selectBranch")} />}
      </div>
    );
  }

  const step = view === "day" ? 1 : 7;

  return (
    <div>
      <PageHeader
        title={t("schedule.title")}
        description={view === "day" ? <span className="capitalize">{formatDayTitle(date)}</span> : `${formatDayShort(from)} – ${formatDayShort(to)}`}
        actions={
          <>
            {branchSelector}
            {can(P.doctorSchedulesManage) ? (
              <Button variant="outline" asChild>
                <Link href={href("schedule/doctors")}>
                  <CalendarClock /> {t("nav.doctorSchedules")}
                </Link>
              </Button>
            ) : null}
            {canBlock ? (
              <Button variant="outline" onClick={() => setBlockInit({ date, doctorId: view === "week" ? effectiveWeekDoctor : null })}>
                <Ban /> {t("schedule.blocks")}
              </Button>
            ) : null}
            {manage ? (
              <Button
                onClick={() => {
                  setCreateInit({ date, doctorId: view === "week" ? effectiveWeekDoctor : null });
                  setCreateOpen(true);
                }}
                data-testid="new-appointment"
              >
                <Plus /> {t("schedule.newAppointment")}
              </Button>
            ) : null}
          </>
        }
      />

      <Tabs value={tab} onValueChange={setTab}>
        <TabsList>
          <TabsTrigger value="calendar">{t("schedule.calendarTab")}</TabsTrigger>
          {can(P.scheduleManage, P.scheduleViewAll) ? <TabsTrigger value="waitlist">{t("schedule.waitlist")}</TabsTrigger> : null}
        </TabsList>

        <TabsContent value="calendar">
          <div className="mb-3 flex flex-wrap items-center gap-2">
            <div className="flex items-center gap-1">
              <Button variant="outline" size="icon" onClick={() => setDate((d) => addDays(d, -step))} aria-label={t("common.prev")}>
                <ChevronLeft />
              </Button>
              <Button variant="outline" onClick={() => setDate(today)} disabled={view === "day" ? date === today : startOfWeek(today) === weekStart}>
                {t("schedule.today")}
              </Button>
              <Button variant="outline" size="icon" onClick={() => setDate((d) => addDays(d, step))} aria-label={t("common.next")}>
                <ChevronRight />
              </Button>
            </div>
            <Input type="date" className="w-40" value={date} onChange={(e) => e.target.value && setDate(e.target.value)} aria-label={t("common.date")} />
            <div className="inline-flex h-9 items-center gap-1 rounded-lg bg-muted p-1">
              {(["day", "week"] as const).map((v) => (
                <button
                  key={v}
                  type="button"
                  onClick={() => setView(v)}
                  className={`rounded-md px-3 py-1 text-sm font-medium ${view === v ? "bg-card text-foreground shadow-sm" : "text-muted-foreground"}`}
                >
                  {v === "day" ? t("schedule.viewDay") : t("schedule.viewWeek")}
                </button>
              ))}
            </div>
            {view === "week" ? (
              <NativeSelect className="w-60" value={effectiveWeekDoctor} onChange={(e) => setWeekDoctor(e.target.value)} aria-label={t("schedule.doctor")}>
                {resources.map((r) => (
                  <option key={r.id} value={r.id}>
                    {r.title}
                  </option>
                ))}
              </NativeSelect>
            ) : null}
            <label className="flex items-center gap-2 text-sm">
              <Checkbox checked={showCancelled} onCheckedChange={(v) => setShowCancelled(v === true)} />
              {t("schedule.showCancelled")}
            </label>
            <Button variant="ghost" size="icon" onClick={() => cal.refetch()} aria-label={t("schedule.refresh")} title={t("schedule.refresh")}>
              <RefreshCw className={cal.isFetching ? "animate-spin" : ""} />
            </Button>
            <div className="ml-auto hidden flex-wrap items-center gap-2 text-xs text-muted-foreground xl:flex">
              {(Object.keys(STATUS_COLORS) as AppointmentStatus[])
                .filter((s) => stats.get(s))
                .map((s) => (
                  <span key={s} className="inline-flex items-center gap-1">
                    <span className="h-2.5 w-2.5 rounded-sm border" style={{ background: STATUS_COLORS[s].bg, borderColor: STATUS_COLORS[s].border }} />
                    {t(`schedule.status.${s}`)}: {stats.get(s)}
                  </span>
                ))}
            </div>
          </div>

          {misplaced.length > 0 ? (
            <div className="mb-3 rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm" data-testid="needs-reschedule">
              <div className="mb-1 flex items-center gap-2 font-medium text-destructive">
                <AlertTriangle className="h-4 w-4" />
                {t("schedule.needsRescheduleTitle", { count: misplaced.length })}
              </div>
              <p className="mb-2 text-xs text-muted-foreground">{t("schedule.needsRescheduleHint")}</p>
              <div className="flex flex-wrap gap-1.5">
                {misplaced.map((a) => (
                  <button
                    key={a.id}
                    type="button"
                    className="rounded-md border bg-card px-2 py-1 text-xs hover:bg-accent"
                    onClick={() => setSelected(a)}
                  >
                    {formatDateTime(a.startsAt, tz)} · {a.patientName} · {a.doctorName}
                  </button>
                ))}
              </div>
            </div>
          ) : null}

          {cal.isLoading || (view === "week" && resourcesQuery.isLoading) ? (
            <Skeleton className="h-[60vh] w-full" />
          ) : cal.isError ? (
            <EmptyState
              title={cal.error instanceof ApiError ? cal.error.userMessage : t("errors.INTERNAL_ERROR")}
              action={
                <Button variant="outline" onClick={() => cal.refetch()}>
                  {t("common.retry")}
                </Button>
              }
            />
          ) : !data || columns.length === 0 ? (
            <EmptyState title={t("schedule.noDoctors")} icon={<CalendarClock className="h-8 w-8" />} />
          ) : (
            <CalendarGrid
              columns={columns}
              data={data}
              tz={tz}
              startMin={startMin}
              endMin={endMin}
              today={today}
              showCancelled={showCancelled}
              canCreate={manage}
              canMove={manage && !move.isPending}
              onSlotClick={(col, minutes) => {
                setCreateInit({ doctorId: col.doctorId, date: col.date, minutes, chairId: defaultChair(col.doctorId, col.date, minutes) });
                setCreateOpen(true);
              }}
              onAppointmentClick={setSelected}
              onBlockClick={
                canBlock
                  ? async (b) => {
                      const s = zoned(b.startsAt, tz);
                      const e = zoned(b.endsAt, tz);
                      if (
                        await confirm({
                          title: t("schedule.blockDeleteConfirm"),
                          description: `${hhmm(s.minutes)}–${hhmm(e.minutes)}${b.reason ? ` · ${b.reason}` : ""}`,
                          confirmText: t("common.delete"),
                          destructive: true,
                        })
                      )
                        deleteBlock.mutate(b);
                    }
                  : undefined
              }
              onDrop={async (a, col, minutes) => {
                const s = zoned(a.startsAt, tz);
                if (s.date === col.date && s.minutes === minutes && a.doctorId === col.doctorId) return;
                const ok = await confirm({
                  title: t("schedule.moveConfirm"),
                  description: `${a.patientName}: ${col.date === s.date ? "" : `${formatDayShort(col.date)} `}${hhmm(minutes)}${a.doctorId !== col.doctorId ? ` · ${col.title}` : ""}`,
                  confirmText: t("schedule.actions.move"),
                });
                if (ok) move.mutate({ a, col, minutes });
              }}
            />
          )}
          {manage ? <p className="mt-2 hidden text-xs text-muted-foreground md:block">{t("schedule.hotkeys")}</p> : null}
        </TabsContent>

        <TabsContent value="waitlist">
          <WaitlistPanel
            branchId={branchId}
            doctors={doctorsList}
            onBook={(w) => {
              const d = w.preferredFrom && w.preferredFrom > today ? w.preferredFrom : today;
              setBookingWaitlist(w);
              setCreateInit({
                date: d,
                doctorId: w.doctorId,
                patient: { id: w.patientId, fullName: w.patientName, phone: w.patientPhone },
                serviceIds: w.serviceId ? [w.serviceId] : [],
                comment: w.comment,
              });
              setCreateOpen(true);
            }}
          />
        </TabsContent>
      </Tabs>

      <AppointmentCreateDialog
        open={createOpen}
        onOpenChange={(o) => {
          setCreateOpen(o);
          if (!o) setTimeout(() => setBookingWaitlist(null), 0);
        }}
        branchId={branchId}
        tz={tz}
        doctors={doctorsList}
        initial={createInit}
        defaultChair={defaultChair}
        onCreated={(a) => {
          void onCreated();
          const d = zoned(a.startsAt, tz).date;
          if (tab === "waitlist") setTab("calendar");
          if (view === "day" ? d !== date : startOfWeek(d) !== weekStart) setDate(d);
        }}
      />
      <AppointmentPanel appointment={selected} onOpenChange={(o) => !o && setSelected(null)} tz={tz} doctors={doctorsList} />
      {blockInit ? (
        <TimeBlockDialog open={!!blockInit} onOpenChange={(o) => !o && setBlockInit(null)} branchId={branchId} tz={tz} doctors={doctorsList} initial={blockInit} />
      ) : null}
    </div>
  );
}

