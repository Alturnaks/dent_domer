"use client";

import * as React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { zodResolver } from "@hookform/resolvers/zod";
import { Stethoscope } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field } from "@/components/ui/label";
import { Card, CardContent } from "@/components/ui/card";
import { useAuth } from "@/lib/auth";
import { ApiError } from "@/lib/api-client";
import { t } from "@/lib/i18n";

const schema = z.object({
  login: z.string().trim().min(1, t("common.required")),
  password: z.string().min(1, t("common.required")),
});
type FormValues = z.infer<typeof schema>;

export default function LoginPage() {
  const { login, status, me } = useAuth();
  const router = useRouter();
  const [error, setError] = React.useState<string | null>(null);
  const form = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { login: "", password: "" } });

  React.useEffect(() => {
    if (status === "authenticated" && me) router.replace(`/${me.organization.slug}/dashboard`);
  }, [status, me, router]);

  const onSubmit = form.handleSubmit(async (values) => {
    setError(null);
    try {
      const m = await login(values.login, values.password);
      router.replace(`/${m.organization.slug}/dashboard`);
    } catch (e) {
      setError(e instanceof ApiError ? e.userMessage : t("errors.INTERNAL_ERROR"));
    }
  });

  return (
    <Card className="shadow-lg">
      <CardContent className="space-y-5 p-6">
        <div className="flex flex-col items-center gap-2 text-center">
          <div className="flex h-11 w-11 items-center justify-center rounded-xl bg-primary text-primary-foreground">
            <Stethoscope className="h-6 w-6" />
          </div>
          <h1 className="text-lg font-semibold">{t("auth.title")}</h1>
          <p className="text-sm text-muted-foreground">{t("auth.subtitle")}</p>
        </div>
        <form className="space-y-4" onSubmit={onSubmit} noValidate data-testid="login-form">
          <Field label={t("auth.login")} error={form.formState.errors.login?.message}>
            <Input autoComplete="username" autoFocus {...form.register("login")} name="login" />
          </Field>
          <Field label={t("auth.password")} error={form.formState.errors.password?.message}>
            <Input type="password" autoComplete="current-password" {...form.register("password")} name="password" />
          </Field>
          {error ? (
            <p role="alert" className="rounded-md bg-destructive/10 px-3 py-2 text-sm text-destructive">
              {error}
            </p>
          ) : null}
          <Button type="submit" className="w-full" loading={form.formState.isSubmitting}>
            {t("auth.submit")}
          </Button>
        </form>
        <div className="flex items-center justify-between text-xs text-muted-foreground">
          <Link href="/forgot-password" className="hover:text-foreground hover:underline">
            {t("auth.forgot")}
          </Link>
          <span>{t("auth.demoHint")}</span>
        </div>
      </CardContent>
    </Card>
  );
}
