namespace Dental.Inventory;

public static class InventoryConsts
{
    public const int MaxNameLength = 300;
    public const int MaxWarehouseNameLength = 200;
    public const int MaxCategoryNameLength = 200;
    public const int MaxSkuLength = 60;
    public const int MaxManufacturerLength = 200;
    public const int MaxBarcodeLength = 64;
    public const int MaxUnitNameLength = 40;
    public const int MaxBatchNumberLength = 100;
    public const int MaxSerialNumberLength = 100;
    public const int MaxDocumentNumberLength = 32;
    public const int MaxInvoiceNumberLength = 64;
    public const int MaxCommentLength = 2000;
    public const int MaxBinLength = 12;
    public const int MaxPhoneLength = 32;
    public const int MaxEmailLength = 256;
    public const int MaxContactLength = 200;
    public const int MaxNotesLength = 2000;
    public const int MaxCounterKeyLength = 64;

    public const int QtyPrecision = 18;
    public const int QtyScale = 4;

    /// <summary>Порог «истекает» по умолчанию, дней.</summary>
    public const int DefaultExpiringDays = 30;
}
