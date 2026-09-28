"use client";

import * as React from "react";
import { useRouter } from "next/navigation";
import { useQueryClient } from "@tanstack/react-query";
import { api, ApiError, getAccessToken, refreshAccessToken, setAccessToken } from "@/lib/api-client";
import type { Me } from "@/lib/types";

type Status = "loading" | "authenticated" | "anonymous";

type AuthContextValue = {
  status: Status;
  me: Me | null;
  permissions: string[];
  login: (login: string, password: string) => Promise<Me>;
  logout: () => Promise<void>;
  switchOrg: (organizationId: string) => Promise<Me>;
  reloadMe: () => Promise<Me | null>;
  can: (...anyOf: string[]) => boolean;
  branchId: string | null;
  setBranchId: (id: string | null) => void;
};

const AuthContext = React.createContext<AuthContextValue | null>(null);

const branchKey = (orgId: string) => `dental.branch.${orgId}`;

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [status, setStatus] = React.useState<Status>("loading");
  const [me, setMe] = React.useState<Me | null>(null);
  const [branchId, setBranchIdState] = React.useState<string | null>(null);
  const queryClient = useQueryClient();

  const applyMe = React.useCallback((m: Me) => {
    setMe(m);
    setStatus("authenticated");
    let stored: string | null = null;
    try {
      stored = localStorage.getItem(branchKey(m.organization.id));
    } catch {
      /* private mode */
    }
    const valid = m.branches.map((b) => b.id);
    if (stored === "all" && m.allBranches) setBranchIdState(null);
    else if (stored && valid.includes(stored)) setBranchIdState(stored);
    else setBranchIdState(m.allBranches || valid.length === 0 ? null : valid[0]);
  }, []);

  const reloadMe = React.useCallback(async () => {
    try {
      const m = await api<Me>("/auth/me");
      applyMe(m);
      return m;
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        setAccessToken(null);
        setMe(null);
        setStatus("anonymous");
        return null;
      }
      throw e;
    }
  }, [applyMe]);

  React.useEffect(() => {
    (async () => {
      if (!getAccessToken()) {
        const ok = await refreshAccessToken();
        if (!ok) {
          setStatus("anonymous");
          return;
        }
      }
      await reloadMe().catch(() => setStatus("anonymous"));
    })();
  }, [reloadMe]);

  const login = React.useCallback(
    async (loginValue: string, password: string) => {
      const res = await api<{ accessToken: string }>("/auth/login", { method: "POST", body: { login: loginValue, password } });
      setAccessToken(res.accessToken);
      queryClient.clear();
      const m = await api<Me>("/auth/me");
      applyMe(m);
      return m;
    },
    [applyMe, queryClient],
  );

  const logout = React.useCallback(async () => {
    try {
      await api("/auth/logout", { method: "POST" });
    } finally {
      setAccessToken(null);
      setMe(null);
      setStatus("anonymous");
      queryClient.clear();
    }
  }, [queryClient]);

  const switchOrg = React.useCallback(
    async (organizationId: string) => {
      const res = await api<{ accessToken: string }>("/auth/switch-org", { method: "POST", body: { organizationId } });
      setAccessToken(res.accessToken);
      queryClient.clear();
      const m = await api<Me>("/auth/me");
      applyMe(m);
      return m;
    },
    [applyMe, queryClient],
  );

  const setBranchId = React.useCallback(
    (id: string | null) => {
      setBranchIdState(id);
      if (me) {
        try {
          localStorage.setItem(branchKey(me.organization.id), id ?? "all");
        } catch {
          /* ignore */
        }
      }
    },
    [me],
  );

  const permissions = React.useMemo(() => me?.permissions ?? [], [me]);
  const can = React.useCallback((...anyOf: string[]) => anyOf.length === 0 || anyOf.some((p) => permissions.includes(p)), [permissions]);

  const value = React.useMemo<AuthContextValue>(
    () => ({ status, me, permissions, login, logout, switchOrg, reloadMe, can, branchId, setBranchId }),
    [status, me, permissions, login, logout, switchOrg, reloadMe, can, branchId, setBranchId],
  );
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = React.useContext(AuthContext);
  if (!ctx) throw new Error("useAuth outside AuthProvider");
  return ctx;
}

/** Текущий филиал для запросов: выбранный или единственный доступный. null = все филиалы. */
export function useBranch() {
  const { branchId, me } = useAuth();
  const branches = me?.branches ?? [];
  const effective = branchId ?? (me?.allBranches ? null : branches[0]?.id ?? null);
  return { branchId: effective, branches, timezone: me?.organization.timezone ?? "Asia/Almaty" };
}

export function useOrgHref() {
  const { me } = useAuth();
  const router = useRouter();
  const href = React.useCallback((path: string) => `/${me?.organization.slug ?? "_"}/${path.replace(/^\//, "")}`, [me]);
  return { href, push: (path: string) => router.push(href(path)) };
}
