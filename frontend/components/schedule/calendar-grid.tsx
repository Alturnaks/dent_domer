"use client";

import * as React from "react";
import { AlertTriangle, Ban, MessageSquare, Star } from "lucide-react";
import { cn } from "@/lib/utils";
import { t } from "@/lib/i18n";
import { STATUS_COLORS } from "@/lib/status";
import type { Appointment, CalendarResponse, TimeBlock } from "@/lib/types";
import { hhmm, zoned } from "./tz";

/** Колонка сетки: врач на конкретную дату (день — колонки врачей, неделя — колонки дней). */
export type GridColumn = {
  key: string;
  doctorId: string;
  date: string;
  title: string;
  subtitle?: string | null;
  color?: string | null;
  note?: string | null;
  highlight?: boolean;
};

export const PX_PER_MIN = 1.6;

/**
 * Запись больше не помещается в график врача (выходной, отпуск, изменённый график, блокировка),
 * но осталась активной — её нужно перенести. Проверяются только будущие запланированные/подтверждённые записи.
 */
export function needsReschedule(a: Appointment, data: CalendarResponse, nowMs = Date.now()): boolean {
  if (a.status !== "Scheduled" && a.status !== "Confirmed") return false;
  const s = new Date(a.startsAt).getTime();
  const e = new Date(a.endsAt).getTime();
  if (e <= nowMs) return false;
  const inWorking = data.working.some((w) => w.doctorId === a.doctorId && new Date(w.start).getTime() <= s && new Date(w.end).getTime() >= e);
  if (!inWorking) return true;
  return data.blocks.some(
    (b) =>
      (b.doctorId === a.doctorId || (b.doctorId === null && (b.chairId === null || b.chairId === a.chairId))) &&
      new Date(b.startsAt).getTime() < e &&
      new Date(b.endsAt).getTime() > s,
  );
}
const SNAP = 15;

type Positioned<T> = { item: T; top: number; height: number; lane: number; lanes: number };

function layout(items: Appointment[], tz: string, startMin: number): Positioned<Appointment>[] {
  const rows = items
    .map((a) => {
      const s = zoned(a.startsAt, tz);
      const e = zoned(a.endsAt, tz);
      const endMin = e.date > s.date ? 1440 : e.minutes;
      return { a, s: s.minutes, e: Math.max(endMin, s.minutes + 5) };
    })
    .sort((x, y) => x.s - y.s || y.e - x.e);
  const out: Positioned<Appointment>[] = [];
  let cluster: { r: (typeof rows)[number]; lane: number }[] = [];
  let clusterEnd = -1;
  const flush = () => {
    const lanes = Math.max(1, ...cluster.map((c) => c.lane + 1));
    for (const c of cluster)
      out.push({ item: c.r.a, top: (c.r.s - startMin) * PX_PER_MIN, height: (c.r.e - c.r.s) * PX_PER_MIN, lane: c.lane, lanes });
    cluster = [];
  };
  for (const r of rows) {
    if (cluster.length && r.s >= clusterEnd) flush();
    const laneEnds: number[] = [];
    for (const c of cluster) laneEnds[c.lane] = Math.max(laneEnds[c.lane] ?? 0, c.r.e);
    let lane = 0;
    while (laneEnds[lane] !== undefined && laneEnds[lane] > r.s) lane++;
    cluster.push({ r, lane });
    clusterEnd = Math.max(clusterEnd, r.e);
  }
  if (cluster.length) flush();
  return out;
}

function range(value: { startsAt?: string; endsAt?: string; start?: string; end?: string }, tz: string, date: string, startMin: number, endMin: number) {
  const s = zoned((value.startsAt ?? value.start)!, tz);
  const e = zoned((value.endsAt ?? value.end)!, tz);
  const from = s.date < date ? startMin : s.date > date ? endMin : s.minutes;
  const to = e.date > date ? endMin : e.date < date ? startMin : e.minutes;
  const a = Math.max(from, startMin);
  const b = Math.min(to, endMin);
  return b > a ? { top: (a - startMin) * PX_PER_MIN, height: (b - a) * PX_PER_MIN } : null;
}

export function CalendarGrid({
  columns,
  data,
  tz,
  startMin,
  endMin,
  today,
  showCancelled,
  canCreate,
  canMove,
  onSlotClick,
  onAppointmentClick,
  onBlockClick,
  onDrop,
}: {
  columns: GridColumn[];
  data: CalendarResponse;
  tz: string;
  startMin: number;
  endMin: number;
  today: string;
  showCancelled: boolean;
  canCreate: boolean;
  canMove: boolean;
  onSlotClick: (col: GridColumn, minutes: number) => void;
  onAppointmentClick: (a: Appointment) => void;
  onBlockClick?: (b: TimeBlock) => void;
  onDrop?: (a: Appointment, col: GridColumn, minutes: number) => void;
}) {
  const scrollRef = React.useRef<HTMLDivElement>(null);
  const dragRef = React.useRef<{ a: Appointment; grabY: number } | null>(null);
  const [hover, setHover] = React.useState<{ key: string; minutes: number } | null>(null);
  const [now, setNow] = React.useState(() => zoned(new Date(), tz));

  React.useEffect(() => {
    const id = setInterval(() => setNow(zoned(new Date(), tz)), 60_000);
    return () => clearInterval(id);
  }, [tz]);

  const height = (endMin - startMin) * PX_PER_MIN;
  const hours: number[] = [];
  for (let m = Math.ceil(startMin / 60) * 60; m < endMin; m += 60) hours.push(m);

  // Автопрокрутка к текущему времени / началу рабочего дня.
  const firstWork = React.useMemo(() => {
    let min = Infinity;
    for (const w of data.working) {
      const z = zoned(w.start, tz);
      if (columns.some((c) => c.doctorId === w.doctorId && c.date === z.date)) min = Math.min(min, z.minutes);
    }
    return Number.isFinite(min) ? min : startMin;
  }, [data.working, columns, tz, startMin]);
  const hasToday = columns.some((c) => c.date === today);
  const scrollKey = columns.map((c) => c.key).join(",");
  React.useEffect(() => {
    const el = scrollRef.current;
    if (!el) return;
    const target = hasToday ? Math.max(startMin, now.minutes - 60) : firstWork;
    el.scrollTop = Math.max(0, (target - startMin) * PX_PER_MIN - 8);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scrollKey, startMin]);

  const minutesAt = (e: React.MouseEvent<HTMLDivElement> | React.DragEvent<HTMLDivElement>, grabY = 0) => {
    const rect = e.currentTarget.getBoundingClientRect();
    const y = e.clientY - rect.top - grabY;
    const m = startMin + Math.floor(y / PX_PER_MIN / SNAP) * SNAP;
    return Math.min(Math.max(m, startMin), endMin - SNAP);
  };

  const visible = (a: Appointment) => showCancelled || (a.status !== "Cancelled" && a.status !== "NoShow");

  return (
    <div ref={scrollRef} className="relative max-h-[calc(100vh-12.5rem)] min-h-96 overflow-auto rounded-xl border bg-card" data-testid="calendar-grid">
      <div className="grid" style={{ gridTemplateColumns: `3.5rem repeat(${columns.length}, minmax(11rem, 1fr))` }}>
        {/* Шапка */}
        <div className="sticky left-0 top-0 z-30 border-b border-r bg-card" />
        {columns.map((c) => (
          <div
            key={c.key}
            className={cn("sticky top-0 z-20 border-b border-r bg-card px-2 py-1.5 last:border-r-0", c.highlight && "bg-primary/5")}
          >
            <div className="flex items-center gap-1.5">
              {c.color ? <span className="h-2.5 w-2.5 shrink-0 rounded-full" style={{ background: c.color }} /> : null}
              <span className="truncate text-sm font-medium" title={c.title}>
                {c.title}
              </span>
            </div>
            <div className="flex items-center gap-1 truncate text-xs text-muted-foreground">
              {c.subtitle ?? " "}
              {c.note ? <span className="rounded bg-warning/20 px-1 text-[10px] font-medium text-[oklch(0.5_0.12_70)]">{c.note}</span> : null}
            </div>
          </div>
        ))}

        {/* Шкала времени */}
        <div className="sticky left-0 z-10 border-r bg-card" style={{ height }}>
          {hours.map((m) => (
            <div key={m} className="absolute right-1.5 -translate-y-1/2 text-[11px] tabular-nums text-muted-foreground" style={{ top: (m - startMin) * PX_PER_MIN }}>
              {m === startMin ? "" : hhmm(m)}
            </div>
          ))}
        </div>

        {columns.map((col) => {
          const working = data.working.filter((w) => w.doctorId === col.doctorId).map((w) => range(w, tz, col.date, startMin, endMin)).filter(Boolean);
          const blocks = data.blocks
            .filter((b) => b.doctorId === col.doctorId || b.doctorId === null)
            .map((b) => ({ b, r: range(b, tz, col.date, startMin, endMin) }))
            .filter((x) => x.r);
          const appts = layout(
            data.appointments.filter((a) => a.doctorId === col.doctorId && zoned(a.startsAt, tz).date === col.date && visible(a)),
            tz,
            startMin,
          );
          const isToday = col.date === today;
          return (
            <div
              key={col.key}
              className={cn("relative border-r bg-muted/60 last:border-r-0", canCreate && "cursor-pointer")}
              style={{ height }}
              onMouseMove={(e) => {
                if (!canCreate) return;
                const m = minutesAt(e);
                if (hover?.key !== col.key || hover.minutes !== m) setHover({ key: col.key, minutes: m });
              }}
              onMouseLeave={() => setHover(null)}
              onClick={(e) => {
                if (!canCreate) return;
                onSlotClick(col, minutesAt(e));
              }}
              onDragOver={(e) => {
                if (dragRef.current) {
                  e.preventDefault();
                  e.dataTransfer.dropEffect = "move";
                  const m = minutesAt(e, dragRef.current.grabY);
                  if (hover?.key !== col.key || hover.minutes !== m) setHover({ key: col.key, minutes: m });
                }
              }}
              onDrop={(e) => {
                const d = dragRef.current;
                dragRef.current = null;
                setHover(null);
                if (!d) return;
                e.preventDefault();
                onDrop?.(d.a, col, minutesAt(e, d.grabY));
              }}
            >
              {working.map((r, i) => (
                <div key={i} className="absolute inset-x-0 bg-card" style={{ top: r!.top, height: r!.height }} />
              ))}
              {/* линии сетки */}
              <div
                className="pointer-events-none absolute inset-0"
                style={{
                  backgroundImage: `repeating-linear-gradient(to bottom, var(--border) 0 1px, transparent 1px ${60 * PX_PER_MIN}px), repeating-linear-gradient(to bottom, color-mix(in oklab, var(--border) 45%, transparent) 0 1px, transparent 1px ${15 * PX_PER_MIN}px)`,
                  backgroundPositionY: `${((60 - (startMin % 60)) % 60) * PX_PER_MIN}px, 0`,
                }}
              />
              {blocks.map(({ b, r }) => (
                <button
                  key={b.id}
                  type="button"
                  className="absolute inset-x-0 z-[1] flex items-start gap-1 overflow-hidden px-1.5 py-0.5 text-left text-[11px] text-muted-foreground"
                  style={{
                    top: r!.top,
                    height: r!.height,
                    backgroundImage: "repeating-linear-gradient(135deg, color-mix(in oklab, var(--muted-foreground) 14%, transparent) 0 6px, transparent 6px 12px)",
                  }}
                  title={b.reason ?? t("schedule.blocks")}
                  onClick={(e) => {
                    e.stopPropagation();
                    onBlockClick?.(b);
                  }}
                >
                  <Ban className="mt-0.5 h-3 w-3 shrink-0" />
                  <span className="truncate">{b.reason || t("schedule.blocks")}</span>
                </button>
              ))}
              {hover?.key === col.key ? (
                <div
                  className="pointer-events-none absolute inset-x-0 z-[2] rounded-sm border border-dashed border-primary/60 bg-primary/10 px-1.5 text-[11px] font-medium text-primary"
                  style={{ top: (hover.minutes - startMin) * PX_PER_MIN, height: SNAP * PX_PER_MIN }}
                >
                  {hhmm(hover.minutes)}
                </div>
              ) : null}
              {appts.map(({ item: a, top, height: h, lane, lanes }) => {
                const c = STATUS_COLORS[a.status];
                const dim = a.status === "Cancelled" || a.status === "NoShow";
                const draggable = canMove && !!onDrop && (a.status === "Scheduled" || a.status === "Confirmed");
                const compact = h < 34;
                const misplaced = needsReschedule(a, data);
                return (
                  <button
                    key={a.id}
                    type="button"
                    data-testid="appointment-block"
                    draggable={draggable}
                    onDragStart={(e) => {
                      dragRef.current = { a, grabY: e.clientY - e.currentTarget.getBoundingClientRect().top };
                      e.dataTransfer.effectAllowed = "move";
                      e.dataTransfer.setData("text/plain", a.id);
                    }}
                    onDragEnd={() => {
                      dragRef.current = null;
                      setHover(null);
                    }}
                    onMouseMove={(e) => e.stopPropagation()}
                    onMouseEnter={() => setHover(null)}
                    onClick={(e) => {
                      e.stopPropagation();
                      onAppointmentClick(a);
                    }}
                    className={cn(
                      "absolute z-[3] overflow-hidden rounded-md border-l-[3px] px-1.5 text-left shadow-sm transition-shadow hover:z-[4] hover:shadow-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
                      compact ? "py-0" : "py-0.5",
                      dim && "opacity-60",
                      draggable && "cursor-grab active:cursor-grabbing",
                      misplaced && "outline-2 outline-dashed outline-destructive",
                    )}
                    style={{
                      top: top + 1,
                      height: Math.max(h - 2, 14),
                      left: `calc(${(lane / lanes) * 100}% + 2px)`,
                      width: `calc(${100 / lanes}% - 4px)`,
                      background: c.bg,
                      borderColor: c.border,
                      color: c.text,
                    }}
                    title={`${misplaced ? `${t("schedule.needsReschedule")}\n` : ""}${hhmm(zoned(a.startsAt, tz).minutes)}–${hhmm(zoned(a.endsAt, tz).minutes)} ${a.patientName}\n${a.services.map((s) => s.name).join(", ")}${a.comment ? `\n${a.comment}` : ""}`}
                  >
                    <div className={cn("flex items-center gap-1 text-[11px] leading-tight", compact && "leading-none")}>
                      {misplaced ? <AlertTriangle className="h-3 w-3 shrink-0 text-destructive" /> : null}
                      <span className="shrink-0 tabular-nums opacity-80">{hhmm(zoned(a.startsAt, tz).minutes)}</span>
                      {a.patientIsVip ? <Star className="h-3 w-3 shrink-0 fill-current" /> : null}
                      <span className={cn("truncate font-semibold", dim && "line-through")}>{a.patientName}</span>
                      {a.comment ? <MessageSquare className="h-3 w-3 shrink-0 opacity-70" /> : null}
                      {a.patientBalance < 0 ? <span className="ml-auto h-1.5 w-1.5 shrink-0 rounded-full bg-destructive" /> : null}
                    </div>
                    {!compact ? (
                      <div className="truncate text-[11px] leading-tight opacity-80">
                        {a.services.length ? a.services.map((s) => s.name).join(", ") : t(`schedule.status.${a.status}`)}
                      </div>
                    ) : null}
                    {h >= 60 ? <div className="truncate text-[10px] leading-tight opacity-70">{t(`schedule.status.${a.status}`)}</div> : null}
                  </button>
                );
              })}
              {isToday && now.minutes >= startMin && now.minutes <= endMin ? (
                <div className="pointer-events-none absolute inset-x-0 z-[5] h-0.5 bg-destructive" style={{ top: (now.minutes - startMin) * PX_PER_MIN }}>
                  <span className="absolute -left-1 -top-1 h-2.5 w-2.5 rounded-full bg-destructive" />
                </div>
              ) : null}
            </div>
          );
        })}
      </div>
    </div>
  );
}
