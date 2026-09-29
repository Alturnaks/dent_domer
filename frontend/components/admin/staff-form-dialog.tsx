"use client";

import * as React from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Wand2 } from "lucide-react";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { toastError } from "@/components/admin/common";
import { api } from "@/lib/api-client";
import { useBranches } from "@/lib/queries";
import { t } from "@/lib/i18n";
import type { NamedRef, Schemas, Staff } from "@/lib/types";
import { useScheduleConflict } from "@/components/schedule/schedule-conflict";

type Position = Schemas["StaffPosition"];
export const POSITIONS: Position[] = ["Owner", "SeniorAdmin", "Admin", "Doctor", "Assistant", "Cashier", "Storekeeper", "Other"];
const COLORS = ["#2563eb", "#dc2626", "#16a34a", "#9333ea", "#ea580c", "#0891b2", "#db2777", "#65a30d", "#ca8a04", "#4f46e5"];

// Роль по умолчанию для должности (по коду предустановленной роли).
const DEFAULT_ROLE: Partial<Record<Position, string>> = { Owner: "owner", SeniorAdmin: "senior_admin", Admin: "admin", Doctor: "doctor" };

type FormState = {
  fullName: string;
  email: string;
  phone: string;
  position: Position;
  roleId: string;
  password: string;
  allBranches: boolean;
  branchIds: string[];
  specialty: string;
  color: string;
};

function initial(s: Staff | null): FormState {
  return {
    fullName: s?.fullName ?? "",
    email: s?.email ?? "",
    phone: s?.phone ?? "",
    position: s?.position ?? "Admin",
    roleId: s?.roleId ?? "",
    password: "",
    allBranches: s?.allBranches ?? false,
    branchIds: s?.branchIds ?? [],
    specialty: s?.specialty ?? "",
    color: s?.color ?? COLORS[0],
  };
}

function generatePassword() {
  const chars = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  const arr = new Uint32Array(10);
  crypto.getRandomValues(arr);
  return Array.from(arr, (n) => chars[n % chars.length]).join("");
}

export function useRoleOptions(enabled = true) {
  return useQuery({ queryKey: ["role-options"], queryFn: () => api<NamedRef[]>("/staff/role-options"), enabled, staleTime: 60_000 });
}

export function StaffFormDialog({ open, onOpenChange, staff }: { open: boolean; onOpenChange: (o: boolean) => void; staff: Staff | null }) {
  const qc = useQueryClient();
  const branches = useBranches();
  const roles = useRoleOptions(open);
  const [f, setF] = React.useState<FormState>(() => initial(staff));
  const [errors, setErrors] = React.useState<Partial<Record<keyof FormState, string>>>({});

  React.useEffect(() => {
    if (open) {
      setF(initial(staff));
      setErrors({});
    }
  }, [open, staff]);

  // Для нового сотрудника подставляем роль по должности.
  React.useEffect(() => {
    if (staff || !open || f.roleId || !roles.data) return;
    const code = DEFAULT_ROLE[f.position];
    const r = roles.data.find((x) => x.type === code);
    if (r) setF((p) => ({ ...p, roleId: r.id }));
  }, [roles.data, staff, open, f.position, f.roleId]);

  const set = <K extends keyof FormState>(k: K, v: FormState[K]) => setF((p) => ({ ...p, [k]: v }));
  const isDoctor = f.position === "Doctor";

  const run = useScheduleConflict();
  const save = useMutation({
    mutationFn: async () => {
      if (staff) {
        const body: Schemas["UpdateStaffRequest"] = {
          fullName: f.fullName.trim(),
          email: f.email.trim(),
          phone: f.phone.trim(),
          password: f.password || null,
          roleId: f.roleId,
          position: f.position,
          specialty: isDoctor ? f.specialty.trim() : "",
          color: isDoctor ? f.color : "",
          allBranches: f.allBranches,
          branchIds: f.allBranches ? [] : f.branchIds,
        };
        return run((onConflict) => api<Staff>(`/staff/${staff.membershipId}`, { method: "PATCH", body: { ...body, onConflict } }));
      }
      const body: Schemas["CreateStaffRequest"] = {
        fullName: f.fullName.trim(),
        email: f.email.trim() || null,
        phone: f.phone.trim() || null,
        password: f.password,
        roleId: f.roleId,
        position: f.position,
        specialty: isDoctor ? f.specialty.trim() || null : null,
        color: isDoctor ? f.color : null,
        allBranches: f.allBranches,
        branchIds: f.allBranches ? [] : f.branchIds,
      };
      return api<Staff>("/staff", { method: "POST", body });
    },
    onSuccess: () => {
      toast.success(staff ? t("staff.savedToast") : t("staff.createdToast"));
      void qc.invalidateQueries({ queryKey: ["staff"] });
      void qc.invalidateQueries({ queryKey: ["doctors"] });
      void qc.invalidateQueries({ queryKey: ["roles"] });
      onOpenChange(false);
    },
    onError: toastError,
  });

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    const errs: typeof errors = {};
    if (!f.fullName.trim()) errs.fullName = t("common.required");
    if (!f.email.trim() && !f.phone.trim()) errs.email = t("staff.errors.loginRequired");
    if (!staff && f.password.length < 8) errs.password = t("staff.errors.passwordShort");
    if (staff && f.password && f.password.length < 8) errs.password = t("staff.errors.passwordShort");
    if (!f.roleId) errs.roleId = t("staff.errors.roleRequired");
    if (!f.allBranches && f.branchIds.length === 0) errs.branchIds = t("staff.errors.branchesRequired");
    setErrors(errs);
    if (Object.keys(errs).length === 0) save.mutate();
  };

  const toggleBranch = (id: string, on: boolean) =>
    set("branchIds", on ? Array.from(new Set([...f.branchIds, id])) : f.branchIds.filter((x) => x !== id));

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent wide>
        <DialogHeader>
          <DialogTitle>{staff ? `${t("staff.editTitle")}: ${staff.fullName}` : t("staff.newTitle")}</DialogTitle>
        </DialogHeader>
        <form className="grid gap-3 sm:grid-cols-2" onSubmit={submit}>
          <Field label={t("staff.fullName")} error={errors.fullName} className="sm:col-span-2">
            <Input autoFocus value={f.fullName} onChange={(e) => set("fullName", e.target.value)} />
          </Field>
          <Field label={t("staff.email")} error={errors.email} hint={t("staff.loginHint")}>
            <Input type="email" autoComplete="off" value={f.email} onChange={(e) => set("email", e.target.value)} />
          </Field>
          <Field label={t("staff.phone")}>
            <Input type="tel" placeholder="+7 7__ ___-__-__" value={f.phone} onChange={(e) => set("phone", e.target.value)} />
          </Field>
          <Field label={t("staff.position")}>
            <NativeSelect
              value={f.position}
              onChange={(e) => {
                const pos = e.target.value as Position;
                const code = DEFAULT_ROLE[pos];
                const r = !staff && code ? roles.data?.find((x) => x.type === code) : undefined;
                setF((p) => ({ ...p, position: pos, roleId: r ? r.id : p.roleId }));
              }}
            >
              {POSITIONS.map((p) => (
                <option key={p} value={p}>
                  {t(`staff.positions.${p}`)}
                </option>
              ))}
            </NativeSelect>
          </Field>
          <Field label={t("staff.role")} error={errors.roleId}>
            <NativeSelect value={f.roleId} onChange={(e) => set("roleId", e.target.value)}>
              <option value="">—</option>
              {roles.data?.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.name}
                </option>
              ))}
            </NativeSelect>
          </Field>
          {isDoctor ? (
            <>
              <Field label={t("staff.specialty")}>
                <Input value={f.specialty} onChange={(e) => set("specialty", e.target.value)} />
              </Field>
              <Field label={t("staff.color")}>
                <div className="flex flex-wrap items-center gap-1.5">
                  {COLORS.map((c) => (
                    <button
                      type="button"
                      key={c}
                      aria-label={c}
                      onClick={() => set("color", c)}
                      className={`h-6 w-6 rounded-full border-2 ${f.color === c ? "border-foreground" : "border-transparent"}`}
                      style={{ backgroundColor: c }}
                    />
                  ))}
                  <input type="color" className="h-6 w-8 cursor-pointer rounded border" value={f.color || "#2563eb"} onChange={(e) => set("color", e.target.value)} />
                </div>
              </Field>
            </>
          ) : null}
          <Field
            label={staff ? t("staff.newPassword") : t("staff.password")}
            error={errors.password}
            hint={staff ? t("staff.newPasswordHint") : t("staff.passwordHint")}
            className="sm:col-span-2"
          >
            <div className="flex gap-2">
              <Input autoComplete="new-password" value={f.password} onChange={(e) => set("password", e.target.value)} className="font-mono" />
              <Button type="button" variant="outline" onClick={() => set("password", generatePassword())}>
                <Wand2 /> {t("staff.generate")}
              </Button>
            </div>
          </Field>
          <Field label={t("staff.branches")} error={errors.branchIds} className="sm:col-span-2">
            <div className="space-y-2 rounded-md border p-3">
              <label className="flex items-center gap-2 text-sm font-medium">
                <Checkbox checked={f.allBranches} onCheckedChange={(v) => set("allBranches", v === true)} />
                {t("staff.allBranches")}
              </label>
              {!f.allBranches ? (
                <div className="grid gap-1.5 sm:grid-cols-2">
                  {branches.data?.map((b) => (
                    <label key={b.id} className="flex items-center gap-2 text-sm">
                      <Checkbox checked={f.branchIds.includes(b.id)} onCheckedChange={(v) => toggleBranch(b.id, v === true)} />
                      {b.name}
                    </label>
                  ))}
                </div>
              ) : null}
            </div>
          </Field>
          <DialogFooter className="sm:col-span-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" loading={save.isPending}>
              {staff ? t("common.save") : t("common.create")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
