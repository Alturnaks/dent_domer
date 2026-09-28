// Права используются только для скрытия элементов UI; источник истины — бэкенд.

export const P = {
  orgSettings: "org.settings.manage",
  branchesManage: "branches.manage",
  staffManage: "staff.manage",
  rolesManage: "roles.manage",
  patientsView: "patients.view",
  patientsEdit: "patients.edit",
  patientsMerge: "patients.merge",
  scheduleViewAll: "schedule.view_all",
  scheduleViewOwn: "schedule.view_own",
  scheduleManage: "schedule.manage",
  doctorSchedulesManage: "doctor_schedules.manage",
  visitsComplete: "visits.complete",
  visitsEditOpen: "visits.edit_open",
  visitsEditClosed: "visits.edit_closed",
  visitsCancel: "visits.cancel",
  discountsApply: "discounts.apply",
  pricesManage: "prices.manage",
  techcardsManage: "techcards.manage",
  cashShift: "cash.shift.open_close",
  cashPayment: "cash.payment.create",
  cashRefund: "cash.payment.refund",
  cashExpense: "cash.expense.create",
  inventoryView: "inventory.view",
  inventoryReceive: "inventory.receive",
  transferCreate: "inventory.transfer.create",
  transferReceive: "inventory.transfer.receive",
  writeoff: "inventory.writeoff",
  inventoryCount: "inventory.count",
  inventoryCountApprove: "inventory.count.approve",
  itemsManage: "inventory.items.manage",
  purchaseRequest: "purchase.request.create",
  purchaseOrder: "purchase.order.create",
  purchaseApprove: "purchase.order.approve",
  payrollOwn: "payroll.view_own",
  payrollAll: "payroll.view_all",
  payrollManage: "payroll.manage",
  reportsBranch: "reports.branch",
  reportsNetwork: "reports.network",
  reportsFinance: "reports.finance",
  reportsPayroll: "reports.payroll",
  auditView: "audit.view",
} as const;

export type NavItem = {
  key: string;
  href: string;
  icon: string;
  /** Показать, если есть хотя бы одно из прав. Пусто — доступно всем. */
  anyOf: string[];
};

export const NAV: NavItem[] = [
  { key: "dashboard", href: "dashboard", icon: "LayoutDashboard", anyOf: [] },
  { key: "schedule", href: "schedule", icon: "CalendarDays", anyOf: [P.scheduleViewAll, P.scheduleViewOwn, P.scheduleManage] },
  { key: "doctorSchedules", href: "schedule/doctors", icon: "CalendarClock", anyOf: [P.doctorSchedulesManage] },
  { key: "patients", href: "patients", icon: "Users", anyOf: [P.patientsView] },
  { key: "cash", href: "cash", icon: "Wallet", anyOf: [P.cashShift, P.cashPayment, P.cashRefund, P.cashExpense] },
  { key: "inventory", href: "inventory", icon: "Package", anyOf: [P.inventoryView] },
  { key: "inventoryDocuments", href: "inventory/documents", icon: "FileStack", anyOf: [P.inventoryView] },
  { key: "items", href: "inventory/items", icon: "Boxes", anyOf: [P.itemsManage, P.inventoryView] },
  { key: "purchasing", href: "purchasing", icon: "ShoppingCart", anyOf: [P.purchaseRequest, P.purchaseOrder, P.purchaseApprove] },
  { key: "catalog", href: "catalog", icon: "ListChecks", anyOf: [P.pricesManage, P.techcardsManage] },
  { key: "staff", href: "staff", icon: "UserCog", anyOf: [P.staffManage, P.rolesManage] },
  { key: "payroll", href: "payroll", icon: "Banknote", anyOf: [P.payrollOwn, P.payrollAll, P.payrollManage] },
  { key: "reports", href: "reports", icon: "BarChart3", anyOf: [P.reportsBranch, P.reportsFinance, P.reportsNetwork, P.reportsPayroll, P.inventoryView] },
  { key: "approvals", href: "approvals", icon: "BadgeCheck", anyOf: [P.discountsApply, P.writeoff, P.cashRefund, P.visitsEditClosed, P.purchaseApprove, P.payrollManage] },
  { key: "audit", href: "audit", icon: "ShieldAlert", anyOf: [P.auditView] },
  { key: "settings", href: "settings", icon: "Settings", anyOf: [P.orgSettings, P.branchesManage] },
];

export function can(permissions: readonly string[] | undefined, ...anyOf: string[]): boolean {
  if (!permissions) return false;
  if (anyOf.length === 0) return true;
  return anyOf.some((p) => permissions.includes(p));
}
