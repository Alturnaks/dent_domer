"use client";

import * as React from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { PageHeader } from "@/components/ui/empty-state";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { RequestsTab } from "@/components/purchasing/requests-tab";
import { DemandTab } from "@/components/purchasing/demand-tab";
import { OrdersTab } from "@/components/purchasing/orders-tab";
import { InvoicesTab } from "@/components/purchasing/invoices-tab";
import { useAuth } from "@/lib/auth";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";

type TabKey = "requests" | "demand" | "orders" | "invoices";

function Purchasing() {
  const { can } = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();

  const tabs: { key: TabKey; allowed: boolean }[] = [
    { key: "requests", allowed: can(P.purchaseRequest, P.purchaseOrder, P.purchaseApprove) },
    { key: "demand", allowed: can(P.purchaseOrder, P.purchaseApprove) },
    { key: "orders", allowed: can(P.purchaseOrder, P.purchaseApprove, P.inventoryReceive) },
    { key: "invoices", allowed: can(P.purchaseOrder, P.purchaseApprove, P.reportsFinance) },
  ];
  const allowed = tabs.filter((x) => x.allowed).map((x) => x.key);
  const requested = params.get("tab") as TabKey | null;
  const tab: TabKey = requested && allowed.includes(requested) ? requested : (allowed[0] ?? "requests");

  const setTab = (v: string) => {
    const q = new URLSearchParams(params.toString());
    q.set("tab", v);
    router.replace(`${pathname}?${q.toString()}`, { scroll: false });
  };

  return (
    <div>
      <PageHeader title={t("purchasing.title")} description={t("purchasing.description")} />
      <Tabs value={tab} onValueChange={setTab}>
        <TabsList>
          {allowed.map((k) => (
            <TabsTrigger key={k} value={k} data-testid={`tab-${k}`}>
              {t(`purchasing.tabs.${k}`)}
            </TabsTrigger>
          ))}
        </TabsList>
        {allowed.includes("requests") ? (
          <TabsContent value="requests">
            <RequestsTab />
          </TabsContent>
        ) : null}
        {allowed.includes("demand") ? (
          <TabsContent value="demand">
            <DemandTab />
          </TabsContent>
        ) : null}
        {allowed.includes("orders") ? (
          <TabsContent value="orders">
            <OrdersTab />
          </TabsContent>
        ) : null}
        {allowed.includes("invoices") ? (
          <TabsContent value="invoices">
            <InvoicesTab />
          </TabsContent>
        ) : null}
      </Tabs>
    </div>
  );
}

export default function PurchasingPage() {
  return (
    <React.Suspense fallback={<Skeleton className="h-64 w-full" />}>
      <Purchasing />
    </React.Suspense>
  );
}
