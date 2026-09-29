using Dental.Domain.Purchasing;
using Shouldly;
using Xunit;

namespace Dental.Domain.Tests;

public class ReplenishmentTests
{
    [Theory]
    [InlineData(3, 10, 50, 0, 0, 47)]   // ниже минимума → до оптимума
    [InlineData(3, 10, 50, 20, 0, 27)]  // минус «в пути»
    [InlineData(3, 10, 50, 20, 30, 0)]  // уже заказано больше потребности → 0
    [InlineData(10, 10, 50, 0, 0, 0)]   // остаток = минимум → не заказываем
    [InlineData(0, 5, 20, 0, 5, 15)]
    public void SuggestedQty(decimal qty, decimal min, decimal optimal, decimal inTransit, decimal ordered, decimal expected) =>
        Replenishment.SuggestedQty(qty, min, optimal, inTransit, ordered).ShouldBe(expected);
}

public class PurchaseOrderStatusTests
{
    private static PurchaseOrder Sent(params (decimal Qty, decimal Received)[] lines)
    {
        var o = new PurchaseOrder { Status = PurchaseOrderStatus.Sent };
        foreach (var (q, r) in lines) o.Lines.Add(new PurchaseOrderLine { Qty = q, ReceivedQty = r, UnitPrice = 100 });
        return o;
    }

    [Fact]
    public void PartialReceipt_GivesPartiallyReceived()
    {
        var o = Sent((50, 20), (20, 20));
        o.RecalculateStatus();
        o.Status.ShouldBe(PurchaseOrderStatus.PartiallyReceived);
    }

    [Fact]
    public void FullReceipt_GivesReceived()
    {
        var o = Sent((50, 50), (20, 25));
        o.RecalculateStatus();
        o.Status.ShouldBe(PurchaseOrderStatus.Received);
    }

    [Fact]
    public void StornoOfAllReceipts_ReturnsToSent()
    {
        var o = Sent((50, 0));
        o.Status = PurchaseOrderStatus.PartiallyReceived;
        o.RecalculateStatus();
        o.Status.ShouldBe(PurchaseOrderStatus.Sent);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.PendingApproval)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    public void NotSentOrders_KeepStatus(PurchaseOrderStatus status)
    {
        var o = Sent((10, 10));
        o.Status = status;
        o.RecalculateStatus();
        o.Status.ShouldBe(status);
    }
}

public class SupplierInvoiceTests
{
    [Theory]
    [InlineData(1000, 0, 0, SupplierInvoiceStatus.Unpaid)]
    [InlineData(1000, 400, 0, SupplierInvoiceStatus.PartiallyPaid)]
    [InlineData(1000, 1000, 0, SupplierInvoiceStatus.Paid)]
    [InlineData(1000, 300, 700, SupplierInvoiceStatus.Paid)]
    public void Status(long amount, long paid, long returned, SupplierInvoiceStatus expected)
    {
        var i = new SupplierInvoice { Amount = amount, PaidAmount = paid, ReturnedAmount = returned };
        i.RecalculateStatus();
        i.Status.ShouldBe(expected);
    }
}
