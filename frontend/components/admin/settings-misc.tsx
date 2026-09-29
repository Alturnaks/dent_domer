"use client";

import * as React from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Check, Pencil, Plus, Trash2, X } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect, Textarea } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Switch } from "@/components/ui/checkbox";
import { TableSkeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/ui/empty-state";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { useConfirm } from "@/components/ui/confirm";
import { ErrorBlock, toastError } from "@/components/admin/common";
import { api } from "@/lib/api-client";
import { useBranches } from "@/lib/queries";
import { t } from "@/lib/i18n";
import type { NamedRef, Schemas } from "@/lib/types";

type RefPath = "/cancel-reasons" | "/lead-sources" | "/writeoff-reasons" | "/expense-categories";

const REFS: { path: RefPath; title: string; types?: string[]; typeNs?: string }[] = [
  { path: "/cancel-reasons", title: "settings.references.cancelReasons", types: ["Cancel", "Reschedule"], typeNs: "cancel" },
  { path: "/lead-sources", title: "settings.references.leadSources" },
  { path: "/writeoff-reasons", title: "settings.references.writeoffReasons", types: ["Expired", "Damaged", "Defect", "Lost", "Other"], typeNs: "writeoff" },
  {
    path: "/expense-categories",
    title: "settings.references.expenseCategories",
    types: ["Rent", "Utilities", "Salary", "Supplies", "Lab", "Marketing", "Other"],
    typeNs: "expense",
  },
];

export function ReferencesTab() {
  return (
    <div className="grid gap-4 lg:grid-cols-2">
      {REFS.map((r) => (
        <ReferenceList key={r.path} {...r} />
      ))}
    </div>
  );
}

/** Универсальный CRUD справочника (NamedRef с опциональным типом). */
export function ReferenceList({ path, title, types, typeNs }: { path: RefPath; title: string; types?: string[]; typeNs?: string }) {
  const qc = useQueryClient();
  const confirm = useConfirm();
  const list = useQuery({ queryKey: ["ref", path], queryFn: () => api<NamedRef[]>(path) });
  const [newName, setNewName] = React.useState("");
  const [newType, setNewType] = React.useState(types?.[0] ?? "");
  const [editId, setEditId] = React.useState<string | null>(null);
  const [editName, setEditName] = React.useState("");
  const [editType, setEditType] = React.useState("");

  const typeLabel = (v: string | null | undefined) => (v && typeNs ? t(`settings.references.types.${typeNs}.${v}`) : "");
  const invalidate = () => void qc.invalidateQueries({ queryKey: ["ref", path] });

  const create = useMutation({
    mutationFn: () => api<NamedRef>(path, { method: "POST", body: { name: newName.trim(), type: types ? newType : null } satisfies Schemas["NamedRefRequest"] }),
    onSuccess: () => {
      setNewName("");
      invalidate();
    },
    onError: toastError,
  });
  const update = useMutation({
    mutationFn: () => api<NamedRef>(`${path}/${editId}`, { method: "PATCH", body: { name: editName.trim(), type: types ? editType : null } satisfies Schemas["NamedRefRequest"] }),
    onSuccess: () => {
      setEditId(null);
      toast.success(t("common.saved"));
      invalidate();
    },
    onError: toastError,
  });
  const remove = useMutation({
    mutationFn: (id: string) => api(`${path}/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      toast.success(t("admin.deleted"));
      invalidate();
    },
    onError: toastError,
  });

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t(title)}</CardTitle>
      </CardHeader>
      <CardContent>
        {list.isLoading ? (
          <TableSkeleton rows={3} cols={2} />
        ) : list.isError ? (
          <ErrorBlock onRetry={() => list.refetch()} />
        ) : (list.data ?? []).length === 0 ? (
          <p className="py-3 text-sm text-muted-foreground">{t("settings.references.empty")}</p>
        ) : (
          <ul className="divide-y rounded-md border">
            {list.data!.map((r) =>
              editId === r.id ? (
                <li key={r.id} className="flex items-center gap-2 px-2 py-1.5">
                  <Input
                    autoFocus
                    className="h-8 flex-1"
                    value={editName}
                    onChange={(e) => setEditName(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === "Enter" && editName.trim()) update.mutate();
                      if (e.key === "Escape") setEditId(null);
                    }}
                  />
                  {types ? (
                    <NativeSelect className="h-8 w-36" value={editType} onChange={(e) => setEditType(e.target.value)}>
                      {types.map((v) => (
                        <option key={v} value={v}>
                          {typeLabel(v)}
                        </option>
                      ))}
                    </NativeSelect>
                  ) : null}
                  <Button size="icon-sm" variant="ghost" disabled={!editName.trim()} loading={update.isPending} onClick={() => update.mutate()}>
                    <Check />
                  </Button>
                  <Button size="icon-sm" variant="ghost" onClick={() => setEditId(null)}>
                    <X />
                  </Button>
                </li>
              ) : (
                <li key={r.id} className="group flex items-center gap-2 px-3 py-1.5 text-sm">
                  <span className="flex-1">{r.name}</span>
                  {types && r.type ? <Badge variant="muted">{typeLabel(r.type)}</Badge> : null}
                  <Button
                    size="icon-sm"
                    variant="ghost"
                    title={t("common.edit")}
                    onClick={() => {
                      setEditId(r.id);
                      setEditName(r.name);
                      setEditType(r.type ?? types?.[0] ?? "");
                    }}
                  >
                    <Pencil />
                  </Button>
                  <Button
                    size="icon-sm"
                    variant="ghost"
                    className="text-destructive"
                    title={t("common.delete")}
                    onClick={async () => {
                      if (
                        await confirm({
                          title: t("settings.references.deleteTitle", { name: r.name }),
                          description: t("settings.references.deleteDescription"),
                          destructive: true,
                          confirmText: t("common.delete"),
                        })
                      )
                        remove.mutate(r.id);
                    }}
                  >
                    <Trash2 />
                  </Button>
                </li>
              ),
            )}
          </ul>
        )}
        <form
          className="mt-2 flex gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            if (newName.trim()) create.mutate();
          }}
        >
          <Input className="h-8" placeholder={t("settings.references.newItem")} value={newName} onChange={(e) => setNewName(e.target.value)} />
          {types ? (
            <NativeSelect className="h-8 w-36" value={newType} onChange={(e) => setNewType(e.target.value)} aria-label={t("settings.references.type")}>
              {types.map((v) => (
                <option key={v} value={v}>
                  {typeLabel(v)}
                </option>
              ))}
            </NativeSelect>
          ) : null}
          <Button size="sm" type="submit" disabled={!newName.trim()} loading={create.isPending}>
            <Plus /> {t("common.add")}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}

export function CashRegistersTab() {
  const qc = useQueryClient();
  const branches = useBranches();
  const list = useQuery({ queryKey: ["cash-registers", "all"], queryFn: () => api<Schemas["CashRegisterDto"][]>("/cash-registers") });
  const [name, setName] = React.useState("");
  const [branchId, setBranchId] = React.useState("");
  const branchName = React.useMemo(() => new Map((branches.data ?? []).map((b) => [b.id, b.name])), [branches.data]);

  React.useEffect(() => {
    if (!branchId && branches.data?.length) setBranchId(branches.data[0].id);
  }, [branches.data, branchId]);

  const create = useMutation({
    mutationFn: () => api("/cash-registers", { method: "POST", body: { branchId, name: name.trim() } satisfies Schemas["CashRegisterRequest"] }),
    onSuccess: () => {
      setName("");
      toast.success(t("settings.registers.createdToast"));
      void qc.invalidateQueries({ queryKey: ["cash-registers"] });
    },
    onError: toastError,
  });

  const rows = [...(list.data ?? [])].sort((a, b) => (branchName.get(a.branchId) ?? "").localeCompare(branchName.get(b.branchId) ?? ""));
  return (
    <Card>
      <form
        className="flex flex-wrap gap-2 border-b p-3"
        onSubmit={(e) => {
          e.preventDefault();
          if (name.trim() && branchId) create.mutate();
        }}
      >
        <Input className="w-64" placeholder={t("settings.registers.name")} value={name} onChange={(e) => setName(e.target.value)} />
        <NativeSelect className="w-64" value={branchId} onChange={(e) => setBranchId(e.target.value)} aria-label={t("settings.registers.branch")}>
          {branches.data?.map((b) => (
            <option key={b.id} value={b.id}>
              {b.name}
            </option>
          ))}
        </NativeSelect>
        <Button type="submit" disabled={!name.trim() || !branchId} loading={create.isPending}>
          <Plus /> {t("settings.registers.add")}
        </Button>
      </form>
      {list.isLoading ? (
        <TableSkeleton rows={3} cols={2} />
      ) : list.isError ? (
        <ErrorBlock className="m-4" onRetry={() => list.refetch()} />
      ) : rows.length === 0 ? (
        <EmptyState className="m-4" title={t("settings.registers.empty")} />
      ) : (
        <Table>
          <THead>
            <TR>
              <TH>{t("settings.registers.name")}</TH>
              <TH>{t("settings.registers.branch")}</TH>
            </TR>
          </THead>
          <TBody>
            {rows.map((r) => (
              <TR key={r.id}>
                <TD className="font-medium">{r.name}</TD>
                <TD>{branchName.get(r.branchId) ?? "—"}</TD>
              </TR>
            ))}
          </TBody>
        </Table>
      )}
      <p className="p-3 text-xs text-muted-foreground">{t("settings.registers.hint")}</p>
    </Card>
  );
}

const VARS = ["patient_name", "date", "time", "doctor", "branch_address"];

export function TemplatesTab() {
  const list = useQuery({ queryKey: ["message-templates"], queryFn: () => api<Schemas["MessageTemplateDto"][]>("/message-templates"), refetchOnWindowFocus: false });
  if (list.isLoading) return <TableSkeleton rows={4} cols={2} />;
  if (list.isError) return <ErrorBlock onRetry={() => list.refetch()} />;
  if (!list.data?.length) return <EmptyState title={t("settings.templates.empty")} />;
  return (
    <div className="grid gap-4 lg:grid-cols-2">
      {list.data.map((tpl) => (
        <TemplateCard key={tpl.id} tpl={tpl} />
      ))}
    </div>
  );
}

function TemplateCard({ tpl }: { tpl: Schemas["MessageTemplateDto"] }) {
  const qc = useQueryClient();
  const ref = React.useRef<HTMLTextAreaElement>(null);
  const [text, setText] = React.useState(tpl.text);
  const [active, setActive] = React.useState(tpl.isActive);
  React.useEffect(() => {
    setText(tpl.text);
    setActive(tpl.isActive);
  }, [tpl]);

  const save = useMutation({
    mutationFn: () => api(`/message-templates/${tpl.id}`, { method: "PATCH", body: { text, isActive: active } satisfies Schemas["MessageTemplateRequest"] }),
    onSuccess: () => {
      toast.success(t("settings.templates.savedToast"));
      void qc.invalidateQueries({ queryKey: ["message-templates"] });
    },
    onError: toastError,
  });

  const insert = (v: string) => {
    const el = ref.current;
    const token = `{${v}}`;
    if (!el) return setText((p) => p + token);
    const s = el.selectionStart ?? text.length;
    const e = el.selectionEnd ?? text.length;
    const next = text.slice(0, s) + token + text.slice(e);
    setText(next);
    requestAnimationFrame(() => {
      el.focus();
      el.setSelectionRange(s + token.length, s + token.length);
    });
  };

  const preview = VARS.reduce((acc, v) => acc.replaceAll(`{${v}}`, t(`settings.templates.sample.${v}`)), text);
  const typeKey = `settings.templates.types.${tpl.type}`;
  const typeName = t(typeKey) === typeKey ? tpl.type : t(typeKey);
  const dirty = text !== tpl.text || active !== tpl.isActive;

  return (
    <Card>
      <CardHeader className="flex-row items-center justify-between space-y-0">
        <div>
          <CardTitle>{typeName}</CardTitle>
          <p className="mt-1 text-xs text-muted-foreground">{tpl.channel}</p>
        </div>
        <label className="flex items-center gap-2 text-sm">
          <Switch checked={active} onCheckedChange={setActive} />
          {t("settings.templates.isActive")}
        </label>
      </CardHeader>
      <CardContent className="space-y-2">
        <Textarea ref={ref} rows={4} value={text} onChange={(e) => setText(e.target.value)} />
        <div className="flex flex-wrap items-center gap-1 text-xs text-muted-foreground">
          <span>{t("settings.templates.placeholders")}</span>
          {VARS.map((v) => (
            <button
              key={v}
              type="button"
              title={t(`settings.templates.vars.${v}`)}
              className="rounded border bg-muted px-1.5 py-0.5 font-mono text-[11px] text-foreground hover:bg-accent"
              onClick={() => insert(v)}
            >
              {`{${v}}`}
            </button>
          ))}
        </div>
        <div className="rounded-md bg-muted/60 p-2 text-xs">
          <span className="font-medium">{t("settings.templates.preview")}: </span>
          {preview}
        </div>
        <div className="flex justify-end gap-2">
          <Button
            size="sm"
            variant="outline"
            disabled={!dirty}
            onClick={() => {
              setText(tpl.text);
              setActive(tpl.isActive);
            }}
          >
            {t("common.reset")}
          </Button>
          <Button size="sm" disabled={!dirty || !text.trim()} loading={save.isPending} onClick={() => save.mutate()}>
            {t("common.save")}
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
