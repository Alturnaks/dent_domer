"use client";

import * as React from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Card, CardContent } from "@/components/ui/card";
import { api, ApiError } from "@/lib/api-client";
import { t } from "@/lib/i18n";

function ResetForm() {
  const params = useSearchParams();
  const token = params.get("token") ?? "";
  const [password, setPassword] = React.useState("");
  const [done, setDone] = React.useState(false);
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api("/auth/password/reset", { method: "POST", body: { token, newPassword: password } });
      setDone(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.userMessage : t("errors.INTERNAL_ERROR"));
    } finally {
      setBusy(false);
    }
  };

  return done ? (
    <p className="rounded-md bg-success/10 px-3 py-2 text-sm">{t("auth.resetDone")}</p>
  ) : (
    <form onSubmit={submit} className="space-y-4">
      <Field label={t("auth.newPassword")} hint="Минимум 8 символов">
        <Input type="password" minLength={8} value={password} onChange={(e) => setPassword(e.target.value)} required autoFocus />
      </Field>
      {error ? <p className="text-sm text-destructive">{error}</p> : null}
      <Button type="submit" className="w-full" loading={busy}>
        {t("auth.resetSubmit")}
      </Button>
    </form>
  );
}

export default function ResetPasswordPage() {
  return (
    <Card className="shadow-lg">
      <CardContent className="space-y-4 p-6">
        <h1 className="text-lg font-semibold">{t("auth.resetTitle")}</h1>
        <React.Suspense fallback={null}>
          <ResetForm />
        </React.Suspense>
        <Link href="/login" className="block text-center text-sm text-primary hover:underline">
          {t("auth.backToLogin")}
        </Link>
      </CardContent>
    </Card>
  );
}
