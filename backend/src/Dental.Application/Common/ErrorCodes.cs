namespace Dental.Application.Common;

/// <summary>Коды ошибок API. На фронте переводятся через i18n (errors.&lt;CODE&gt;).</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string NotFound = "NOT_FOUND";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string BranchForbidden = "BRANCH_FORBIDDEN";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string InternalError = "INTERNAL_ERROR";
    public const string RateLimited = "RATE_LIMITED";

    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string AccountLocked = "ACCOUNT_LOCKED";
    public const string AccountDisabled = "ACCOUNT_DISABLED";
    public const string NoMembership = "NO_MEMBERSHIP";
    public const string InvalidRefreshToken = "INVALID_REFRESH_TOKEN";
    public const string CsrfFailed = "CSRF_FAILED";
    public const string InvalidResetToken = "INVALID_RESET_TOKEN";

    public const string IdempotencyKeyRequired = "IDEMPOTENCY_KEY_REQUIRED";
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";

    public const string SlugTaken = "SLUG_TAKEN";
    public const string EmailTaken = "EMAIL_TAKEN";
    public const string PhoneTaken = "PHONE_TAKEN";
    public const string RoleInUse = "ROLE_IN_USE";
    public const string PresetRoleLocked = "PRESET_ROLE_LOCKED";
    public const string UnknownPermission = "UNKNOWN_PERMISSION";

    public const string PatientDuplicate = "PATIENT_DUPLICATE";
    public const string PatientMergeInvalid = "PATIENT_MERGE_INVALID";
    public const string IinTaken = "IIN_TAKEN";

    public const string DoctorNotWorking = "DOCTOR_NOT_WORKING";
    public const string SlotConflict = "SLOT_CONFLICT";
    public const string ScheduleHasAppointments = "SCHEDULE_HAS_APPOINTMENTS";
    public const string InvalidStatusTransition = "INVALID_STATUS_TRANSITION";
    public const string CancelReasonRequired = "CANCEL_REASON_REQUIRED";
    public const string InvalidTimeRange = "INVALID_TIME_RANGE";

    public const string VisitNotOpen = "VISIT_NOT_OPEN";
    public const string VisitNotClosed = "VISIT_NOT_CLOSED";
    public const string VisitPendingApproval = "VISIT_PENDING_APPROVAL";
    public const string VisitAlreadyExists = "VISIT_ALREADY_EXISTS";
    public const string CommentRequired = "COMMENT_REQUIRED";
    public const string PriceNotFound = "PRICE_NOT_FOUND";
    public const string DiscountInvalid = "DISCOUNT_INVALID";
    public const string PayrollPeriodLocked = "PAYROLL_PERIOD_LOCKED";
    public const string PayrollPeriodOverlap = "PAYROLL_PERIOD_OVERLAP";

    public const string ShiftNotOpen = "SHIFT_NOT_OPEN";
    public const string ShiftAlreadyOpen = "SHIFT_ALREADY_OPEN";
    public const string InsufficientBalance = "INSUFFICIENT_BALANCE";
    public const string RefundExceedsPayment = "REFUND_EXCEEDS_PAYMENT";
    public const string AmountInvalid = "AMOUNT_INVALID";

    public const string StockInsufficient = "STOCK_INSUFFICIENT";
    public const string WarehouseLocked = "WAREHOUSE_LOCKED";
    public const string DocumentNotEditable = "DOCUMENT_NOT_EDITABLE";
    public const string DocumentInvalidState = "DOCUMENT_INVALID_STATE";
    public const string BatchRequired = "BATCH_REQUIRED";
    public const string ExpiryRequired = "EXPIRY_REQUIRED";
    public const string SerialRequired = "SERIAL_REQUIRED";
    public const string ReasonRequired = "REASON_REQUIRED";
    public const string SupplierRequired = "SUPPLIER_REQUIRED";
    public const string LinesRequired = "LINES_REQUIRED";
    public const string SameWarehouse = "SAME_WAREHOUSE";
    public const string ImportFailed = "IMPORT_FAILED";

    public const string ApprovalNotPending = "APPROVAL_NOT_PENDING";
    public const string ApprovalLimitExceeded = "APPROVAL_LIMIT_EXCEEDED";
    public const string ApprovalSelf = "APPROVAL_SELF";

    public const string ReportNotFound = "REPORT_NOT_FOUND";
    public const string ReportParamsInvalid = "REPORT_PARAMS_INVALID";
}
