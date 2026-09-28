"use client";

import * as React from "react";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Bell, Building2, ChevronDown, LogOut, Menu, Search, Stethoscope, X } from "lucide-react";
import { useAuth } from "@/lib/auth";
import { NAV, can } from "@/lib/permissions";
import { t } from "@/lib/i18n";
import { cn } from "@/lib/utils";
import { api } from "@/lib/api-client";
import { formatDateTime } from "@/lib/format";
import { NavIcons } from "./icons";
import { Button } from "@/components/ui/button";
import { NativeSelect } from "@/components/ui/input";
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { PatientSearchDialog } from "./patient-search";

type NotificationItem = { id: string; type: string; title: string; body?: string | null; entityType?: string | null; entityId?: string | null; readAt?: string | null; createdAt: string };

export function AppShell({ children }: { children: React.ReactNode }) {
  const { me } = useAuth();
  const [mobileOpen, setMobileOpen] = React.useState(false);
  const [searchOpen, setSearchOpen] = React.useState(false);
  const pathname = usePathname();

  React.useEffect(() => setMobileOpen(false), [pathname]);
  React.useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setSearchOpen(true);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  if (!me) return null;

  return (
    <div className="flex min-h-screen">
      <aside className="sticky top-0 hidden h-screen w-60 shrink-0 border-r bg-sidebar lg:block">
        <Sidebar />
      </aside>
      {mobileOpen ? (
        <div className="fixed inset-0 z-40 lg:hidden">
          <div className="absolute inset-0 bg-black/40" onClick={() => setMobileOpen(false)} />
          <aside className="absolute left-0 top-0 h-full w-64 border-r bg-sidebar shadow-xl">
            <button className="absolute right-2 top-3 p-1" onClick={() => setMobileOpen(false)} aria-label={t("common.close")}>
              <X className="h-5 w-5" />
            </button>
            <Sidebar />
          </aside>
        </div>
      ) : null}
      <div className="flex min-w-0 flex-1 flex-col">
        <Header onMenu={() => setMobileOpen(true)} onSearch={() => setSearchOpen(true)} />
        <main className="flex-1 p-3 sm:p-5">{children}</main>
      </div>
      <PatientSearchDialog open={searchOpen} onOpenChange={setSearchOpen} />
    </div>
  );
}

function Sidebar() {
  const { me, permissions } = useAuth();
  const pathname = usePathname();
  const slug = me!.organization.slug;
  const items = NAV.filter((n) => can(permissions, ...n.anyOf));
  return (
    <div className="flex h-full flex-col">
      <div className="flex h-14 items-center gap-2 border-b px-4">
        <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary text-primary-foreground">
          <Stethoscope className="h-4 w-4" />
        </div>
        <div className="min-w-0">
          <div className="truncate text-sm font-semibold">{me!.organization.name}</div>
          <div className="truncate text-xs text-muted-foreground">{t("app.tagline")}</div>
        </div>
      </div>
      <nav className="flex-1 space-y-0.5 overflow-y-auto p-2" data-testid="sidebar-nav">
        {items.map((item) => {
          const href = `/${slug}/${item.href}`;
          const active = pathname === href || (item.href !== "dashboard" && pathname.startsWith(href + "/") && !items.some((o) => o.href !== item.href && o.href.startsWith(item.href + "/") && pathname.startsWith(`/${slug}/${o.href}`)));
          const Icon = NavIcons[item.icon];
          return (
            <Link
              key={item.key}
              href={href}
              data-nav={item.key}
              className={cn(
                "flex items-center gap-2.5 rounded-md px-3 py-2 text-sm transition-colors",
                active ? "bg-primary/10 font-medium text-primary" : "text-foreground/80 hover:bg-muted",
              )}
            >
              {Icon ? <Icon className="h-4 w-4 shrink-0" /> : null}
              <span className="truncate">{t(`nav.${item.key}`)}</span>
            </Link>
          );
        })}
      </nav>
      <div className="border-t p-3 text-xs text-muted-foreground">
        <div className="truncate font-medium text-foreground">{me!.user.fullName}</div>
        <div className="truncate">{me!.role.name}</div>
      </div>
    </div>
  );
}

function Header({ onMenu, onSearch }: { onMenu: () => void; onSearch: () => void }) {
  const { me, switchOrg, logout, branchId, setBranchId } = useAuth();
  const router = useRouter();
  if (!me) return null;
  const branchValue = branchId ?? "all";
  return (
    <header className="sticky top-0 z-30 flex h-14 items-center gap-2 border-b bg-card/95 px-3 backdrop-blur sm:px-5">
      <Button variant="ghost" size="icon" className="lg:hidden" onClick={onMenu} aria-label={t("header.menu")}>
        <Menu className="h-5 w-5" />
      </Button>

      {me.organizations.length > 1 ? (
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" size="sm" className="max-w-48" data-testid="org-switcher">
              <Building2 className="h-4 w-4" />
              <span className="truncate">{me.organization.name}</span>
              <ChevronDown className="h-3 w-3 opacity-60" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="start">
            <DropdownMenuLabel>{t("header.organization")}</DropdownMenuLabel>
            {me.organizations.map((o) => (
              <DropdownMenuItem
                key={o.id}
                onSelect={async () => {
                  if (o.id === me.organization.id) return;
                  const m = await switchOrg(o.id);
                  router.push(`/${m.organization.slug}/dashboard`);
                }}
              >
                {o.name}
              </DropdownMenuItem>
            ))}
          </DropdownMenuContent>
        </DropdownMenu>
      ) : null}

      {me.branches.length > 0 ? (
        <NativeSelect
          aria-label={t("header.branch")}
          data-testid="branch-switcher"
          className="h-8 w-auto max-w-56"
          value={branchValue}
          onChange={(e) => setBranchId(e.target.value === "all" ? null : e.target.value)}
        >
          {me.allBranches || me.branches.length > 1 ? <option value="all">{t("common.allBranches")}</option> : null}
          {me.branches.map((b) => (
            <option key={b.id} value={b.id}>
              {b.name}
            </option>
          ))}
        </NativeSelect>
      ) : null}

      <div className="flex-1" />

      <button
        onClick={onSearch}
        className="hidden h-8 w-64 items-center gap-2 rounded-md border bg-muted/40 px-3 text-sm text-muted-foreground hover:bg-muted md:flex"
      >
        <Search className="h-4 w-4" />
        <span className="flex-1 text-left">{t("header.searchPatient")}</span>
        <kbd className="rounded border bg-card px-1.5 text-[10px]">{t("header.searchHint")}</kbd>
      </button>
      <Button variant="ghost" size="icon" className="md:hidden" onClick={onSearch} aria-label={t("common.search")}>
        <Search className="h-5 w-5" />
      </Button>

      <NotificationsBell />

      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="ghost" size="sm" data-testid="user-menu">
            <span className="flex h-7 w-7 items-center justify-center rounded-full bg-primary/15 text-xs font-semibold text-primary">
              {me.user.fullName.split(" ").slice(0, 2).map((p) => p[0]).join("")}
            </span>
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuLabel>
            <div className="text-sm text-foreground">{me.user.fullName}</div>
            <div className="font-normal">{me.user.email}</div>
          </DropdownMenuLabel>
          <DropdownMenuSeparator />
          <DropdownMenuItem
            onSelect={async () => {
              await logout();
              router.replace("/login");
            }}
          >
            <LogOut /> {t("header.logout")}
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </header>
  );
}

function NotificationsBell() {
  const { me } = useAuth();
  const qc = useQueryClient();
  const { data } = useQuery({
    queryKey: ["notifications"],
    queryFn: () => api<{ items: NotificationItem[]; unread: number }>("/notifications", { query: { limit: 20 } }),
    refetchInterval: 60_000,
    retry: false,
  });
  const readAll = useMutation({
    mutationFn: () => api("/notifications/read-all", { method: "POST" }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["notifications"] }),
  });
  const unread = data?.unread ?? me?.unreadNotifications ?? 0;
  const pending = me?.pendingApprovals ?? 0;
  const badge = unread + pending;
  const slug = me?.organization.slug;
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" className="relative" aria-label={t("header.notifications")} data-testid="notifications">
          <Bell className="h-5 w-5" />
          {badge > 0 ? (
            <span className="absolute right-1 top-1 flex h-4 min-w-4 items-center justify-center rounded-full bg-destructive px-1 text-[10px] font-semibold text-white">
              {badge > 99 ? "99+" : badge}
            </span>
          ) : null}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-80">
        <div className="flex items-center justify-between px-2 py-1">
          <span className="text-sm font-semibold">{t("header.notifications")}</span>
          {unread > 0 ? (
            <button className="text-xs text-primary hover:underline" onClick={() => readAll.mutate()}>
              {t("header.markAllRead")}
            </button>
          ) : null}
        </div>
        {pending > 0 ? (
          <DropdownMenuItem asChild>
            <Link href={`/${slug}/approvals`} className="font-medium text-primary">
              {t("header.approvalsPending", { count: pending })}
            </Link>
          </DropdownMenuItem>
        ) : null}
        <DropdownMenuSeparator />
        <div className="max-h-80 overflow-y-auto">
          {(data?.items ?? []).length === 0 ? (
            <p className="px-2 py-4 text-center text-sm text-muted-foreground">{t("header.noNotifications")}</p>
          ) : (
            data!.items.map((n) => (
              <div key={n.id} className={cn("rounded-sm px-2 py-1.5 text-sm", !n.readAt && "bg-primary/5")}>
                <div className="font-medium">{n.title}</div>
                {n.body ? <div className="text-xs text-muted-foreground">{n.body}</div> : null}
                <div className="text-[10px] text-muted-foreground">{formatDateTime(n.createdAt)}</div>
              </div>
            ))
          )}
        </div>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
