"use client";

import * as React from "react";
import { useRouter } from "next/navigation";
import { useAuth } from "@/lib/auth";

export default function Home() {
  const { status, me } = useAuth();
  const router = useRouter();
  React.useEffect(() => {
    if (status === "anonymous") router.replace("/login");
    else if (status === "authenticated" && me) router.replace(`/${me.organization.slug}/dashboard`);
  }, [status, me, router]);
  return <div className="flex min-h-screen items-center justify-center text-sm text-muted-foreground">Загрузка…</div>;
}
