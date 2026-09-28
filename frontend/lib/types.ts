// Типы API берутся ТОЛЬКО из сгенерированного lib/api-types.ts (npm run gen-types).
import type { components } from "@/lib/api-types";

export type Schemas = components["schemas"];
export type Me = Schemas["MeResponse"];
export type PatientListItem = Schemas["PatientListItem"];
export type Patient = Schemas["PatientDto"];
export type PatientRequest = Schemas["PatientRequest"];
export type DuplicateCandidate = Schemas["DuplicateCandidate"];
export type DuplicatePair = Schemas["DuplicatePair"];
export type Appointment = Schemas["AppointmentDto"];
export type AppointmentStatus = Schemas["AppointmentStatus"];
export type CalendarResponse = Schemas["CalendarResponse"];
export type Staff = Schemas["StaffDto"];
export type ServiceItem = Schemas["ServiceDto"];
export type NamedRef = Schemas["NamedRefDto"];
export type Branch = Schemas["BranchDto"];
export type Chair = Schemas["ChairDto"];
export type DoctorSchedule = Schemas["DoctorScheduleDto"];
export type ScheduleException = Schemas["ScheduleExceptionDto"];
export type TimeBlock = Schemas["TimeBlockDto"];
export type Waitlist = Schemas["WaitlistDto"];
export type AuditEntry = Schemas["AuditEntryDto"];
export type CursorPage<T> = { items: T[]; nextCursor?: string | null };
