namespace Dental.Inventory;

/// <summary>Тип склада: центральный склад сети, склад филиала, склад кабинета.</summary>
public enum WarehouseType { Central = 0, Branch = 1, Cabinet = 2 }

/// <summary>Базовая единица учёта номенклатуры.</summary>
public enum BaseUnit { Pcs = 0, G = 1, Ml = 2, Pack = 3 }

public enum StockDocumentType { Receipt = 0, Transfer = 1, Writeoff = 2, Inventory = 3, ReturnToSupplier = 4, VisitConsumption = 5 }

public enum StockDocumentStatus { Draft = 0, PendingApproval = 1, Posted = 2, InTransit = 3, Received = 4, Cancelled = 5 }

public enum WriteoffReasonType { Expired = 0, Damaged = 1, Defect = 2, Lost = 3, Other = 4 }

/// <summary>Виды подтверждений склада (= <see cref="Dental.Approvals.ApprovalTypes"/>).</summary>
public static class InventoryApprovalTypes
{
    public const string Writeoff = Dental.Approvals.ApprovalTypes.Writeoff;
    public const string TransferShortage = Dental.Approvals.ApprovalTypes.TransferShortage;
}
