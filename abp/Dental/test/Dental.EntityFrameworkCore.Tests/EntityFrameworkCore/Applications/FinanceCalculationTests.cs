using System;
using Dental.Finance;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Dental.EntityFrameworkCore.Applications;

public class FinanceCalculationTests
{
    [Fact]
    public void Discounts_Round_In_Minor_Units_And_Total_Is_Exact()
    { var i = new VisitItem(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, 101, 50, [11, 85]); i.DiscountAmount.ShouldBe(152); i.Total.ShouldBe(151); }
    [Theory]
    [InlineData(0, 100, 0)] [InlineData(101, 100, 0)] [InlineData(1, -1, 0)] [InlineData(1, 100, -1)] [InlineData(1, 100, 101)]
    public void Invalid_Line_Cannot_Enter_Visit(int qty, long price, decimal discount)
    { Should.Throw<BusinessException>(() => new VisitItem(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), qty, price, discount)); }
    [Theory] [InlineData(19)] [InlineData(50)] [InlineData(86)] [InlineData(99)]
    public void Invalid_Fdi_Tooth_Number_Is_Rejected(int tooth)
    { Should.Throw<BusinessException>(() => new VisitItem(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 100, 0, [tooth])); }
    [Fact]
    public void Shift_Counts_Cash_Only_And_Excludes_Unapproved_Refunds()
    {
        var shift = Guid.NewGuid(); var patient = Guid.NewGuid(); var branch = Guid.NewGuid();
        Payment P(PaymentMethod method, long amount, PaymentType type) => new(Guid.NewGuid(), null, branch, patient, null, shift, method, amount, type, null, null);
        var expected = CashShift.ExpectedCash(10000, [P(PaymentMethod.Cash, 5000, PaymentType.Payment), P(PaymentMethod.Card, 9000, PaymentType.Payment), P(PaymentMethod.Cash, 2000, PaymentType.Refund), P(PaymentMethod.Balance, 7000, PaymentType.Payment)],
            [new Expense(Guid.NewGuid(), null, branch, Guid.NewGuid(), 1000, DateTime.UtcNow, shift, null, null), new Expense(Guid.NewGuid(), null, branch, Guid.NewGuid(), 8000, DateTime.UtcNow, null, null, null)],
            [new CashOperation(Guid.NewGuid(), null, shift, CashOperationType.Deposit, 3000, "deposit"), new CashOperation(Guid.NewGuid(), null, shift, CashOperationType.Collection, 4000, "collection")]);
        expected.ShouldBe(11000);
    }
    [Fact]
    public void Mutations_Require_Current_Concurrency_Stamp()
    { Should.Throw<BusinessException>(() => FinanceManager.CheckStamp("current", null)); Should.Throw<BusinessException>(() => FinanceManager.CheckStamp("current", "stale")); FinanceManager.CheckStamp("current", "current"); }
}
