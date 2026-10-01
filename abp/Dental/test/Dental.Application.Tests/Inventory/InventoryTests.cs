using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Data.Seed;
using Dental.Inventory;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Xunit;

namespace Dental.Inventory;

/// <summary>Ключевые инварианты склада: FEFO, средневзвешенная, журнал + кэш, перемещение с недостачей, инвентаризация, лимит списания.</summary>
public abstract class InventoryTests<TStartupModule> : DentalApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    // ---------- чистая логика ----------

    [Fact]
    public void Fefo_Takes_Nearest_Expiry_First_And_Undated_Last()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var stock = new[]
        {
            new BatchStock(a, null, 10, 1),
            new BatchStock(b, new DateOnly(2027, 1, 1), 3, 2),
            new BatchStock(c, new DateOnly(2026, 12, 1), 2, 3),
        };
        var alloc = Fefo.Allocate(stock, 6, allowNegative: false);
        alloc.Select(x => (x.BatchId, x.Qty)).ShouldBe(new[] { ((Guid?)c, 2m), (b, 3m), (a, 1m) });
    }

    [Fact]
    public void Fefo_Insufficient_Throws_Or_Goes_Negative()
    {
        var a = Guid.NewGuid();
        var stock = new[] { new BatchStock(a, null, 2, 5) };
        Should.Throw<StockException>(() => Fefo.Allocate(stock, 3, allowNegative: false)).Code.ShouldBe(DentalDomainErrorCodes.StockInsufficient);
        var alloc = Fefo.Allocate(stock, 3, allowNegative: true);
        alloc.Single().Qty.ShouldBe(3);
    }

    [Fact]
    public void Weighted_Average_Ignores_Negative_Stock()
    {
        Fefo.WeightedAverage(10, 100, 10, 200).ShouldBe(150);
        Fefo.WeightedAverage(-5, 100, 10, 200).ShouldBe(200);
    }

    // ---------- проведение документов ----------

    private async Task<Guid> TenantIdAsync() =>
        (await GetRequiredService<ITenantRepository>().FindByNameAsync(DentalDemoDataSeedContributor.DemoTenantName.ToUpperInvariant()))!.Id;

    private async Task InTenantAsync(Func<Task> action)
    {
        using (GetRequiredService<ICurrentTenant>().Change(await TenantIdAsync()))
        {
            await WithUnitOfWorkAsync(action);
        }
    }

    [Fact]
    public async Task Demo_Seed_Has_Items_Stock_And_Consistent_Cache()
    {
        await InTenantAsync(async () =>
        {
            (await GetRequiredService<IRepository<Item, Guid>>().GetCountAsync()).ShouldBeGreaterThan(40);
            (await GetRequiredService<IRepository<Supplier, Guid>>().GetCountAsync()).ShouldBe(5);
            (await GetRequiredService<IRepository<StockMovement, Guid>>().GetCountAsync()).ShouldBeGreaterThan(0);
            (await GetRequiredService<StockManager>().RebuildBalancesAsync()).ShouldBe(0);
        });
    }

    [Fact]
    public async Task Receipt_Transfer_With_Shortage_Creates_Pending_Writeoff()
    {
        await InTenantAsync(async () =>
        {
            var stock = GetRequiredService<StockManager>();
            var warehouses = await GetRequiredService<IRepository<Warehouse, Guid>>().GetListAsync();
            var central = warehouses.First(w => w.Type == WarehouseType.Central);
            var branch = warehouses.First(w => w.Type == WarehouseType.Branch);
            var supplier = (await GetRequiredService<IRepository<Supplier, Guid>>().GetListAsync()).First();
            var item = await NewItemAsync("T-REC");

            var receipt = await stock.CreateAsync(StockDocumentType.Receipt, new StockDocumentHeader(null, central.Id, supplier.Id),
                [new StockLineData(item.Id, 10, UnitCost: 100_00), new StockLineData(item.Id, 10, UnitCost: 200_00)]);
            receipt = await stock.PostAsync(receipt.Id);
            receipt.Status.ShouldBe(StockDocumentStatus.Posted);
            receipt.TotalCost.ShouldBe(3000_00);
            var bal = await BalanceAsync(central.Id, item.Id);
            bal.Qty.ShouldBe(20);
            bal.AvgCost.ShouldBe(150_00);

            var transfer = await stock.CreateAsync(StockDocumentType.Transfer, new StockDocumentHeader(central.Id, branch.Id), [new StockLineData(item.Id, 4)]);
            transfer = await stock.PostAsync(transfer.Id);
            transfer.Status.ShouldBe(StockDocumentStatus.InTransit);
            (await BalanceAsync(central.Id, item.Id)).Qty.ShouldBe(16);

            transfer = await stock.ReceiveAsync(transfer.Id, new Dictionary<Guid, decimal> { [transfer.Lines.Single().Id] = 3 }, null);
            transfer.Status.ShouldBe(StockDocumentStatus.Received);
            (await BalanceAsync(branch.Id, item.Id)).Qty.ShouldBe(3);

            var shortage = (await GetRequiredService<IRepository<StockDocument, Guid>>().GetListAsync(d => d.SourceDocumentId == transfer.Id)).Single();
            shortage.Status.ShouldBe(StockDocumentStatus.PendingApproval);
            shortage.TotalCost.ShouldBe(150_00);

            // Подтверждение недостачи: проводится без движений.
            await stock.CompleteWriteoffAsync(await stock.GetWithLinesAsync(shortage.Id));
            shortage.Status.ShouldBe(StockDocumentStatus.Posted);
            (await BalanceAsync(central.Id, item.Id)).Qty.ShouldBe(16);

            // Сторно проведённого прихода: нужна причина; уйти в минус (16 − 20) нельзя.
            (await Should.ThrowAsync<StockException>(() => stock.CancelAsync(receipt.Id, null))).Code.ShouldBe(DentalDomainErrorCodes.StockCommentRequired);
            (await Should.ThrowAsync<StockException>(() => stock.CancelAsync(receipt.Id, "ошибка"))).Code.ShouldBe(DentalDomainErrorCodes.StockInsufficient);
        });
    }

    [Fact]
    public async Task Writeoff_Over_Role_Limit_Is_Rejected_By_Default_Gateway()
    {
        await InTenantAsync(async () =>
        {
            var stock = GetRequiredService<StockManager>();
            var central = (await GetRequiredService<IRepository<Warehouse, Guid>>().GetListAsync()).First(w => w.Type == WarehouseType.Central);
            var reason = (await GetRequiredService<IRepository<WriteoffReason, Guid>>().GetListAsync()).First();
            var item = await NewItemAsync("T-WO");
            var supplier = (await GetRequiredService<IRepository<Supplier, Guid>>().GetListAsync()).First();
            var receipt = await stock.CreateAsync(StockDocumentType.Receipt, new StockDocumentHeader(null, central.Id, supplier.Id), [new StockLineData(item.Id, 5, UnitCost: 1000_00)]);
            await stock.PostAsync(receipt.Id);

            var wo = await stock.CreateAsync(StockDocumentType.Writeoff, new StockDocumentHeader(central.Id, null, ReasonId: reason.Id), [new StockLineData(item.Id, 1)]);
            var ex = await Should.ThrowAsync<BusinessException>(() => stock.PostAsync(wo.Id));
            ex.Code.ShouldBe(DentalDomainErrorCodes.RoleLimitExceeded);
        });
    }

    [Fact]
    public async Task Inventory_Locks_Warehouse_And_Posts_Difference()
    {
        await InTenantAsync(async () =>
        {
            var stock = GetRequiredService<StockManager>();
            var warehouses = GetRequiredService<IRepository<Warehouse, Guid>>();
            var branch = (await warehouses.GetListAsync()).Last(w => w.Type == WarehouseType.Branch);
            var count = await stock.CreateAsync(StockDocumentType.Inventory, new StockDocumentHeader(branch.Id, null), null);
            count.Lines.ShouldNotBeEmpty();
            (await warehouses.GetAsync(branch.Id)).IsLocked.ShouldBeTrue();

            var transfer = await stock.CreateAsync(StockDocumentType.Transfer, new StockDocumentHeader(branch.Id, (await warehouses.GetListAsync()).First(w => w.Type == WarehouseType.Central).Id),
                [new StockLineData(count.Lines[0].ItemId, 1)]);
            (await Should.ThrowAsync<StockException>(() => stock.PostAsync(transfer.Id))).Code.ShouldBe(DentalDomainErrorCodes.StockWarehouseLocked);

            var line = count.Lines[0];
            var before = (await BalanceAsync(branch.Id, line.ItemId, line.BatchId)).Qty;
            count = await stock.PostAsync(count.Id, new Dictionary<Guid, decimal> { [line.Id] = line.ExpectedQty!.Value + 2 });
            count.Status.ShouldBe(StockDocumentStatus.Posted);
            (await BalanceAsync(branch.Id, line.ItemId, line.BatchId)).Qty.ShouldBe(before + 2);
            (await warehouses.GetAsync(branch.Id)).IsLocked.ShouldBeFalse();
        });
    }

    private async Task<Item> NewItemAsync(string sku)
    {
        var item = new Item(Guid.NewGuid(), GetRequiredService<ICurrentTenant>().Id, sku + "-" + Guid.NewGuid().ToString("N")[..6], "Тест " + sku, BaseUnit.Pcs);
        return await GetRequiredService<IRepository<Item, Guid>>().InsertAsync(item, autoSave: true);
    }

    private async Task<StockBalance> BalanceAsync(Guid warehouseId, Guid itemId, Guid? batchId = null)
    {
        var list = await GetRequiredService<IRepository<StockBalance, Guid>>().GetListAsync(b => b.WarehouseId == warehouseId && b.ItemId == itemId);
        return list.Single(b => b.BatchId == batchId);
    }
}
