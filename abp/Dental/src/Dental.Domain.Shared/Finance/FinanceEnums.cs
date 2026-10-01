namespace Dental.Finance;

public enum VisitStatus { Open, Closed, Cancelled }
public enum VisitApprovalState { None, PendingDiscount, PendingCorrection }
public enum CashShiftStatus { Open, Closed }
public enum PaymentMethod { Cash, Card, KaspiQr, Transfer, Insurance, Balance }
public enum PaymentType { Payment, Refund, Advance }
public enum CashOperationType { Collection, Deposit }
public enum ExpenseCategoryType { Rent, Utilities, Salary, Supplies, Lab, Marketing, Other }
