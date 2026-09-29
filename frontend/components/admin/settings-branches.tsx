"use client";

import * as React from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Armchair, Pencil, Plus } from "lucide-react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Checkbox, Switch } from "@/components/ui/checkbox";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { TableSkeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { ErrorBlock, hhmm, hhmmss, toastError } from "@/components/admin/common";
import { api } from "@/lib/api-client";
import { useAuth } from "@/lib/auth";
import { formatPhone } from "@/lib/format";
import { t } from "@/lib/i18n";
import type { Branch, Chair, Schemas } from "@/lib/types";
import { useScheduleConflict } from "@/components/schedule/schedule-conflict";

type Room = Schemas["RoomDto"];
type Day = { dayOfWeek: number; isWorking: boolean; open: string; close: string };
const WEEK = [1, 2, 3, 4, 5, 6, 0];

function normalizeDays(b: Branch | null): Day[] {
  const src = b?.workingHours?.days ?? [];
  return WEEK.map((dow) => {
    const d = src.find((x) => x.dayOfWeek === dow);
    return {
      dayOfWeek: dow,
      isWorking: d ? (d.isWorking ?? false) : dow !== 0,
      open: hhmm(d?.open) || "09:00",
      close: hhmm(d?.close) || (dow === 6 ? "15:00" : "20:00"),
    };
  });
}

function hoursSummary(b: Branch): string {
  const days = normalizeDays(b);
  const groups: { from: number; to: number; text: string }[] = [];
  days.forEach((d, i) => {
    const text = d.isWorking ? `${d.open}–${d.close}` : t("settings.branches.dayOff");
    const last = groups[groups.length - 1];
    if (last && last.text === text && last.to === i - 1) last.to = i;
    else groups.push({ from: i, to: i, text });
  });
  const name = (i: number) => t(`settings.branches.days.${days[i].dayOfWeek}`);
  return groups.map((g) => `${name(g.from)}${g.to > g.from ? `–${name(g.to)}` : ""} ${g.text}`).join(", ");
}

export function BranchesTab() {
  const branches = useQuery({ queryKey: ["branches", "with-inactive"], queryFn: () => api<Branch[]>("/branches", { query: { include_inactive: true } }) });
  const [editing, setEditing] = React.useState<Branch | null>(null);
  const [formOpen, setFormOpen] = React.useState(false);
  const [roomsFor, setRoomsFor] = React.useState<Branch | null>(null);

  const rows = branches.data ?? [];
  return (
    <>
      <div className="mb-3 flex justify-end">
        <Button
          onClick={() => {
            setEditing(null);
            setFormOpen(true);
          }}
        >
          <Plus /> {t("settings.branches.add")}
        </Button>
      </div>
      <Card>
        {branches.isLoading ? (
          <TableSkeleton rows={3} />
        ) : branches.isError ? (
          <ErrorBlock className="m-4" onRetry={() => branches.refetch()} />
        ) : rows.length === 0 ? (
          <EmptyState className="m-4" title={t("settings.branches.empty")} />
        ) : (
          <Table>
            <THead>
              <TR>
                <TH>{t("settings.branches.name")}</TH>
                <TH>{t("settings.branches.address")}</TH>
                <TH>{t("settings.branches.phone")}</TH>
                <TH>{t("settings.branches.hours")}</TH>
                <TH>{t("common.status")}</TH>
                <TH className="w-24" />
              </TR>
            </THead>
            <TBody>
              {rows.map((b) => (
                <TR key={b.id} className={b.isActive ? undefined : "opacity-60"}>
                  <TD className="font-medium">{b.name}</TD>
                  <TD>{b.address ?? "—"}</TD>
                  <TD className="whitespace-nowrap">{formatPhone(b.phone)}</TD>
                  <TD className="text-xs text-muted-foreground">{hoursSummary(b)}</TD>
                  <TD>{b.isActive ? <Badge variant="success">{t("admin.active")}</Badge> : <Badge variant="muted">{t("admin.inactive")}</Badge>}</TD>
                  <TD>
                    <div className="flex justify-end gap-1">
                      <Button size="icon-sm" variant="ghost" title={t("settings.branches.rooms")} onClick={() => setRoomsFor(b)}>
                        <Armchair />
                      </Button>
                      <Button
                        size="icon-sm"
                        variant="ghost"
                        title={t("common.edit")}
                        onClick={() => {
                          setEditing(b);
                          setFormOpen(true);
                        }}
                      >
                        <Pencil />
                      </Button>
                    </div>
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
        )}
      </Card>
      <BranchDialog open={formOpen} onOpenChange={setFormOpen} branch={editing} />
      <RoomsChairsDialog branch={roomsFor} onOpenChange={(o) => !o && setRoomsFor(null)} />
    </>
  );
}

function BranchDialog({ open, onOpenChange, branch }: { open: boolean; onOpenChange: (o: boolean) => void; branch: Branch | null }) {
  const qc = useQueryClient();
  const { reloadMe } = useAuth();
  const [name, setName] = React.useState("");
  const [address, setAddress] = React.useState("");
  const [phone, setPhone] = React.useState("");
  const [isActive, setIsActive] = React.useState(true);
  const [days, setDays] = React.useState<Day[]>([]);

  React.useEffect(() => {
    if (!open) return;
    setName(branch?.name ?? "");
    setAddress(branch?.address ?? "");
    setPhone(branch?.phone ?? "");
    setIsActive(branch?.isActive ?? true);
    setDays(normalizeDays(branch));
  }, [open, branch]);

  const run = useScheduleConflict();
  const save = useMutation({
    mutationFn: () => {
      const body: Schemas["BranchRequest"] = {
        name: name.trim(),
        address: address.trim() || null,
        phone: phone.trim() || null,
        isActive,
        workingHours: { days: days.map((d) => ({ dayOfWeek: d.dayOfWeek, isWorking: d.isWorking, open: hhmmss(d.open), close: hhmmss(d.close) })) },
      };
      return branch ? run((onConflict) => api<Branch>(`/branches/${branch.id}`, { method: "PATCH", body: { ...body, onConflict } })) : api<Branch>("/branches", { method: "POST", body });
    },
    onSuccess: () => {
      toast.success(branch ? t("settings.branches.savedToast") : t("settings.branches.createdToast"));
      void qc.invalidateQueries({ queryKey: ["branches"] });
      void reloadMe().catch(() => undefined);
      onOpenChange(false);
    },
    onError: toastError,
  });

  const setDay = (i: number, patch: Partial<Day>) => setDays((p) => p.map((d, j) => (j === i ? { ...d, ...patch } : d)));

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent wide>
        <DialogHeader>
          <DialogTitle>{branch ? `${t("settings.branches.editTitle")}: ${branch.name}` : t("settings.branches.newTitle")}</DialogTitle>
        </DialogHeader>
        <form
          className="grid gap-3 sm:grid-cols-2"
          onSubmit={(e) => {
            e.preventDefault();
            if (name.trim()) save.mutate();
          }}
        >
          <Field label={t("settings.branches.name")} className="sm:col-span-2">
            <Input autoFocus value={name} onChange={(e) => setName(e.target.value)} />
          </Field>
          <Field label={t("settings.branches.address")}>
            <Input value={address} onChange={(e) => setAddress(e.target.value)} />
          </Field>
          <Field label={t("settings.branches.phone")}>
            <Input type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} />
          </Field>
          <label className="flex items-center gap-2 text-sm sm:col-span-2">
            <Switch checked={isActive} onCheckedChange={setIsActive} />
            {t("settings.branches.isActive")}
          </label>
          <div className="sm:col-span-2">
            <div className="mb-1.5 text-sm font-medium">{t("settings.branches.hours")}</div>
            <div className="divide-y rounded-md border">
              {days.map((d, i) => (
                <div key={d.dayOfWeek} className="flex flex-wrap items-center gap-3 px-3 py-1.5">
                  <label className="flex w-28 items-center gap-2 text-sm">
                    <Checkbox checked={d.isWorking} onCheckedChange={(v) => setDay(i, { isWorking: v === true })} />
                    {t(`settings.branches.days.${d.dayOfWeek}`)}
                  </label>
                  {d.isWorking ? (
                    <div className="flex items-center gap-2">
                      <Input type="time" className="h-8 w-28" value={d.open} onChange={(e) => setDay(i, { open: e.target.value })} />
                      <span className="text-muted-foreground">—</span>
                      <Input type="time" className="h-8 w-28" value={d.close} onChange={(e) => setDay(i, { close: e.target.value })} />
                    </div>
                  ) : (
                    <span className="text-sm text-muted-foreground">{t("settings.branches.dayOff")}</span>
                  )}
                </div>
              ))}
            </div>
          </div>
          <DialogFooter className="sm:col-span-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" disabled={!name.trim()} loading={save.isPending}>
              {branch ? t("common.save") : t("common.create")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function RoomsChairsDialog({ branch, onOpenChange }: { branch: Branch | null; onOpenChange: (o: boolean) => void }) {
  const qc = useQueryClient();
  const id = branch?.id;
  const rooms = useQuery({ queryKey: ["rooms", id], queryFn: () => api<Room[]>(`/branches/${id}/rooms`), enabled: !!id });
  const chairs = useQuery({ queryKey: ["branch-chairs", id], queryFn: () => api<Chair[]>(`/branches/${id}/chairs`), enabled: !!id });
  const [roomName, setRoomName] = React.useState("");
  const [chairName, setChairName] = React.useState("");
  const [chairRoom, setChairRoom] = React.useState("");

  React.useEffect(() => {
    setRoomName("");
    setChairName("");
    setChairRoom("");
  }, [id]);

  const invalidateChairs = () => {
    void qc.invalidateQueries({ queryKey: ["branch-chairs", id] });
    void qc.invalidateQueries({ queryKey: ["chairs"] });
  };

  const addRoom = useMutation({
    mutationFn: () => api<Room>(`/branches/${id}/rooms`, { method: "POST", body: { name: roomName.trim() } satisfies Schemas["RoomRequest"] }),
    onSuccess: () => {
      setRoomName("");
      void qc.invalidateQueries({ queryKey: ["rooms", id] });
    },
    onError: toastError,
  });
  const addChair = useMutation({
    mutationFn: () =>
      api<Chair>(`/branches/${id}/chairs`, {
        method: "POST",
        body: { name: chairName.trim(), roomId: chairRoom || null, isActive: true } satisfies Schemas["ChairRequest"],
      }),
    onSuccess: () => {
      setChairName("");
      invalidateChairs();
    },
    onError: toastError,
  });
  const run = useScheduleConflict();
  const updateChair = useMutation({
    mutationFn: ({ chair, patch }: { chair: Chair; patch: Partial<Schemas["ChairRequest"]> }) =>
      run((onConflict) =>
        api<Chair>(`/chairs/${chair.id}`, {
          method: "PATCH",
          body: { name: chair.name, roomId: chair.roomId ?? null, isActive: chair.isActive, ...patch, onConflict } satisfies Schemas["ChairRequest"],
        }),
      ),
    onSuccess: () => {
      toast.success(t("common.saved"));
      invalidateChairs();
    },
    onError: toastError,
  });

  const roomList = rooms.data ?? [];
  return (
    <Dialog open={!!branch} onOpenChange={onOpenChange}>
      <DialogContent wide>
        <DialogHeader>
          <DialogTitle>{t("settings.branches.roomsTitle", { name: branch?.name ?? "" })}</DialogTitle>
        </DialogHeader>
        <div className="grid gap-5 md:grid-cols-[1fr_1.5fr]">
          <section>
            <h4 className="mb-2 text-sm font-semibold">{t("settings.branches.roomsList")}</h4>
            {rooms.isLoading ? (
              <TableSkeleton rows={2} cols={1} />
            ) : roomList.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t("settings.branches.noRooms")}</p>
            ) : (
              <ul className="mb-2 divide-y rounded-md border text-sm">
                {roomList.map((r) => (
                  <li key={r.id} className="px-3 py-1.5">
                    {r.name}
                  </li>
                ))}
              </ul>
            )}
            <form
              className="mt-2 flex gap-2"
              onSubmit={(e) => {
                e.preventDefault();
                if (roomName.trim()) addRoom.mutate();
              }}
            >
              <Input className="h-8" placeholder={t("settings.branches.newRoom")} value={roomName} onChange={(e) => setRoomName(e.target.value)} />
              <Button size="sm" type="submit" disabled={!roomName.trim()} loading={addRoom.isPending}>
                <Plus />
              </Button>
            </form>
          </section>
          <section>
            <h4 className="mb-2 text-sm font-semibold">{t("settings.branches.chairsList")}</h4>
            {chairs.isLoading ? (
              <TableSkeleton rows={2} cols={3} />
            ) : (chairs.data ?? []).length === 0 ? (
              <p className="text-sm text-muted-foreground">{t("settings.branches.noChairs")}</p>
            ) : (
              <div className="divide-y rounded-md border">
                {chairs.data!.map((c) => (
                  <ChairRow key={c.id} chair={c} rooms={roomList} onSave={(patch) => updateChair.mutate({ chair: c, patch })} />
                ))}
              </div>
            )}
            <form
              className="mt-2 flex gap-2"
              onSubmit={(e) => {
                e.preventDefault();
                if (chairName.trim()) addChair.mutate();
              }}
            >
              <Input className="h-8" placeholder={t("settings.branches.newChair")} value={chairName} onChange={(e) => setChairName(e.target.value)} />
              <NativeSelect className="h-8 w-40" value={chairRoom} onChange={(e) => setChairRoom(e.target.value)}>
                <option value="">{t("settings.branches.noRoom")}</option>
                {roomList.map((r) => (
                  <option key={r.id} value={r.id}>
                    {r.name}
                  </option>
                ))}
              </NativeSelect>
              <Button size="sm" type="submit" disabled={!chairName.trim()} loading={addChair.isPending}>
                <Plus />
              </Button>
            </form>
          </section>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function ChairRow({ chair, rooms, onSave }: { chair: Chair; rooms: Room[]; onSave: (patch: Partial<Schemas["ChairRequest"]>) => void }) {
  const [name, setName] = React.useState(chair.name);
  React.useEffect(() => setName(chair.name), [chair.name]);
  const commitName = () => {
    if (name.trim() && name.trim() !== chair.name) onSave({ name: name.trim() });
    else setName(chair.name);
  };
  return (
    <div className="flex items-center gap-2 px-2 py-1.5">
      <Input
        className="h-8 flex-1"
        value={name}
        onChange={(e) => setName(e.target.value)}
        onBlur={commitName}
        onKeyDown={(e) => {
          if (e.key === "Enter") {
            e.preventDefault();
            commitName();
          }
        }}
      />
      <NativeSelect className="h-8 w-36" value={chair.roomId ?? ""} onChange={(e) => onSave({ roomId: e.target.value || null })}>
        <option value="">{t("settings.branches.noRoom")}</option>
        {rooms.map((r) => (
          <option key={r.id} value={r.id}>
            {r.name}
          </option>
        ))}
      </NativeSelect>
      <Switch checked={chair.isActive} onCheckedChange={(v) => onSave({ isActive: v })} title={chair.isActive ? t("admin.active") : t("admin.inactive")} />
    </div>
  );
}
