"use client";

import * as React from "react";
import { useParams, usePathname, useRouter } from "next/navigation";
import { ShieldX } from "lucide-react";
import { EmptyState } from "@/components/ui/empty-state";
import { NAV, can } from "@/lib/permissions";
import { t } from "@/lib/i18n";
import { useAuth } from "@/lib/auth";
import { AppShell } from "@/components/layout/app-shell";
import { Skeleton } from "@/components/ui/skeleton";

export default function OrgLayout({ children }: { children: React.ReactNode }) {
  const { status, me, switchOrg } = useAuth();
  const params = useParams<{ orgSlug: string }>();
  const router = useRouter();
  const pathname = usePathname();

  React.useEffect(() => {
    if (status === "anonymous") router.replace("/login");
    if (status === "authenticated" && me && params.orgSlug !== me.organization.slug) {
      const target = me.organizations.find((o) => o.slug === params.orgSlug);
      if (target) void switchOrg(target.id);
      else router.replace(`/${me.organization.slug}/dashboard`);
    }
  }, [status, me, params.orgSlug, router, switchOrg]);

  if (status !== "authenticated" || !me || me.organization.slug !== params.orgSlug) {
    return (
      <div className="flex min-h-screen">
        <div className="hidden w-60 border-r p-4 lg:block">
          <Skeleton className="mb-6 h-8 w-40" />
          {Array.from({ length: 8 }).map((_, i) => (
            <Skeleton key={i} className="mb-2 h-7 w-full" />
          ))}
        </div>
        <div className="flex-1 p-5">
          <Skeleton className="mb-4 h-8 w-64" />
          <Skeleton className="h-64 w-full" />
        </div>
      </div>
    );
  }
  // Раздел из меню без нужных прав — показываем «нет доступа» вместо пустой страницы с ошибками API.
  const section = pathname.split("/").slice(2).join("/");
  const navItem = [...NAV].sort((a, b) => b.href.length - a.href.length).find((n) => section === n.href || section.startsWith(`${n.href}/`));
  const forbidden = navItem && !can(me.permissions, ...navItem.anyOf);
  return (
    <AppShell>
      {forbidden ? <EmptyState icon={<ShieldX className="h-8 w-8" />} title={t("errors.FORBIDDEN")} /> : children}
    </AppShell>
  );
}
