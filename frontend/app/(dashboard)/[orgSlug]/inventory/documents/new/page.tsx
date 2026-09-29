"use client";

import * as React from "react";
import { useSearchParams } from "next/navigation";
import { EmptyState } from "@/components/ui/empty-state";
import { Skeleton } from "@/components/ui/skeleton";
import { DocumentEditor } from "@/components/inventory/document-editor";
import type { DocType } from "@/components/inventory/shared";
import { t } from "@/lib/i18n";

const CREATABLE: DocType[] = ["Receipt", "Writeoff", "Transfer", "Inventory"];

function NewDocument() {
  const type = useSearchParams().get("type") as DocType | null;
  if (!type || !CREATABLE.includes(type)) return <EmptyState title={t("common.notFound")} />;
  return <DocumentEditor key={type} newType={type} />;
}

export default function NewStockDocumentPage() {
  return (
    <React.Suspense fallback={<Skeleton className="h-64 w-full" />}>
      <NewDocument />
    </React.Suspense>
  );
}
