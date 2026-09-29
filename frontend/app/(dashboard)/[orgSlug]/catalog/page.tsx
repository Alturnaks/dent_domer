"use client";

import { PageHeader } from "@/components/ui/empty-state";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { NoAccess } from "@/components/admin/common";
import { ServicesTab } from "@/components/admin/catalog-services";
import { PriceListsTab } from "@/components/admin/catalog-price-lists";
import { useAuth } from "@/lib/auth";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";

export default function CatalogPage() {
  const { can } = useAuth();
  if (!can(P.pricesManage, P.techcardsManage)) return <NoAccess />;
  const canPrices = can(P.pricesManage, P.reportsFinance);
  return (
    <div>
      <PageHeader title={t("catalog.title")} />
      <Tabs defaultValue="services">
        <TabsList>
          <TabsTrigger value="services">{t("catalog.tabs.services")}</TabsTrigger>
          {canPrices ? <TabsTrigger value="price-lists">{t("catalog.tabs.priceLists")}</TabsTrigger> : null}
        </TabsList>
        <TabsContent value="services">
          <ServicesTab />
        </TabsContent>
        {canPrices ? (
          <TabsContent value="price-lists">
            <PriceListsTab />
          </TabsContent>
        ) : null}
      </Tabs>
    </div>
  );
}
