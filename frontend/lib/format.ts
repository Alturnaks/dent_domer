// Форматирование: суммы «12 500 ₸», даты «28.09.2026». Деньги приходят в тиынах (целые).

const NBSP = " ";

export function formatMoney(minor: number | null | undefined, currency = "KZT"): string {
  if (minor === null || minor === undefined) return "—";
  const major = minor / 100;
  const hasFraction = Math.abs(minor) % 100 !== 0;
  const s = new Intl.NumberFormat("ru-RU", {
    minimumFractionDigits: hasFraction ? 2 : 0,
    maximumFractionDigits: 2,
  })
    .format(major)
    .replace(/\s/g, NBSP);
  const symbol = currency === "KZT" ? "₸" : currency;
  return `${s}${NBSP}${symbol}`;
}

/** Ввод в тенге → тиыны. */
export function toMinor(major: number | string): number {
  const n = typeof major === "string" ? Number(major.replace(/\s/g, "").replace(",", ".")) : major;
  return Math.round((Number.isFinite(n) ? n : 0) * 100);
}

export function toMajor(minor: number | null | undefined): number {
  return (minor ?? 0) / 100;
}

export function formatNumber(n: number | null | undefined, digits = 3): string {
  if (n === null || n === undefined) return "—";
  return new Intl.NumberFormat("ru-RU", { maximumFractionDigits: digits }).format(n).replace(/\s/g, NBSP);
}

export function formatPercent(n: number | null | undefined, digits = 1): string {
  if (n === null || n === undefined) return "—";
  return `${new Intl.NumberFormat("ru-RU", { maximumFractionDigits: digits }).format(n)}%`;
}

function toDate(value: string | Date): Date {
  return typeof value === "string" ? new Date(value.length === 10 ? `${value}T00:00:00` : value) : value;
}

export function formatDate(value: string | Date | null | undefined, timeZone?: string): string {
  if (!value) return "—";
  return new Intl.DateTimeFormat("ru-RU", { day: "2-digit", month: "2-digit", year: "numeric", timeZone }).format(toDate(value));
}

export function formatTime(value: string | Date | null | undefined, timeZone?: string): string {
  if (!value) return "—";
  return new Intl.DateTimeFormat("ru-RU", { hour: "2-digit", minute: "2-digit", timeZone }).format(toDate(value));
}

export function formatDateTime(value: string | Date | null | undefined, timeZone?: string): string {
  if (!value) return "—";
  return `${formatDate(value, timeZone)} ${formatTime(value, timeZone)}`;
}

/** YYYY-MM-DD в локальном времени браузера. */
export function isoDate(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${y}-${m}-${day}`;
}

export function formatPhone(phone: string | null | undefined): string {
  if (!phone) return "—";
  const d = phone.replace(/\D/g, "");
  if (d.length === 11) return `+${d[0]} ${d.slice(1, 4)} ${d.slice(4, 7)}-${d.slice(7, 9)}-${d.slice(9)}`;
  return phone;
}

export function whatsappLink(phone: string | null | undefined, text?: string): string | null {
  if (!phone) return null;
  const d = phone.replace(/\D/g, "");
  return `https://wa.me/${d}${text ? `?text=${encodeURIComponent(text)}` : ""}`;
}
