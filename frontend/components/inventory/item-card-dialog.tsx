"use client";

import * as React from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Table, TBody, TD, TH, THead, TR } from "@/components/ui/table";
import { TableSkeleton } from "@/components/ui/skeleton";
import { Badge } from "@/components/ui/badge";
import { api, ApiError } from "@/lib/api-client";
import { useOrgHref } from "@/lib/auth";
import { formatDate, formatDateTime, formatMoney, formatNumber, isoDate } from "@/lib/format";
import { t } from "@/lib/i18n";
import type { Schemas } from "@/lib/types";
import { docTypeLabel, unitLabel } from "./shared";

/** Карточка товара: остатки по складам/партиям и последние движения. */
export function ItemCardDialog({ itemId, onOpenChange }: { itemId: string | null; onOpenChange: (open: boolean) => void }) {
  const { href } = useOrgHref();
  const card = useQuery({
    queryKey: ["stock", "card", itemId],
    queryFn: () => api<Schemas["ItemCardDto"]>(`/stock/items/${itemId}/card`),
    enabled: !!itemId,
  });
  const d = card.data;
  const unit = unitLabel(d?.item.baseUnit);
  const today = isoDate(new Date());

  return (
    <Dialog open={!!itemId} onOpenChange={onOpenChange}>
      <DialogContent wide className="max-w-4xl">
        <DialogHeader>
          <DialogTitle>{d ? d.item.name : t("inventory.card.title")}</DialogTitle>
          <DialogDescription>
            {d ? (
              <>
                {t("inventory.card.sku")}: {d.item.sku}
                {d.item.manufacturer ? ` · ${t("inventory.card.manufacturer")}: ${d.item.manufacturer}` : ""}
              </>
            ) : (
              t("common.loading")
            )}
          </DialogDescription>
        </DialogHeader>

        {card.isLoading ? (
          <TableSkeleton rows={5} />
        ) : card.isError ? (
          <p className="text-sm text-destructive">{card.error instanceof ApiError ? card.error.userMessage : t("errors.INTERNAL_ERROR")}</p>
        ) : d ? (
          <div className="space-y-4">
            <div className="grid grid-cols-2 gap-3">
              <div className="rounded-lg border p-3">
                <div className="text-xs text-muted-foreground">{t("inventory.card.totalQty")}</div>
                <div className="tabular text-lg font-semibold">
                  {formatNumber(d.totalQty)} {unit}
                </div>
              </div>
              <div className="rounded-lg border p-3">
                <div className="text-xs text-muted-foreground">{t("inventory.card.totalAmount")}</div>
                <div className="tabular text-lg font-semibold">{formatMoney(d.totalAmount)}</div>
              </div>
            </div>

            <section>
              <h3 className="mb-1.5 text-sm font-medium">{t("inventory.card.balances")}</h3>
              {d.balances.length === 0 ? (
                <p className="text-sm text-muted-foreground">{t("inventory.card.noBalances")}</p>
              ) : (
                <div className="rounded-lg border">
                  <Table>
                    <THead>
                      <TR>
                        <TH>{t("inventory.card.warehouse")}</TH>
                        <TH>{t("inventory.card.batch")}</TH>
                        <TH>{t("inventory.card.expiry")}</TH>
                        <TH className="text-right">{t("inventory.card.qty")}</TH>
                        <TH className="text-right">{t("inventory.card.unitCost")}</TH>
                      </TR>
                    </THead>
                    <TBody>
                      {d.balances.map((b, i) => (
                        <TR key={`${b.warehouseId}-${b.batchId ?? i}`}>
                          <TD>{b.warehouseName}</TD>
                          <TD>{b.batchNumber ?? b.serialNumber ?? <span className="text-muted-foreground">{t("inventory.card.noBatch")}</span>}</TD>
                          <TD className={b.expiresAt && b.expiresAt < today ? "text-destructive" : ""}>{formatDate(b.expiresAt)}</TD>
                          <TD className={`tabular text-right ${b.qty < 0 ? "text-destructive" : ""}`}>
                            {formatNumber(b.qty)} {unit}
                          </TD>
                          <TD className="tabular text-right">{formatMoney(Math.round(b.unitCost))}</TD>
                        </TR>
                      ))}
                    </TBody>
                  </Table>
                </div>
              )}
            </section>

            <section>
              <h3 className="mb-1.5 text-sm font-medium">{t("inventory.card.movements")}</h3>
              {d.movements.length === 0 ? (
                <p className="text-sm text-muted-foreground">{t("inventory.card.noMovements")}</p>
              ) : (
                <div className="max-h-80 overflow-y-auto rounded-lg border">
                  <Table>
                    <THead>
                      <TR>
                        <TH>{t("inventory.card.date")}</TH>
                        <TH>{t("inventory.card.document")}</TH>
                        <TH>{t("inventory.card.warehouse")}</TH>
                        <TH>{t("inventory.card.batch")}</TH>
                        <TH className="text-right">{t("inventory.card.qty")}</TH>
                        <TH className="text-right">{t("inventory.card.unitCost")}</TH>
                      </TR>
                    </THead>
                    <TBody>
                      {d.movements.map((m) => (
                        <TR key={m.id}>
                          <TD className="whitespace-nowrap">{formatDateTime(m.movedAt)}</TD>
                          <TD className="whitespace-nowrap">
                            <Badge variant="muted" className="mr-1.5">
                              {docTypeLabel(m.documentType)}
                            </Badge>
                            <Link className="text-primary hover:underline" href={href(`inventory/documents/${m.documentId}`)} onClick={() => onOpenChange(false)}>
                              {m.documentNumber}
                            </Link>
                          </TD>
                          <TD>{m.warehouseName}</TD>
                          <TD>{m.batchNumber ?? "—"}</TD>
                          <TD className={`tabular text-right font-medium ${m.qty < 0 ? "text-destructive" : "text-success"}`}>
                            {m.qty > 0 ? "+" : ""}
                            {formatNumber(m.qty)}
                          </TD>
                          <TD className="tabular text-right">{formatMoney(Math.round(m.unitCost))}</TD>
                        </TR>
                      ))}
                    </TBody>
                  </Table>
                </div>
              )}
            </section>
          </div>
        ) : null}
      </DialogContent>
    </Dialog>
  );
}
