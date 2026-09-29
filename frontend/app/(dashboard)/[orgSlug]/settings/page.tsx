"use client";

import type * as React from "react";
import { PageHeader } from "@/components/ui/empty-state";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { NoAccess } from "@/components/admin/common";
import { OrgSettingsTab } from "@/components/admin/settings-org";
import { BranchesTab } from "@/components/admin/settings-branches";
import { CashRegistersTab, ReferencesTab, TemplatesTab } from "@/components/admin/settings-misc";
import { useAuth } from "@/lib/auth";
import { t } from "@/lib/i18n";
import { P } from "@/lib/permissions";

export default function SettingsPage() {
  const { can } = useAuth();
  const org = can(P.orgSettings);
  const branches = can(P.branchesManage);
  if (!org && !branches) return <NoAccess />;

  const tabs = [
    org && { key: "org", node: <OrgSettingsTab /> },
    branches && { key: "branches", node: <BranchesTab /> },
    org && { key: "references", node: <ReferencesTab /> },
    (org || branches) && { key: "registers", node: <CashRegistersTab /> },
    org && { key: "templates", node: <TemplatesTab /> },
  ].filter(Boolean) as { key: string; node: React.ReactNode }[];

  return (
    <div>
      <PageHeader title={t("settings.title")} />
      <Tabs defaultValue={tabs[0].key}>
        <TabsList>
          {tabs.map((x) => (
            <TabsTrigger key={x.key} value={x.key}>
              {t(`settings.tabs.${x.key}`)}
            </TabsTrigger>
          ))}
        </TabsList>
        {tabs.map((x) => (
          <TabsContent key={x.key} value={x.key}>
            {x.node}
          </TabsContent>
        ))}
      </Tabs>
    </div>
  );
}
