"use client";

import * as React from "react";
import Link from "next/link";
import { useInfiniteQuery } from "@tanstack/react-query";
import { Copy, Plus, Search, Star } from "lucide-react";
import { PageHeader, EmptyState } from "@/components/ui/empty-state";
import { Button } from "@/components/ui/button";
import { Input, NativeSelect } from "@/components/ui/input";
import { Card } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Checkbox } from "@/components/ui/checkbox";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { TableSkeleton } from "@/components/ui/skeleton";
import { PatientFormDialog } from "@/components/patients/patient-form-dialog";
import { api } from "@/lib/api-client";
import { useAuth, useOrgHref } from "@/lib/auth";
import { useReference } from "@/lib/queries";
import { formatDate, formatMoney, formatPhone } from "@/lib/format";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";
import type { CursorPage, PatientListItem } from "@/lib/types";

export default function PatientsPage() {
  const { can } = useAuth();
  const { href, push } = useOrgHref();
  const [q, setQ] = React.useState("");
  const [debounced, setDebounced] = React.useState("");
  const [debtors, setDebtors] = React.useState(false);
  const [months, setMonths] = React.useState("");
  const [source, setSource] = React.useState("");
  const [tag, setTag] = React.useState("");
  const [createOpen, setCreateOpen] = React.useState(false);
  const sources = useReference("/lead-sources");

  React.useEffect(() => {
    const id = setTimeout(() => setDebounced(q.trim()), 300);
    return () => clearTimeout(id);
  }, [q]);

  const query = useInfiniteQuery({
    queryKey: ["patients", debounced, debtors, months, source, tag],
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) =>
      api<CursorPage<PatientListItem>>("/patients", {
        query: { q: debounced, debtors: debtors || undefined, not_visited_months: months || undefined, source: source || undefined, tag: tag || undefined, cursor: pageParam ?? undefined, limit: 50 },
      }),
    getNextPageParam: (last) => last.nextCursor ?? null,
  });
  const rows = query.data?.pages.flatMap((p) => p.items) ?? [];

  return (
    <div>
      <PageHeader
        title={t("patients.title")}
        actions={
          <>
            {can(P.patientsMerge) ? (
              <Button variant="outline" asChild>
                <Link href={href("patients/duplicates")}>
                  <Copy /> {t("patients.duplicatesTitle")}
                </Link>
              </Button>
            ) : null}
            {can(P.patientsEdit, P.scheduleManage) ? (
              <Button onClick={() => setCreateOpen(true)} data-testid="new-patient">
                <Plus /> {t("patients.new")}
              </Button>
            ) : null}
          </>
        }
      />
      <Card className="mb-3 flex flex-wrap items-center gap-3 p-3">
        <div className="relative min-w-60 flex-1">
          <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input className="pl-8" placeholder={t("patients.searchPlaceholder")} value={q} onChange={(e) => setQ(e.target.value)} />
        </div>
        <NativeSelect className="w-44" value={source} onChange={(e) => setSource(e.target.value)} aria-label={t("patients.filters.source")}>
          <option value="">{t("patients.filters.source")}: {t("common.all").toLowerCase()}</option>
          {sources.data?.map((s) => (
            <option key={s.id} value={s.id}>
              {s.name}
            </option>
          ))}
        </NativeSelect>
        <Input className="w-36" placeholder={t("patients.filters.tag")} value={tag} onChange={(e) => setTag(e.target.value)} />
        <NativeSelect className="w-48" value={months} onChange={(e) => setMonths(e.target.value)} aria-label={t("patients.filters.notVisited")}>
          <option value="">{t("patients.filters.notVisited")}: —</option>
          {[3, 6, 12].map((m) => (
            <option key={m} value={m}>
              {t("patients.filters.notVisited").replace("N", String(m))}
            </option>
          ))}
        </NativeSelect>
        <label className="flex items-center gap-2 text-sm">
          <Checkbox checked={debtors} onCheckedChange={(v) => setDebtors(v === true)} />
          {t("patients.filters.debtors")}
        </label>
      </Card>

      <Card>
        {query.isLoading ? (
          <TableSkeleton />
        ) : rows.length === 0 ? (
          <EmptyState
            className="m-4"
            title={t("patients.empty")}
            description={t("patients.emptyHint")}
            action={can(P.patientsEdit) ? <Button onClick={() => setCreateOpen(true)}>{t("patients.new")}</Button> : null}
          />
        ) : (
          <Table>
            <THead>
              <TR>
                <TH>ФИО</TH>
                <TH>{t("patients.phone")}</TH>
                <TH>{t("patients.birthDate")}</TH>
                <TH>{t("patients.lastVisit")}</TH>
                <TH>{t("patients.nextAppointment")}</TH>
                <TH className="text-right">{t("patients.balance")}</TH>
              </TR>
            </THead>
            <TBody>
              {rows.map((p) => (
                <TR key={p.id} className="cursor-pointer" onClick={() => push(`patients/${p.id}`)}>
                  <TD>
                    <div className="flex items-center gap-1.5 font-medium">
                      {p.isVip ? <Star className="h-3.5 w-3.5 fill-warning text-warning" /> : null}
                      {p.fullName}
                    </div>
                    {p.tags.length ? (
                      <div className="mt-0.5 flex gap-1">
                        {p.tags.map((tg) => (
                          <Badge key={tg} variant="muted" className="text-[10px]">
                            {tg}
                          </Badge>
                        ))}
                      </div>
                    ) : null}
                  </TD>
                  <TD className="whitespace-nowrap">{formatPhone(p.phone)}</TD>
                  <TD>{formatDate(p.birthDate)}</TD>
                  <TD>{formatDate(p.lastVisitAt)}</TD>
                  <TD>{formatDate(p.nextAppointmentAt)}</TD>
                  <TD className={`tabular text-right ${p.balance < 0 ? "text-destructive" : p.balance > 0 ? "text-success" : ""}`}>
                    {p.balance === 0 ? "—" : formatMoney(p.balance)}
                  </TD>
                </TR>
              ))}
            </TBody>
          </Table>
        )}
        {query.hasNextPage ? (
          <div className="flex justify-center p-3">
            <Button variant="outline" loading={query.isFetchingNextPage} onClick={() => query.fetchNextPage()}>
              {t("patients.loadMore")}
            </Button>
          </div>
        ) : null}
      </Card>

      <PatientFormDialog open={createOpen} onOpenChange={setCreateOpen} onSaved={(p) => push(`patients/${p.id}`)} />
    </div>
  );
}
