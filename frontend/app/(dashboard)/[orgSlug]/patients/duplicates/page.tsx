"use client";

import * as React from "react";
import Link from "next/link";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { ArrowLeft } from "lucide-react";
import { PageHeader, EmptyState } from "@/components/ui/empty-state";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { TableSkeleton } from "@/components/ui/skeleton";
import { useConfirm } from "@/components/ui/confirm";
import { api, ApiError } from "@/lib/api-client";
import { useOrgHref } from "@/lib/auth";
import { formatDate, formatPhone } from "@/lib/format";
import { t } from "@/lib/i18n";
import type { DuplicateCandidate, DuplicatePair } from "@/lib/types";

function Side({ c, onKeep, busy }: { c: DuplicateCandidate; onKeep: () => void; busy: boolean }) {
  const { href } = useOrgHref();
  return (
    <div className="flex-1 rounded-lg border p-3 text-sm">
      <Link href={href(`patients/${c.id}`)} className="font-medium text-primary hover:underline">
        {c.fullName}
      </Link>
      <div className="text-muted-foreground">{formatPhone(c.phone)}</div>
      <div className="text-muted-foreground">
        {formatDate(c.birthDate)} {c.iin ? `· ${c.iin}` : ""}
      </div>
      <Button size="sm" variant="outline" className="mt-2" loading={busy} onClick={onKeep}>
        {t("patients.mergeInto")}
      </Button>
    </div>
  );
}

export default function DuplicatesPage() {
  const qc = useQueryClient();
  const confirm = useConfirm();
  const { href } = useOrgHref();
  const pairs = useQuery({ queryKey: ["patients", "duplicates"], queryFn: () => api<DuplicatePair[]>("/patients/duplicates") });
  const merge = useMutation({
    mutationFn: ({ main, dup }: { main: string; dup: string }) => api(`/patients/${main}/merge`, { method: "POST", body: { duplicateId: dup } }),
    onSuccess: () => {
      toast.success(t("patients.merged"));
      qc.invalidateQueries({ queryKey: ["patients"] });
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.userMessage : t("errors.INTERNAL_ERROR")),
  });

  const doMerge = async (main: string, dup: string) => {
    if (await confirm({ title: t("patients.merge"), description: t("patients.mergeConfirm"), destructive: true, confirmText: t("patients.merge") }))
      merge.mutate({ main, dup });
  };

  return (
    <div>
      <Link href={href("patients")} className="mb-2 inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
        <ArrowLeft className="h-4 w-4" /> {t("patients.title")}
      </Link>
      <PageHeader title={t("patients.duplicatesTitle")} />
      {pairs.isLoading ? (
        <TableSkeleton />
      ) : (pairs.data ?? []).length === 0 ? (
        <EmptyState title={t("patients.noDuplicates")} />
      ) : (
        <div className="space-y-3">
          {pairs.data!.map((pair) => (
            <Card key={`${pair.a.id}-${pair.b.id}`} className="p-3">
              <Badge variant="warning" className="mb-2">
                {t(`patients.reasons.${pair.reason}`)}
              </Badge>
              <div className="flex flex-col gap-3 sm:flex-row">
                <Side c={pair.a} busy={merge.isPending} onKeep={() => doMerge(pair.a.id, pair.b.id)} />
                <Side c={pair.b} busy={merge.isPending} onKeep={() => doMerge(pair.b.id, pair.a.id)} />
              </div>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
