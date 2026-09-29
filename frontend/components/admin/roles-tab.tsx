"use client";

import * as React from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Lock, Plus, Trash2 } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Checkbox, Switch } from "@/components/ui/checkbox";
import { TableSkeleton } from "@/components/ui/skeleton";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { useConfirm } from "@/components/ui/confirm";
import { ErrorBlock, MoneyInput, minorToInput, tengeToMinorOrNull, toastError } from "@/components/admin/common";
import { api } from "@/lib/api-client";
import { useAuth } from "@/lib/auth";
import { t } from "@/lib/i18n";
import { cn } from "@/lib/utils";
import type { Schemas } from "@/lib/types";

type Role = Schemas["RoleDto"];
type PermGroup = Schemas["PermissionGroupDto"];

type Draft = { name: string; permissions: string[]; maxDiscountPct: string; maxWriteoff: string; maxRefund: string; canEditClosed: boolean };

function toDraft(r: Role): Draft {
  return {
    name: r.name,
    permissions: [...r.permissions],
    maxDiscountPct: r.limits.maxDiscountPct === null || r.limits.maxDiscountPct === undefined ? "" : String(r.limits.maxDiscountPct),
    maxWriteoff: minorToInput(r.limits.maxWriteoffAmount),
    maxRefund: minorToInput(r.limits.maxRefundAmount),
    canEditClosed: r.limits.canEditClosedShiftVisits ?? false,
  };
}

function toRequest(d: Draft): Schemas["RoleRequest"] {
  const pct = d.maxDiscountPct.trim() === "" ? null : Number(d.maxDiscountPct.replace(",", "."));
  return {
    name: d.name.trim(),
    permissions: d.permissions,
    limits: {
      maxDiscountPct: pct === null || !Number.isFinite(pct) ? null : pct,
      maxWriteoffAmount: tengeToMinorOrNull(d.maxWriteoff),
      maxRefundAmount: tengeToMinorOrNull(d.maxRefund),
      canEditClosedShiftVisits: d.canEditClosed,
    },
  };
}

export function RolesTab() {
  const qc = useQueryClient();
  const confirm = useConfirm();
  const { reloadMe } = useAuth();
  const roles = useQuery({ queryKey: ["roles"], queryFn: () => api<Role[]>("/roles"), refetchOnWindowFocus: false });
  const catalog = useQuery({ queryKey: ["roles", "permissions"], queryFn: () => api<PermGroup[]>("/roles/permissions"), staleTime: Infinity });
  const [selectedId, setSelectedId] = React.useState<string | null>(null);
  const [draft, setDraft] = React.useState<Draft | null>(null);
  const [createOpen, setCreateOpen] = React.useState(false);

  const list = React.useMemo(() => roles.data ?? [], [roles.data]);
  const selected = list.find((r) => r.id === selectedId) ?? null;
  const locked = selected?.code === "owner";

  React.useEffect(() => {
    if (!selectedId && list.length) setSelectedId(list.find((r) => r.code !== "owner")?.id ?? list[0].id);
  }, [list, selectedId]);

  React.useEffect(() => {
    setDraft(selected ? toDraft(selected) : null);
  }, [selected]);

  const invalidate = () => {
    void qc.invalidateQueries({ queryKey: ["roles"] });
    void qc.invalidateQueries({ queryKey: ["role-options"] });
    void qc.invalidateQueries({ queryKey: ["staff"] });
  };

  const save = useMutation({
    mutationFn: () => api<Role>(`/roles/${selectedId}`, { method: "PATCH", body: toRequest(draft!) }),
    onSuccess: () => {
      toast.success(t("staff.roles.savedToast"));
      invalidate();
      void reloadMe().catch(() => undefined);
    },
    onError: toastError,
  });

  const remove = useMutation({
    mutationFn: (id: string) => api(`/roles/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      toast.success(t("staff.roles.deletedToast"));
      setSelectedId(null);
      invalidate();
    },
    onError: toastError,
  });

  if (roles.isError) return <ErrorBlock onRetry={() => roles.refetch()} />;

  const togglePerm = (code: string, on: boolean) =>
    setDraft((d) => (d ? { ...d, permissions: on ? Array.from(new Set([...d.permissions, code])) : d.permissions.filter((p) => p !== code) } : d));
  const toggleGroup = (codes: string[], on: boolean) =>
    setDraft((d) =>
      d ? { ...d, permissions: on ? Array.from(new Set([...d.permissions, ...codes])) : d.permissions.filter((p) => !codes.includes(p)) } : d,
    );
  const dirty = selected && draft ? JSON.stringify(toRequest(draft)) !== JSON.stringify(toRequest(toDraft(selected))) : false;

  return (
    <div className="grid gap-4 lg:grid-cols-[280px_1fr]">
      <Card className="h-fit">
        <CardHeader className="flex-row items-center justify-between space-y-0">
          <CardTitle>{t("staff.tabs.roles")}</CardTitle>
          <Button size="sm" variant="outline" onClick={() => setCreateOpen(true)}>
            <Plus /> {t("staff.roles.newRole")}
          </Button>
        </CardHeader>
        <CardContent className="p-2">
          {roles.isLoading ? (
            <TableSkeleton rows={4} cols={1} />
          ) : (
            <ul className="space-y-0.5">
              {list.map((r) => (
                <li key={r.id}>
                  <button
                    type="button"
                    onClick={() => setSelectedId(r.id)}
                    className={cn(
                      "flex w-full items-center justify-between gap-2 rounded-md px-2.5 py-2 text-left text-sm hover:bg-muted",
                      r.id === selectedId && "bg-muted font-medium",
                    )}
                  >
                    <span className="min-w-0">
                      <span className="block truncate">{r.name}</span>
                      <span className="text-xs text-muted-foreground">{t("staff.roles.members", { count: r.membersCount })}</span>
                    </span>
                    <Badge variant={r.isPreset ? "muted" : "default"}>{r.isPreset ? t("staff.roles.preset") : t("staff.roles.custom")}</Badge>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>

      {!selected || !draft ? (
        <Card className="p-10 text-center text-sm text-muted-foreground">{roles.isLoading ? t("common.loading") : t("staff.roles.selectRole")}</Card>
      ) : (
        <Card>
          <CardHeader className="flex-row flex-wrap items-end gap-3 space-y-0">
            <Field label={t("staff.roles.name")} className="min-w-60 flex-1">
              <Input value={draft.name} disabled={locked} onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
            </Field>
            <div className="flex gap-2">
              {!selected.isPreset ? (
                <Button
                  variant="outline"
                  className="text-destructive"
                  loading={remove.isPending}
                  onClick={async () => {
                    if (await confirm({ title: t("staff.roles.deleteTitle", { name: selected.name }), description: t("staff.roles.deleteDescription"), destructive: true, confirmText: t("common.delete") }))
                      remove.mutate(selected.id);
                  }}
                >
                  <Trash2 /> {t("common.delete")}
                </Button>
              ) : null}
              <Button disabled={locked || !dirty || !draft.name.trim()} loading={save.isPending} onClick={() => save.mutate()}>
                {t("common.save")}
              </Button>
            </div>
          </CardHeader>
          <CardContent className="space-y-5">
            {locked ? (
              <p className="flex items-center gap-2 rounded-md bg-muted p-2.5 text-sm text-muted-foreground">
                <Lock className="h-4 w-4" /> {t("staff.roles.ownerLocked")}
              </p>
            ) : null}

            <section>
              <h4 className="mb-1 text-sm font-semibold">{t("staff.roles.limits")}</h4>
              <p className="mb-2 text-xs text-muted-foreground">{t("staff.roles.limitsHint")}</p>
              <div className="grid gap-3 sm:grid-cols-3">
                <Field label={t("staff.roles.maxDiscountPct")} hint={t("admin.emptyNoLimit")}>
                  <Input
                    inputMode="decimal"
                    disabled={locked}
                    value={draft.maxDiscountPct}
                    onChange={(e) => setDraft({ ...draft, maxDiscountPct: e.target.value.replace(/[^\d.,]/g, "") })}
                  />
                </Field>
                <Field label={t("staff.roles.maxWriteoffAmount")} hint={t("admin.emptyNoLimit")}>
                  <MoneyInput disabled={locked} value={draft.maxWriteoff} onChange={(v) => setDraft({ ...draft, maxWriteoff: v })} />
                </Field>
                <Field label={t("staff.roles.maxRefundAmount")} hint={t("admin.emptyNoLimit")}>
                  <MoneyInput disabled={locked} value={draft.maxRefund} onChange={(v) => setDraft({ ...draft, maxRefund: v })} />
                </Field>
              </div>
              <label className="mt-3 flex items-center gap-2 text-sm">
                <Switch disabled={locked} checked={draft.canEditClosed} onCheckedChange={(v) => setDraft({ ...draft, canEditClosed: v })} />
                {t("staff.roles.canEditClosedShiftVisits")}
              </label>
            </section>

            <section>
              <h4 className="mb-2 text-sm font-semibold">
                {t("staff.roles.permissions")}{" "}
                <span className="font-normal text-muted-foreground">
                  ({t("staff.roles.count", { count: draft.permissions.length, total: catalog.data?.reduce((s, g) => s + g.codes.length, 0) ?? 0 })})
                </span>
              </h4>
              {catalog.isLoading ? (
                <TableSkeleton rows={3} cols={3} />
              ) : (
                <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
                  {catalog.data?.map((g) => {
                    const on = g.codes.filter((c) => draft.permissions.includes(c)).length;
                    return (
                      <div key={g.group} className="rounded-lg border p-3">
                        <div className="mb-2 flex items-center justify-between gap-2">
                          <span className="text-sm font-medium">{t(`staff.permGroups.${g.group}`)}</span>
                          {!locked ? (
                            <button
                              type="button"
                              className="text-xs text-primary hover:underline"
                              onClick={() => toggleGroup(g.codes, on < g.codes.length)}
                            >
                              {on < g.codes.length ? t("staff.roles.selectAll") : t("staff.roles.selectNone")}
                            </button>
                          ) : null}
                        </div>
                        <div className="space-y-1.5">
                          {g.codes.map((code) => (
                            <label key={code} className="flex items-start gap-2 text-sm" title={code}>
                              <Checkbox
                                className="mt-0.5"
                                disabled={locked}
                                checked={draft.permissions.includes(code)}
                                onCheckedChange={(v) => togglePerm(code, v === true)}
                              />
                              <span>{t(`staff.perms.${code}`)}</span>
                            </label>
                          ))}
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </section>
          </CardContent>
        </Card>
      )}

      <CreateRoleDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        roles={list}
        onCreated={(r) => {
          invalidate();
          setSelectedId(r.id);
        }}
      />
    </div>
  );
}

function CreateRoleDialog({
  open,
  onOpenChange,
  roles,
  onCreated,
}: {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  roles: Role[];
  onCreated: (r: Role) => void;
}) {
  const [name, setName] = React.useState("");
  const [copyFrom, setCopyFrom] = React.useState("");
  React.useEffect(() => {
    if (open) {
      setName("");
      setCopyFrom("");
    }
  }, [open]);

  const create = useMutation({
    mutationFn: () => {
      const src = roles.find((r) => r.id === copyFrom);
      const body: Schemas["RoleRequest"] = {
        name: name.trim(),
        permissions: src ? [...src.permissions] : [],
        limits: src
          ? { ...src.limits }
          : { maxDiscountPct: 0, maxWriteoffAmount: 0, maxRefundAmount: 0, canEditClosedShiftVisits: false },
      };
      return api<Role>("/roles", { method: "POST", body });
    },
    onSuccess: (r) => {
      toast.success(t("staff.roles.createdToast"));
      onCreated(r);
      onOpenChange(false);
    },
    onError: toastError,
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("staff.roles.newRole")}</DialogTitle>
        </DialogHeader>
        <form
          className="space-y-3"
          onSubmit={(e) => {
            e.preventDefault();
            if (name.trim()) create.mutate();
          }}
        >
          <Field label={t("staff.roles.name")}>
            <Input autoFocus value={name} onChange={(e) => setName(e.target.value)} />
          </Field>
          <Field label={t("staff.roles.copyFrom")}>
            <NativeSelect value={copyFrom} onChange={(e) => setCopyFrom(e.target.value)}>
              <option value="">{t("staff.roles.none")}</option>
              {roles.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.name}
                </option>
              ))}
            </NativeSelect>
          </Field>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" disabled={!name.trim()} loading={create.isPending}>
              {t("common.create")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
