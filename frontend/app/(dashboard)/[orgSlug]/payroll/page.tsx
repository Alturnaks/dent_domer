"use client";

import * as React from "react";
import { EmptyState, PageHeader } from "@/components/ui/empty-state";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { MyPayrollView } from "@/components/payroll/my-payroll";
import { PeriodsTab } from "@/components/payroll/periods-tab";
import { SchemesTab } from "@/components/payroll/schemes-tab";
import { useAuth } from "@/lib/auth";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";

export default function PayrollPage() {
  const { can, me } = useAuth();
  const isManager = can(P.payrollAll, P.payrollManage);
  // Владелец имеет все права; вкладка «Моя зарплата» нужна менеджеру, только если он сам врач.
  const hasOwn = can(P.payrollOwn) && (!isManager || me?.position === "Doctor");
  const tabs = [...(isManager ? (["periods", "schemes"] as const) : []), ...(hasOwn ? (["my"] as const) : [])];
  const [tab, setTab] = React.useState<string>(tabs[0] ?? "my");

  if (tabs.length === 0) {
    return (
      <div>
        <PageHeader title={t("payroll.title")} />
        <EmptyState title={t("errors.FORBIDDEN")} />
      </div>
    );
  }

  // Врач без прав управления — сразу «Моя зарплата» без вкладок.
  if (!isManager) {
    return (
      <div>
        <PageHeader title={t("payroll.my.title")} />
        <MyPayrollView />
      </div>
    );
  }

  return (
    <div>
      <PageHeader title={t("payroll.title")} description={t("payroll.description")} />
      <Tabs value={tab} onValueChange={setTab}>
        <TabsList>
          {tabs.map((k) => (
            <TabsTrigger key={k} value={k}>
              {t(`payroll.tabs.${k}`)}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="periods">
          <PeriodsTab />
        </TabsContent>
        <TabsContent value="schemes">
          <SchemesTab />
        </TabsContent>
        {hasOwn ? (
          <TabsContent value="my">
            <MyPayrollView />
          </TabsContent>
        ) : null}
      </Tabs>
    </div>
  );
}
