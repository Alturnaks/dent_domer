namespace Dental;

public static class DentalDomainErrorCodes
{
    public const string BranchAccessDenied = "Dental:BranchAccessDenied";
    public const string BranchNameAlreadyExists = "Dental:BranchNameAlreadyExists";
    public const string EmployeeAlreadyExists = "Dental:EmployeeAlreadyExists";
    public const string EmployeeAlreadyFired = "Dental:EmployeeAlreadyFired";
    public const string CannotFireYourself = "Dental:CannotFireYourself";
    public const string EmployeeBranchesRequired = "Dental:EmployeeBranchesRequired";
    public const string RoleLimitExceeded = "Dental:RoleLimitExceeded";
    public const string InvalidWorkingHours = "Dental:InvalidWorkingHours";

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
