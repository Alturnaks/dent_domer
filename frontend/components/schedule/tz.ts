// Работа со временем в часовом поясе организации (а не браузера).
// Даты — строки YYYY-MM-DD, время — минуты от начала суток.

const cache = new Map<string, Intl.DateTimeFormat>();

function fmt(tz: string) {
  let f = cache.get(tz);
  if (!f) {
    f = new Intl.DateTimeFormat("en-CA", {
      timeZone: tz,
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
      hourCycle: "h23",
    });
    cache.set(tz, f);
  }
  return f;
}

function parts(ms: number, tz: string) {
  const p: Record<string, number> = {};
  for (const x of fmt(tz).formatToParts(new Date(ms))) if (x.type !== "literal") p[x.type] = Number(x.value);
  return p as { year: number; month: number; day: number; hour: number; minute: number; second: number };
}

const pad = (n: number) => String(n).padStart(2, "0");

/** Дата и минуты суток момента времени в поясе tz. */
export function zoned(value: string | Date, tz: string): { date: string; minutes: number } {
  const ms = typeof value === "string" ? Date.parse(value) : value.getTime();
  const p = parts(ms, tz);
  return { date: `${p.year}-${pad(p.month)}-${pad(p.day)}`, minutes: p.hour * 60 + p.minute };
}

/** Смещение пояса в минутах для момента времени. */
export function tzOffset(tz: string, ms: number): number {
  const p = parts(ms, tz);
  const asUtc = Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute, p.second);
  return Math.round((asUtc - Math.floor(ms / 1000) * 1000) / 60000);
}

/** Локальные дата+минуты в поясе tz → ISO-строка со смещением (например 2026-09-28T09:00:00+05:00). */
export function toIso(date: string, minutes: number, tz: string): string {
  const [y, m, d] = date.split("-").map(Number);
  const guess = Date.UTC(y, m - 1, d, 0, minutes);
  let off = tzOffset(tz, guess - tzOffset(tz, guess) * 60000);
  if (!Number.isFinite(off)) off = 0;
  const sign = off >= 0 ? "+" : "-";
  const abs = Math.abs(off);
  const localDay = new Date(guess);
  const ds = `${localDay.getUTCFullYear()}-${pad(localDay.getUTCMonth() + 1)}-${pad(localDay.getUTCDate())}`;
  return `${ds}T${pad(localDay.getUTCHours())}:${pad(localDay.getUTCMinutes())}:00${sign}${pad(Math.floor(abs / 60))}:${pad(abs % 60)}`;
}

export function todayIn(tz: string): string {
  return zoned(new Date(), tz).date;
}

export function addDays(date: string, n: number): string {
  const [y, m, d] = date.split("-").map(Number);
  const x = new Date(Date.UTC(y, m - 1, d + n));
  return `${x.getUTCFullYear()}-${pad(x.getUTCMonth() + 1)}-${pad(x.getUTCDate())}`;
}

/** 0 = воскресенье … 6 = суббота. */
export function weekdayOf(date: string): number {
  const [y, m, d] = date.split("-").map(Number);
  return new Date(Date.UTC(y, m - 1, d)).getUTCDay();
}

/** Понедельник недели, в которую входит дата. */
export function startOfWeek(date: string): string {
  const wd = weekdayOf(date);
  return addDays(date, wd === 0 ? -6 : 1 - wd);
}

export function hhmm(minutes: number): string {
  const m = ((minutes % 1440) + 1440) % 1440;
  return `${pad(Math.floor(m / 60))}:${pad(m % 60)}`;
}

/** "09:30" или "09:30:00" → минуты. */
export function parseHhmm(s: string | null | undefined): number | null {
  if (!s) return null;
  const m = /^(\d{1,2}):(\d{2})/.exec(s);
  return m ? Number(m[1]) * 60 + Number(m[2]) : null;
}

export function formatDayTitle(date: string): string {
  const [y, m, d] = date.split("-").map(Number);
  return new Intl.DateTimeFormat("ru-RU", { weekday: "long", day: "numeric", month: "long", year: "numeric", timeZone: "UTC" }).format(
    new Date(Date.UTC(y, m - 1, d)),
  );
}

export function formatDayShort(date: string): string {
  const [y, m, d] = date.split("-").map(Number);
  return new Intl.DateTimeFormat("ru-RU", { weekday: "short", day: "2-digit", month: "2-digit", timeZone: "UTC" }).format(new Date(Date.UTC(y, m - 1, d)));
}
