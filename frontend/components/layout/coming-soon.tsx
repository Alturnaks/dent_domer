import { Construction } from "lucide-react";
import { EmptyState, PageHeader } from "@/components/ui/empty-state";
import { t } from "@/lib/i18n";

export function ComingSoon({ titleKey }: { titleKey: string }) {
  return (
    <div>
      <PageHeader title={t(titleKey)} />
      <EmptyState icon={<Construction className="h-8 w-8" />} title={t("common.inDevelopment")} description={t("common.inDevelopmentHint")} />
    </div>
  );
}
