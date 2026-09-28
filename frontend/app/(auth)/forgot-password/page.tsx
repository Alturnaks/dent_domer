"use client";

import * as React from "react";
import Link from "next/link";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Card, CardContent } from "@/components/ui/card";
import { api, ApiError } from "@/lib/api-client";
import { t } from "@/lib/i18n";

export default function ForgotPasswordPage() {
  const [login, setLogin] = React.useState("");
  const [sent, setSent] = React.useState(false);
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api("/auth/password/forgot", { method: "POST", body: { login } });
      setSent(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.userMessage : t("errors.INTERNAL_ERROR"));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card className="shadow-lg">
      <CardContent className="space-y-4 p-6">
        <h1 className="text-lg font-semibold">{t("auth.forgotTitle")}</h1>
        <p className="text-sm text-muted-foreground">{t("auth.forgotSubtitle")}</p>
        {sent ? (
          <p className="rounded-md bg-success/10 px-3 py-2 text-sm">{t("auth.forgotSent")}</p>
        ) : (
          <form onSubmit={submit} className="space-y-4">
            <Field label={t("auth.login")}>
              <Input value={login} onChange={(e) => setLogin(e.target.value)} required autoFocus />
            </Field>
            {error ? <p className="text-sm text-destructive">{error}</p> : null}
            <Button type="submit" className="w-full" loading={busy}>
              {t("auth.forgotSubmit")}
            </Button>
          </form>
        )}
        <Link href="/login" className="block text-center text-sm text-primary hover:underline">
          {t("auth.backToLogin")}
        </Link>
      </CardContent>
    </Card>
  );
}
