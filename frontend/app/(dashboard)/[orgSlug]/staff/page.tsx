"use client";

import * as React from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Pencil, Plus, UserX } from "lucide-react";
import { PageHeader, EmptyState } from "@/components/ui/empty-state";
import { Button } from "@/components/ui/button";
import { NativeSelect } from "@/components/ui/input";
import { Card } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Checkbox } from "@/components/ui/checkbox";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { TableSkeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useConfirm } from "@/components/ui/confirm";
import { POSITIONS, StaffFormDialog } from "@/components/admin/staff-form-dialog";
import { RolesTab } from "@/components/admin/roles-tab";
import { ErrorBlock, NoAccess, toastError } from "@/components/admin/common";
import { api } from "@/lib/api-client";
import { useAuth } from "@/lib/auth";
import { useBranches } from "@/lib/queries";
import { formatDate, formatPhone } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import type { Staff } from "@/lib/types";

export default function StaffPage() {
  const { can } = useAuth();
  const canStaff = can(P.staffManage);
  const canRoles = can(P.rolesManage);
  const [formOpen, setFormOpen] = React.useState(false);
  const [editing, setEditing] = React.useState<Staff | null>(null);

  if (!canStaff && !canRoles) return <NoAccess />;

  return (
    <div>
      <PageHeader
        title={t("staff.title")}
        actions={
          canStaff ? (
            <Button
              onClick={() => {
                setEditing(null);
                setFormOpen(true);
              }}
            >
              <Plus /> {t("staff.add")}
            </Button>
          ) : null
        }
      />
      <Tabs defaultValue={canStaff ? "employees" : "roles"}>
        <TabsList>
          {canStaff ? <TabsTrigger value="employees">{t("staff.tabs.employees")}</TabsTrigger> : null}
          {canRoles ? <TabsTrigger value="roles">{t("staff.tabs.roles")}</TabsTrigger> : null}
        </TabsList>
        {canStaff ? (
          <TabsContent value="employees">
            <EmployeesTab
              onEdit={(s) => {
                setEditing(s);
                setFormOpen(true);
              }}
              onCreate={() => {
                setEditing(null);
                setFormOpen(true);
              }}
            />
          </TabsContent>
        ) : null}
        {canRoles ? (
          <TabsContent value="roles">
            <RolesTab />
          </TabsContent>
        ) : null}
      </Tabs>
      {canStaff ? <StaffFormDialog open={formOpen} onOpenChange={setFormOpen} staff={editing} /> : null}
    </div>
  );
}

function EmployeesTab({ onEdit, onCreate }: { onEdit: (s: Staff) => void; onCreate: () => void }) {
  const qc = useQueryClient();
  const confirm = useConfirm();
  const { me } = useAuth();
  const branches = useBranches();
  const [position, setPosition] = React.useState("");
  const [branch, setBranch] = React.useState("");
  const [fired, setFired] = React.useState(false);

  const query = useQuery({
    queryKey: ["staff", { position, branch, fired }],
    queryFn: () => api<Staff[]>("/staff", { query: { position: position || undefined, branch_id: branch || undefined, include_fired: fired || undefined } }),
  });

  const fire = useMutation({
    mutationFn: (id: string) => api<Staff>(`/staff/${id}/fire`, { method: "POST" }),
    onSuccess: () => {
      toast.success(t("staff.firedToast"));
      void qc.invalidateQueries({ queryKey: ["staff"] });
      void qc.invalidateQueries({ queryKey: ["doctors"] });
      void qc.invalidateQueries({ queryKey: ["roles"] });
    },
    onError: toastError,
  });

  const branchName = React.useMemo(() => new Map((branches.data ?? []).map((b) => [b.id, b.name])), [branches.data]);
  const rows = query.data ?? [];

  return (
    <>
      <Card className="mb-3 flex flex-wrap items-center gap-3 p-3">
        <NativeSelect className="w-52" value={position} onChange={(e) => setPosition(e.target.value)} aria-label={t("staff.position")}>
          <option value="">{t("staff.allPositions")}</option>
          {POSITIONS.map((p) => (
            <option key={p} value={p}>
              {t(`staff.positions.${p}`)}
            </option>
          ))}
        </NativeSelect>
        <NativeSelect className="w-60" value={branch} onChange={(e) => setBranch(e.target.value)} aria-label={t("staff.branches")}>
          <option value="">{t("staff.allBranchesFilter")}</option>
          {branches.data?.map((b) => (
            <option key={b.id} value={b.id}>
              {b.name}
            </option>
          ))}
        </NativeSelect>
        <label className="flex items-center gap-2 text-sm">
          <Checkbox checked={fired} onCheckedChange={(v) => setFired(v === true)} />
          {t("staff.showFired")}
        </label>
      </Card>
      <Card>
        {query.isLoading ? (
          <TableSkeleton />
        ) : query.isError ? (
          <ErrorBlock className="m-4" onRetry={() => query.refetch()} />
        ) : rows.length === 0 ? (
          <EmptyState className="m-4" title={t("staff.empty")} description={t("staff.emptyHint")} action={<Button onClick={onCreate}>{t("staff.add")}</Button>} />
        ) : (
          <Table>
            <THead>
              <TR>
                <TH>{t("staff.fullName")}</TH>
                <TH>{t("staff.position")}</TH>
                <TH>{t("staff.role")}</TH>
                <TH>{t("staff.branches")}</TH>
                <TH>{t("staff.contacts")}</TH>
                <TH>{t("staff.status")}</TH>
                <TH className="w-24" />
              </TR>
            </THead>
            <TBody>
              {rows.map((s) => {
                const isFired = !s.isActive || !!s.firedAt;
                return (
                  <TR key={s.membershipId} className={isFired ? "opacity-60" : undefined}>
                    <TD>
                      <div className="flex items-center gap-2">
                        {s.color ? <span className="h-2.5 w-2.5 shrink-0 rounded-full" style={{ backgroundColor: s.color }} /> : null}
                        <div>
                          <div className="font-medium">{s.fullName}</div>
                          {s.specialty ? <div className="text-xs text-muted-foreground">{s.specialty}</div> : null}
                        </div>
                      </div>
                    </TD>
                    <TD className="whitespace-nowrap">{t(`staff.positions.${s.position}`)}</TD>
                    <TD className="whitespace-nowrap">{s.roleName}</TD>
                    <TD className="max-w-64">
                      {s.allBranches ? (
                        <Badge variant="secondary">{t("common.allBranches")}</Badge>
                      ) : (
                        <div className="flex flex-wrap gap-1">
                          {s.branchIds.map((id) => (
                            <Badge key={id} variant="muted">
                              {branchName.get(id) ?? "—"}
                            </Badge>
                          ))}
                        </div>
                      )}
                    </TD>
                    <TD className="text-xs">
                      {s.email ? <div>{s.email}</div> : null}
                      {s.phone ? <div className="whitespace-nowrap text-muted-foreground">{formatPhone(s.phone)}</div> : null}
                    </TD>
                    <TD>
                      {isFired ? (
                        <Badge variant="destructive">{t("staff.fired", { date: formatDate(s.firedAt) })}</Badge>
                      ) : (
                        <Badge variant="success">{t("staff.working")}</Badge>
                      )}
                    </TD>
                    <TD className="text-right">
                      <div className="flex justify-end gap-1">
                        <Button size="icon-sm" variant="ghost" title={t("common.edit")} onClick={() => onEdit(s)}>
                          <Pencil />
                        </Button>
                        {!isFired && s.userId !== me?.user.id ? (
                          <Button
                            size="icon-sm"
                            variant="ghost"
                            className="text-destructive"
                            title={t("staff.fire")}
                            onClick={async () => {
                              if (
                                await confirm({
                                  title: t("staff.fireTitle", { name: s.fullName }),
                                  description: t("staff.fireDescription"),
                                  destructive: true,
                                  confirmText: t("staff.fire"),
                                })
                              )
                                fire.mutate(s.membershipId);
                            }}
                          >
                            <UserX />
                          </Button>
                        ) : null}
                      </div>
                    </TD>
                  </TR>
                );
              })}
            </TBody>
          </Table>
        )}
      </Card>
    </>
  );
}
