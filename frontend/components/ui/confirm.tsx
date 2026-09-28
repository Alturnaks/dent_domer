"use client";
import * as React from "react";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "./dialog";
import { Button } from "./button";
import { t } from "@/lib/i18n";

type ConfirmOptions = { title: string; description?: string; confirmText?: string; destructive?: boolean };
type Pending = ConfirmOptions & { resolve: (v: boolean) => void };

const ConfirmContext = React.createContext<(o: ConfirmOptions) => Promise<boolean>>(async () => false);

/** Подтверждение перед разрушительными действиями: const confirm = useConfirm(); if (await confirm({...})) … */
export function ConfirmProvider({ children }: { children: React.ReactNode }) {
  const [pending, setPending] = React.useState<Pending | null>(null);
  const confirm = React.useCallback((o: ConfirmOptions) => new Promise<boolean>((resolve) => setPending({ ...o, resolve })), []);
  const close = (v: boolean) => {
    pending?.resolve(v);
    setPending(null);
  };
  return (
    <ConfirmContext.Provider value={confirm}>
      {children}
      <Dialog open={pending !== null} onOpenChange={(o) => !o && close(false)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{pending?.title}</DialogTitle>
            {pending?.description ? <DialogDescription>{pending.description}</DialogDescription> : null}
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => close(false)}>
              {t("common.cancel")}
            </Button>
            <Button variant={pending?.destructive ? "destructive" : "default"} onClick={() => close(true)}>
              {pending?.confirmText ?? t("common.confirm")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </ConfirmContext.Provider>
  );
}

export function useConfirm() {
  return React.useContext(ConfirmContext);
}
