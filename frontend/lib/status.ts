import type { AppointmentStatus } from "@/lib/types";

/** Цвета статусов записи (фон события в календаре и бейджи). */
export const STATUS_COLORS: Record<AppointmentStatus, { bg: string; border: string; text: string }> = {
  Scheduled: { bg: "#e0f2fe", border: "#38bdf8", text: "#075985" },
  Confirmed: { bg: "#dcfce7", border: "#22c55e", text: "#166534" },
  Arrived: { bg: "#fef9c3", border: "#eab308", text: "#854d0e" },
  InChair: { bg: "#ffedd5", border: "#f97316", text: "#9a3412" },
  Completed: { bg: "#e5e7eb", border: "#6b7280", text: "#1f2937" },
  Cancelled: { bg: "#fee2e2", border: "#ef4444", text: "#991b1b" },
  NoShow: { bg: "#f3e8ff", border: "#a855f7", text: "#6b21a8" },
};

export const ACTIVE_STATUSES: AppointmentStatus[] = ["Scheduled", "Confirmed", "Arrived", "InChair", "Completed"];
