namespace Dental;

public static class DentalDomainErrorCodes
{
    public const string AppointmentSlotConflict = "Dental:AppointmentSlotConflict";
    public const string AppointmentDoctorNotWorking = "Dental:AppointmentDoctorNotWorking";
    public const string AppointmentInvalidStatus = "Dental:AppointmentInvalidStatus";
    public const string AppointmentReasonRequired = "Dental:AppointmentReasonRequired";
    public const string ScheduleHasAppointments = "Dental:ScheduleHasAppointments";
    public const string ScheduleInvalidRange = "Dental:ScheduleInvalidRange";
    public const string ScheduleDoctorInvalid = "Dental:ScheduleDoctorInvalid";
    public const string ScheduleChairInvalid = "Dental:ScheduleChairInvalid";
    public const string ScheduleOverlappingShifts = "Dental:ScheduleOverlappingShifts";
    public const string BranchAccessDenied = "Dental:BranchAccessDenied";
    public const string BranchNameAlreadyExists = "Dental:BranchNameAlreadyExists";
    public const string EmployeeAlreadyExists = "Dental:EmployeeAlreadyExists";
    public const string EmployeeAlreadyFired = "Dental:EmployeeAlreadyFired";
    public const string CannotFireYourself = "Dental:CannotFireYourself";
    public const string EmployeeBranchesRequired = "Dental:EmployeeBranchesRequired";
    public const string RoleLimitExceeded = "Dental:RoleLimitExceeded";
    public const string InvalidWorkingHours = "Dental:InvalidWorkingHours";

    // Пациенты
    public const string PatientDuplicate = "Dental:PatientDuplicate";
    public const string IinTaken = "Dental:IinTaken";
    public const string InvalidIin = "Dental:InvalidIin";
    public const string InvalidPhone = "Dental:InvalidPhone";
    public const string InvalidBirthDate = "Dental:InvalidBirthDate";
    public const string PatientMergeInvalid = "Dental:PatientMergeInvalid";

    // Каталог
    public const string CategoryParentInvalid = "Dental:CategoryParentInvalid";
    public const string ServiceCodeAlreadyExists = "Dental:ServiceCodeAlreadyExists";
    public const string PriceInvalid = "Dental:PriceInvalid";
    public const string TechCardItemInvalid = "Dental:TechCardItemInvalid";

    // Подтверждения
    public const string ApprovalNotPending = "Dental:ApprovalNotPending";
    public const string ApprovalOwnRequest = "Dental:ApprovalOwnRequest";
    public const string ApprovalLimitExceeded = "Dental:ApprovalLimitExceeded";

    // Склад
    public const string StockInsufficient = "Dental:StockInsufficient";
    public const string StockDocumentNotEditable = "Dental:StockDocumentNotEditable";
    public const string StockDocumentInvalidState = "Dental:StockDocumentInvalidState";
    public const string StockLinesRequired = "Dental:StockLinesRequired";
    public const string StockSupplierRequired = "Dental:StockSupplierRequired";
    public const string StockReasonRequired = "Dental:StockReasonRequired";
    public const string StockSerialRequired = "Dental:StockSerialRequired";
    public const string StockBatchRequired = "Dental:StockBatchRequired";
    public const string StockExpiryRequired = "Dental:StockExpiryRequired";
    public const string StockWarehouseRequired = "Dental:StockWarehouseRequired";
    public const string StockSameWarehouse = "Dental:StockSameWarehouse";
    public const string StockWarehouseLocked = "Dental:StockWarehouseLocked";
    public const string StockCommentRequired = "Dental:StockCommentRequired";
    public const string StockQtyMustBePositive = "Dental:StockQtyMustBePositive";
    public const string StockInvalidActualQty = "Dental:StockInvalidActualQty";
    public const string StockVisitConsumptionManual = "Dental:StockVisitConsumptionManual";
    public const string StockNoBranchWarehouse = "Dental:StockNoBranchWarehouse";
    public const string ItemUnitNotFound = "Dental:ItemUnitNotFound";
    public const string ItemSerialRequiresPcs = "Dental:ItemSerialRequiresPcs";
    public const string ItemBaseUnitLocked = "Dental:ItemBaseUnitLocked";
    public const string ItemSkuAlreadyExists = "Dental:ItemSkuAlreadyExists";
    public const string StockLevelInvalid = "Dental:StockLevelInvalid";
    public const string WarehouseBranchRequired = "Dental:WarehouseBranchRequired";
    public const string WarehouseNotEmpty = "Dental:WarehouseNotEmpty";
    public const string CategoryCycle = "Dental:CategoryCycle";
    public const string SupplierBinInvalid = "Dental:SupplierBinInvalid";
}
