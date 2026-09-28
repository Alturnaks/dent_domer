"use client";

import { PageHeader } from "@/components/ui/empty-state";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { useAuth } from "@/lib/auth";
import { t } from "@/lib/i18n";

export default function DashboardPage() {
  const { me } = useAuth();
  if (!me) return null;
  return (
    <div>
      <PageHeader title={t("dashboard.welcome", { name: me.user.fullName.split(" ")[1] ?? me.user.fullName })} description={t("dashboard.role", { role: me.role.name })} />
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        <Card>
          <CardHeader>
            <CardTitle>{t("header.organization")}</CardTitle>
          </CardHeader>
          <CardContent className="text-sm">
            <div className="font-medium">{me.organization.name}</div>
            <div className="text-muted-foreground">{me.organization.timezone} · {me.organization.currency}</div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>{t("header.branch")}</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-wrap gap-1.5">
            {me.branches.map((b) => (
              <Badge key={b.id} variant="secondary">
                {b.name}
              </Badge>
            ))}
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>Права ({me.permissions.length})</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-wrap gap-1">
            {me.permissions.map((p) => (
              <Badge key={p} variant="muted" className="font-mono text-[10px]">
                {p}
              </Badge>
            ))}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
