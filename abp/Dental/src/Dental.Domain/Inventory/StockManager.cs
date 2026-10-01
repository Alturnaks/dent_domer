using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Dental.Settings;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Settings;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace Dental.Inventory;

/// <summary>
/// Складской учёт через журнал движений. Проведение документа атомарно пишет StockMovement
/// и обновляет кэш StockBalance в той же транзакции (строки остатков блокируются FOR UPDATE в фиксированном порядке).
/// Права и доступ к филиалам проверяет app-слой.
/// </summary>
[ExposeServices(typeof(StockManager), typeof(IVisitStockConsumer))]
public class StockManager : DomainService, IVisitStockConsumer
{
    private sealed record MoveSpec(Guid WarehouseId, Guid ItemId, Guid? BatchId, decimal Qty, decimal UnitCost, Guid? LineId, Guid? PatientId = null);

    public sealed record BalanceResult(Guid WarehouseId, Guid ItemId, Guid? BatchId, decimal NewQty);

    private readonly IRepository<StockDocument, Guid> _documents;
    private readonly IRepository<StockBalance, Guid> _balances;
    private readonly IRepository<StockMovement, Guid> _movements;
    private readonly IRepository<Batch, Guid> _batches;
    private readonly IRepository<Item, Guid> _items;
    private readonly IRepository<Warehouse, Guid> _warehouses;
    private readonly IRepository<WriteoffReason, Guid> _reasons;
    private readonly IRepository<SupplierItem, Guid> _supplierItems;
    private readonly IStockLockProvider _locks;
    private readonly IDocumentNumberGenerator _numbers;
    private readonly IApprovalGateway _approvals;
    private readonly ISettingProvider _settings;
    private readonly IUnitOfWorkManager _uow;

    protected ICurrentUser CurrentUser => LazyServiceProvider.LazyGetRequiredService<ICurrentUser>();

    public StockManager(
        IRepository<StockDocument, Guid> documents,
        IRepository<StockBalance, Guid> balances,
        IRepository<StockMovement, Guid> movements,
        IRepository<Batch, Guid> batches,
        IRepository<Item, Guid> items,
        IRepository<Warehouse, Guid> warehouses,
        IRepository<WriteoffReason, Guid> reasons,
        IRepository<SupplierItem, Guid> supplierItems,
        IStockLockProvider locks,
        IDocumentNumberGenerator numbers,
        IApprovalGateway approvals,
        ISettingProvider settings,
        IUnitOfWorkManager uow)
    {
        _documents = documents;
        _balances = balances;
        _movements = movements;
        _batches = batches;
        _items = items;
        _warehouses = warehouses;
        _reasons = reasons;
        _supplierItems = supplierItems;
        _locks = locks;
        _numbers = numbers;
        _approvals = approvals;
        _settings = settings;
        _uow = uow;
    }

    // ================= Документы =================

    public async Task<StockDocument> GetWithLinesAsync(Guid id)
    {
        var q = (await _documents.WithDetailsAsync(d => d.Lines)).Where(d => d.Id == id);
        return await AsyncExecuter.FirstOrDefaultAsync(q) ?? throw new EntityNotFoundException(typeof(StockDocument), id);
    }

    public async Task<StockDocument> CreateAsync(StockDocumentType type, StockDocumentHeader header, IReadOnlyList<StockLineData>? lines)
    {
        if (type == StockDocumentType.VisitConsumption)
        {
            throw StockException.BadRequest(DentalDomainErrorCodes.StockVisitConsumptionManual);
        }
        var doc = new StockDocument(GuidGenerator.Create(), CurrentTenant.Id, type, await NextNumberAsync(type))
        {
            WarehouseFromId = header.WarehouseFromId,
            WarehouseToId = header.WarehouseToId,
            SupplierId = header.SupplierId,
            PurchaseOrderId = header.PurchaseOrderId,
            InvoiceNumber = NullIfEmpty(header.InvoiceNumber),
            InvoiceDate = header.InvoiceDate,
            ReasonId = header.ReasonId,
            Comment = NullIfEmpty(header.Comment),
        };
        ValidateWarehouses(doc);
        doc.BranchId = await BranchOfDocAsync(doc);
        if (type == StockDocumentType.Inventory)
        {
            await StartInventoryAsync(doc);
        }
        else
        {
            await SetLinesAsync(doc, lines ?? []);
        }
        await _documents.InsertAsync(doc, autoSave: true);
        return doc;
    }

    /// <summary>Редактирование черновика. null в шапке — оставить как есть, "" — очистить.</summary>
    public async Task<StockDocument> UpdateAsync(StockDocument doc, StockDocumentHeader header, IReadOnlyList<StockLineData>? lines)
    {
        doc.EnsureDraft();
        if (doc.Type != StockDocumentType.Inventory)
        {
            doc.WarehouseFromId = header.WarehouseFromId ?? doc.WarehouseFromId;
            doc.WarehouseToId = header.WarehouseToId ?? doc.WarehouseToId;
            ValidateWarehouses(doc);
        }
        if (header.SupplierId is not null) doc.SupplierId = header.SupplierId;
        if (header.InvoiceNumber is not null) doc.InvoiceNumber = NullIfEmpty(header.InvoiceNumber);
        if (header.InvoiceDate is not null) doc.InvoiceDate = header.InvoiceDate;
        if (header.ReasonId is not null) doc.ReasonId = header.ReasonId;
        if (header.Comment is not null) doc.Comment = NullIfEmpty(header.Comment);
        if (lines is not null)
        {
            if (doc.Type == StockDocumentType.Inventory)
            {
                // Для инвентаризации строки не заменяются: вводится только факт; новые позиции — с ожидаемым 0.
                foreach (var l in lines)
                {
                    var line = doc.Lines.FirstOrDefault(x => x.ItemId == l.ItemId && x.BatchId == l.BatchId);
                    if (line is not null) line.ActualQty = l.ActualQty ?? l.Qty;
                    else
                    {
                        var added = await NewLineAsync(doc, l);
                        added.ExpectedQty = 0;
                        added.ActualQty = l.ActualQty ?? l.Qty;
                        added.Qty = 0;
                        added.QtyInput = 0;
                        added.TotalCost = 0;
                        doc.Lines.Add(added);
                    }
                }
            }
            else
            {
                await SetLinesAsync(doc, lines);
            }
        }
        doc.BranchId = await BranchOfDocAsync(doc);
        await SaveAsync();
        return doc;
    }

    /// <summary>Проведение документа (приход, списание, отправка перемещения, возврат поставщику, утверждение инвентаризации).</summary>
    public async Task<StockDocument> PostAsync(Guid id, IReadOnlyDictionary<Guid, decimal>? counts = null)
    {
        await _locks.LockDocumentAsync(id);
        var doc = await GetWithLinesAsync(id);
        if (doc.Status != StockDocumentStatus.Draft)
        {
            throw new StockException(DentalDomainErrorCodes.StockDocumentInvalidState);
        }
        if (doc.Lines.Count == 0 && doc.Type != StockDocumentType.Inventory)
        {
            throw StockException.BadRequest(DentalDomainErrorCodes.StockLinesRequired);
        }
        switch (doc.Type)
        {
            case StockDocumentType.Receipt: await PostReceiptAsync(doc); break;
            case StockDocumentType.Writeoff: await PostWriteoffAsync(doc); break;
            case StockDocumentType.Transfer: await SendTransferAsync(doc); break;
            case StockDocumentType.ReturnToSupplier: await PostReturnAsync(doc); break;
            case StockDocumentType.Inventory: await ApproveInventoryAsync(doc, counts); break;
            default: throw new StockException(DentalDomainErrorCodes.StockDocumentInvalidState);
        }
        await SaveAsync();
        return doc;
    }

    /// <summary>Приёмка перемещения получателем: приход фактического количества; недостача — в документ списания на подтверждение.</summary>
    public async Task<StockDocument> ReceiveAsync(Guid id, IReadOnlyDictionary<Guid, decimal>? actuals, string? comment)
    {
        await _locks.LockDocumentAsync(id);
        var doc = await GetWithLinesAsync(id);
        if (doc.Type != StockDocumentType.Transfer || doc.Status != StockDocumentStatus.InTransit)
        {
            throw new StockException(DentalDomainErrorCodes.StockDocumentInvalidState);
        }
        var to = await _warehouses.GetAsync(doc.WarehouseToId!.Value);
        var moves = new List<MoveSpec>();
        var shortage = new List<(StockDocumentLine Line, decimal Qty)>();
        foreach (var line in doc.Lines)
        {
            var actual = actuals is not null && actuals.TryGetValue(line.Id, out var a) ? a : line.Qty;
            if (actual < 0 || actual > line.Qty)
            {
                throw StockException.BadRequest(DentalDomainErrorCodes.StockInvalidActualQty);
            }
            line.ActualQty = actual;
            if (actual > 0) moves.Add(new MoveSpec(to.Id, line.ItemId, line.BatchId, actual, line.UnitCost, line.Id));
            if (actual < line.Qty) shortage.Add((line, line.Qty - actual));
        }
        await ApplyMovesAsync(doc, moves, allowNegative: false);
        doc.MarkReceived(Clock.Now, CurrentUser.Id);
        doc.AppendComment(comment);
        if (shortage.Count > 0)
        {
            await CreateShortageWriteoffAsync(doc, to, shortage);
        }
        doc.RaiseChanged("received");
        await SaveAsync();
        return doc;
    }

    /// <summary>Отмена: черновик/ожидающий — просто отменяется; проведённый приход, списание или возврат — сторно движений.</summary>
    public async Task<StockDocument> CancelAsync(Guid id, string? comment)
    {
        await _locks.LockDocumentAsync(id);
        var doc = await GetWithLinesAsync(id);
        switch (doc.Status)
        {
            case StockDocumentStatus.Draft or StockDocumentStatus.PendingApproval:
                if (doc.Type == StockDocumentType.Inventory) await UnlockWarehouseAsync(doc);
                await _approvals.CancelPendingAsync(nameof(StockDocument), doc.Id, "Документ отменён");
                doc.Status = StockDocumentStatus.Cancelled;
                doc.RaiseChanged("cancelled");
                break;
            case StockDocumentStatus.Posted when doc.Type is StockDocumentType.Receipt or StockDocumentType.Writeoff or StockDocumentType.ReturnToSupplier:
                if (string.IsNullOrWhiteSpace(comment))
                {
                    throw StockException.BadRequest(DentalDomainErrorCodes.StockCommentRequired);
                }
                await ReverseMovementsAsync(doc);
                doc.Status = StockDocumentStatus.Cancelled;
                Logger.LogWarning("Suspicious: storno of stock document {Number} for {Total} tiyn: {Comment}", doc.Number, doc.TotalCost, comment);
                doc.RaiseChanged("storno");
                break;
            default:
                throw new StockException(DentalDomainErrorCodes.StockDocumentInvalidState);
        }
        doc.AppendComment(comment, "Отмена: ");
        await SaveAsync();
        return doc;
    }

    /// <summary>Подтверждённое списание (сразу или после подтверждения в модуле Approvals).</summary>
    public async Task CompleteWriteoffAsync(StockDocument doc)
    {
        if (doc.Status is not (StockDocumentStatus.PendingApproval or StockDocumentStatus.Draft)) return;
        var moves = doc.Lines.Select(l => new MoveSpec(doc.WarehouseFromId!.Value, l.ItemId, l.BatchId, -l.Qty, l.UnitCost, l.Id)).ToList();
        if (doc.SourceDocumentId is not null)
        {
            // Недостача при перемещении: товар уже списан со склада-отправителя и не пришёл — движений нет.
            moves.Clear();
        }
        await ApplyMovesAsync(doc, moves, allowNegative: false);
        doc.MarkPosted(Clock.Now, CurrentUser.Id);
        if (doc.TotalCost > await GetLongSettingAsync(DentalSettings.SuspiciousWriteoffAmount))
        {
            Logger.LogWarning("Suspicious: large writeoff {Number} for {Total} tiyn", doc.Number, doc.TotalCost);
        }
        doc.RaiseChanged("posted");
    }

    /// <summary>Отклонение подтверждения: документ возвращается в черновик (недостача остаётся на разбор).</summary>
    public Task RejectPendingAsync(StockDocument doc)
    {
        if (doc.Status == StockDocumentStatus.PendingApproval) doc.Status = StockDocumentStatus.Draft;
        return Task.CompletedTask;
    }

    // ================= Проведение по типам =================

    private async Task PostReceiptAsync(StockDocument doc)
    {
        if (doc.SupplierId is null) throw StockException.BadRequest(DentalDomainErrorCodes.StockSupplierRequired);
        if (doc.WarehouseToId is null) throw StockException.BadRequest(DentalDomainErrorCodes.StockWarehouseRequired);
        var items = await ItemsAsync(doc.Lines.Select(l => l.ItemId));
        var moves = new List<MoveSpec>();
        foreach (var line in doc.Lines)
        {
            var item = items[line.ItemId];
            if (line.Qty <= 0) throw StockException.BadRequest(DentalDomainErrorCodes.StockQtyMustBePositive).WithData("name", item.Name);
            if (item.TrackSerials && (line.Qty != 1 || string.IsNullOrWhiteSpace(line.SerialNumber)))
                throw StockException.BadRequest(DentalDomainErrorCodes.StockSerialRequired).WithData("name", item.Name);
            if (item.TrackBatches && string.IsNullOrWhiteSpace(line.BatchNumber) && string.IsNullOrWhiteSpace(line.SerialNumber))
                throw StockException.BadRequest(DentalDomainErrorCodes.StockBatchRequired).WithData("name", item.Name);
            if (item.TrackExpiry && line.ExpiresAt is null)
                throw StockException.BadRequest(DentalDomainErrorCodes.StockExpiryRequired).WithData("name", item.Name);

            if (item.UsesBatches)
            {
                var batch = new Batch(GuidGenerator.Create(), CurrentTenant.Id, item.Id, line.BatchNumber, line.SerialNumber, line.ExpiresAt, line.UnitCost, doc.SupplierId);
                await _batches.InsertAsync(batch, autoSave: true);
                line.BatchId = batch.Id;
            }
            line.RecalculateTotal();
            moves.Add(new MoveSpec(doc.WarehouseToId.Value, line.ItemId, line.BatchId, line.Qty, line.UnitCost, line.Id));
        }
        await ApplyMovesAsync(doc, moves, allowNegative: false);
        doc.RecalculateTotal();
        doc.MarkPosted(Clock.Now, CurrentUser.Id);

        // Последняя цена у поставщика (тиыны за базовую единицу).
        var supplierId = doc.SupplierId.Value;
        var existing = (await _supplierItems.GetListAsync(s => s.SupplierId == supplierId)).ToDictionary(s => s.ItemId);
        foreach (var g in doc.Lines.GroupBy(l => l.ItemId))
        {
            var price = MoneyMath.Round(g.Last().UnitCost);
            if (!existing.TryGetValue(g.Key, out var si))
            {
                si = new SupplierItem(GuidGenerator.Create(), CurrentTenant.Id, supplierId, g.Key);
                si.SetPrice(price, Clock.Now);
                await _supplierItems.InsertAsync(si);
            }
            else
            {
                si.SetPrice(price, Clock.Now);
            }
        }
        doc.RaiseChanged("posted");
    }

    private async Task PostWriteoffAsync(StockDocument doc)
    {
        if (doc.ReasonId is null) throw StockException.BadRequest(DentalDomainErrorCodes.StockReasonRequired);
        var from = await GetWarehouseForOutAsync(doc.WarehouseFromId);
        await AllocateLinesAsync(doc, from.Id, allowNegative: false);
        doc.RecalculateTotal();
        var allowed = await _approvals.RequestIfOverWriteoffLimitAsync(InventoryApprovalTypes.Writeoff, nameof(StockDocument), doc.Id, doc.TotalCost,
            $"Списание {doc.Number} на {FormatMoney(doc.TotalCost)}", doc.BranchId,
            new { doc.Number, doc.TotalCost, lines = doc.Lines.Count });
        if (!allowed)
        {
            doc.Status = StockDocumentStatus.PendingApproval;
            return;
        }
        await CompleteWriteoffAsync(doc);
    }

    private async Task SendTransferAsync(StockDocument doc)
    {
        var from = await GetWarehouseForOutAsync(doc.WarehouseFromId);
        if (doc.WarehouseToId is null || doc.WarehouseToId == doc.WarehouseFromId)
        {
            throw StockException.BadRequest(DentalDomainErrorCodes.StockSameWarehouse);
        }
        await AllocateLinesAsync(doc, from.Id, allowNegative: false);
        var moves = doc.Lines.Select(l => new MoveSpec(from.Id, l.ItemId, l.BatchId, -l.Qty, l.UnitCost, l.Id)).ToList();
        await ApplyMovesAsync(doc, moves, allowNegative: false);
        doc.RecalculateTotal();
        doc.MarkInTransit(Clock.Now, CurrentUser.Id);
        doc.RaiseChanged("sent");
    }

    private async Task PostReturnAsync(StockDocument doc)
    {
        if (doc.SupplierId is null) throw StockException.BadRequest(DentalDomainErrorCodes.StockSupplierRequired);
        var from = await GetWarehouseForOutAsync(doc.WarehouseFromId);
        await AllocateLinesAsync(doc, from.Id, allowNegative: false);
        var moves = doc.Lines.Select(l => new MoveSpec(from.Id, l.ItemId, l.BatchId, -l.Qty, l.UnitCost, l.Id)).ToList();
        await ApplyMovesAsync(doc, moves, allowNegative: false);
        doc.RecalculateTotal();
        doc.MarkPosted(Clock.Now, CurrentUser.Id);
        doc.RaiseChanged("posted");
    }

    // ================= Инвентаризация =================

    private async Task StartInventoryAsync(StockDocument doc)
    {
        var w = await _warehouses.GetAsync(doc.WarehouseFromId!.Value);
        w.Lock(doc.Id);
        doc.SnapshotAt = Clock.Now;
        var balances = await _balances.GetListAsync(b => b.WarehouseId == w.Id && b.Qty != 0);
        var batchIds = balances.Where(b => b.BatchId != null).Select(b => b.BatchId!.Value).ToList();
        var batches = (await _batches.GetListAsync(b => batchIds.Contains(b.Id))).ToDictionary(b => b.Id);
        foreach (var b in balances)
        {
            var batch = b.BatchId is { } bid && batches.TryGetValue(bid, out var bt) ? bt : null;
            doc.Lines.Add(new StockDocumentLine(GuidGenerator.Create(), doc.Id, b.ItemId)
            {
                BatchId = b.BatchId, Qty = 0, QtyInput = 0, ExpectedQty = b.Qty, ActualQty = null, UnitCost = batch?.UnitCost ?? b.AvgCost,
                BatchNumber = batch?.BatchNumber, SerialNumber = batch?.SerialNumber, ExpiresAt = batch?.ExpiresAt,
            });
        }
    }

    /// <summary>
    /// Утверждение: движения на разницу (факт − ожидаемое на момент старта). Расход визитов во время пересчёта уже учтён
    /// в остатке и не искажает результат: итог = факт + расход после снимка.
    /// </summary>
    private async Task ApproveInventoryAsync(StockDocument doc, IReadOnlyDictionary<Guid, decimal>? counts)
    {
        foreach (var c in counts ?? new Dictionary<Guid, decimal>())
        {
            var line = doc.Lines.FirstOrDefault(l => l.Id == c.Key) ?? throw new EntityNotFoundException(typeof(StockDocumentLine), c.Key);
            if (c.Value < 0) throw StockException.BadRequest(DentalDomainErrorCodes.StockInvalidActualQty);
            line.ActualQty = c.Value;
        }
        var moves = new List<MoveSpec>();
        long diffValue = 0;
        foreach (var line in doc.Lines)
        {
            var actual = line.ActualQty ?? line.ExpectedQty ?? 0;
            line.ActualQty = actual;
            var diff = actual - (line.ExpectedQty ?? 0);
            line.Qty = diff;
            line.QtyInput = diff;
            line.RecalculateTotal();
            diffValue += Math.Abs(line.TotalCost);
            if (diff != 0) moves.Add(new MoveSpec(doc.WarehouseFromId!.Value, line.ItemId, line.BatchId, diff, line.UnitCost, line.Id));
        }
        await ApplyMovesAsync(doc, moves, allowNegative: true);
        doc.RecalculateTotal();
        doc.MarkPosted(Clock.Now, CurrentUser.Id);
        await UnlockWarehouseAsync(doc);
        if (diffValue > await GetLongSettingAsync(DentalSettings.SuspiciousInventoryDiffAmount))
        {
            Logger.LogWarning("Suspicious: inventory {Number} discrepancy {Diff} tiyn (net {Net})", doc.Number, diffValue, doc.TotalCost);
        }
        doc.RaiseChanged("posted");
    }

    private async Task UnlockWarehouseAsync(StockDocument doc)
    {
        if (doc.WarehouseFromId is not { } wid) return;
        var w = await _warehouses.FindAsync(wid);
        w?.Unlock(doc.Id);
    }

    // ================= Расход по визиту =================

    public async Task<(Guid DocumentId, IReadOnlyDictionary<Guid, VisitMaterialCost> Costs)> ConsumeAsync(
        Guid visitId, Guid branchId, Guid patientId, IReadOnlyList<VisitMaterialUsage> usages)
    {
        var candidates = await _warehouses.GetListAsync(w => w.BranchId == branchId);
        var warehouse = candidates.OrderBy(w => w.Type == WarehouseType.Cabinet ? 0 : 1).ThenBy(w => w.CreationTime).FirstOrDefault()
                        ?? throw new StockException(DentalDomainErrorCodes.StockNoBranchWarehouse);
        var allowNegative = await GetBoolSettingAsync(DentalSettings.AllowNegativeStock);
        var doc = new StockDocument(GuidGenerator.Create(), CurrentTenant.Id, StockDocumentType.VisitConsumption, await NextNumberAsync(StockDocumentType.VisitConsumption))
        {
            VisitId = visitId, BranchId = branchId, WarehouseFromId = warehouse.Id,
        };
        var costs = new Dictionary<Guid, VisitMaterialCost>();
        var moves = new List<MoveSpec>();
        var items = await ItemsAsync(usages.Select(u => u.ItemId));
        foreach (var u in usages.Where(u => u.Qty > 0))
        {
            var allocation = await AllocateAsync(warehouse.Id, u.ItemId, u.Qty, allowNegative);
            long cost = 0;
            Guid? batch = null;
            foreach (var a in allocation)
            {
                var line = new StockDocumentLine(GuidGenerator.Create(), doc.Id, u.ItemId) { BatchId = a.BatchId, Qty = a.Qty, QtyInput = a.Qty, UnitCost = a.UnitCost };
                line.RecalculateTotal();
                doc.Lines.Add(line);
                moves.Add(new MoveSpec(warehouse.Id, u.ItemId, a.BatchId, -a.Qty, a.UnitCost, line.Id, items[u.ItemId].TrackSerials ? patientId : null));
                cost += line.TotalCost;
                batch ??= a.BatchId;
            }
            costs[u.UsageId] = new VisitMaterialCost(cost, batch);
        }
        await _documents.InsertAsync(doc, autoSave: true);
        var result = await ApplyMovesAsync(doc, moves, allowNegative);
        doc.RecalculateTotal();
        doc.MarkPosted(Clock.Now, CurrentUser.Id);
        var negative = result.Where(r => r.NewQty < 0).Select(r => items.TryGetValue(r.ItemId, out var i) ? i.Name : r.ItemId.ToString()).Distinct().ToList();
        if (negative.Count > 0)
        {
            Logger.LogWarning("Negative stock on warehouse {Warehouse}: {Items}", warehouse.Name, string.Join(", ", negative));
            await LazyServiceProvider.LazyGetRequiredService<Dental.Notifications.NotificationManager>().NotifyByPermissionAsync(Dental.Permissions.DentalPermissions.Inventory.View,"stock_low","Отрицательный остаток: " + warehouse.Name,string.Join(", ",negative),nameof(StockDocument),doc.Id,branchId,null);
        }
        doc.RaiseChanged("posted");
        await SaveAsync();
        return (doc.Id, costs);
    }

    public async Task ReverseAsync(Guid documentId, string reason)
    {
        var doc = await GetWithLinesAsync(documentId);
        if (doc.Status != StockDocumentStatus.Posted) return;
        await ReverseMovementsAsync(doc);
        doc.Status = StockDocumentStatus.Cancelled;
        doc.AppendComment(reason, "Сторно: ");
        doc.RaiseChanged("storno");
        await SaveAsync();
    }

    // ================= Движения и остатки =================

    /// <summary>
    /// Блокирует строки остатков (FOR UPDATE) в порядке (склад, товар), обновляет кэш и пишет журнал движений.
    /// Уход в минус запрещён, если allowNegative = false.
    /// </summary>
    private async Task<List<BalanceResult>> ApplyMovesAsync(StockDocument doc, IReadOnlyList<MoveSpec> moves, bool allowNegative)
    {
        var now = Clock.Now;
        var results = new List<BalanceResult>();
        foreach (var group in moves.GroupBy(m => (m.WarehouseId, m.ItemId)).OrderBy(g => g.Key.WarehouseId).ThenBy(g => g.Key.ItemId))
        {
            await _locks.LockBalancesAsync(group.Key.WarehouseId, group.Key.ItemId);
            foreach (var m in group)
            {
                var balance = await FindBalanceAsync(m.WarehouseId, m.ItemId, m.BatchId);
                if (balance is null)
                {
                    balance = new StockBalance(GuidGenerator.Create(), CurrentTenant.Id, m.WarehouseId, m.ItemId, m.BatchId);
                    await _balances.InsertAsync(balance, autoSave: true);
                }
                balance.Apply(m.Qty, m.UnitCost, now);
                if (balance.Qty < 0 && !allowNegative)
                {
                    var item = await _items.FindAsync(m.ItemId);
                    throw new StockException(DentalDomainErrorCodes.StockInsufficient)
                        .WithData("name", item?.Name ?? m.ItemId.ToString())
                        .WithData("missing", (-balance.Qty).ToString("0.####", CultureInfo.InvariantCulture));
                }
                await _movements.InsertAsync(new StockMovement(GuidGenerator.Create(), CurrentTenant.Id, doc.Id, m.LineId, m.WarehouseId, m.ItemId,
                    m.BatchId, m.Qty, m.UnitCost, now, m.PatientId, doc.Type));
                results.Add(new BalanceResult(m.WarehouseId, m.ItemId, m.BatchId, balance.Qty));
            }
        }
        await SaveAsync();
        return results;
    }

    private async Task<StockBalance?> FindBalanceAsync(Guid warehouseId, Guid itemId, Guid? batchId) =>
        batchId is { } b
            ? await _balances.FindAsync(x => x.WarehouseId == warehouseId && x.ItemId == itemId && x.BatchId == b)
            : await _balances.FindAsync(x => x.WarehouseId == warehouseId && x.ItemId == itemId && x.BatchId == null);

    private async Task ReverseMovementsAsync(StockDocument doc)
    {
        var original = await _movements.GetListAsync(m => m.DocumentId == doc.Id);
        var moves = original.Select(m => new MoveSpec(m.WarehouseId, m.ItemId, m.BatchId, -m.Qty, m.UnitCost, m.LineId, m.PatientId)).ToList();
        await ApplyMovesAsync(doc, moves, allowNegative: doc.Type == StockDocumentType.VisitConsumption);
    }

    /// <summary>FEFO-подбор партий на складе (строки остатков блокируются).</summary>
    public async Task<IReadOnlyList<BatchAllocation>> AllocateAsync(Guid warehouseId, Guid itemId, decimal qty, bool allowNegative)
    {
        await _locks.LockBalancesAsync(warehouseId, itemId);
        await SaveAsync();
        var balances = await _balances.GetListAsync(b => b.WarehouseId == warehouseId && b.ItemId == itemId);
        var batchIds = balances.Where(b => b.BatchId != null).Select(b => b.BatchId!.Value).ToList();
        var batches = (await _batches.GetListAsync(b => batchIds.Contains(b.Id))).ToDictionary(b => b.Id);
        var stock = balances.OrderBy(b => b.CreationTime).Select(b =>
        {
            var bt = b.BatchId is { } id && batches.TryGetValue(id, out var x) ? x : null;
            return new BatchStock(b.BatchId, bt?.ExpiresAt, b.Qty, bt?.UnitCost ?? b.AvgCost, bt?.CreationTime ?? b.CreationTime);
        }).ToList();
        decimal fallback;
        if (stock.Count > 0)
        {
            fallback = stock[^1].UnitCost;
        }
        else
        {
            var q = (await _batches.GetQueryableAsync()).Where(b => b.ItemId == itemId).OrderByDescending(b => b.CreationTime).Select(b => (decimal?)b.UnitCost);
            fallback = await AsyncExecuter.FirstOrDefaultAsync(q) ?? 0;
        }
        try
        {
            return Fefo.Allocate(stock, qty, allowNegative, fallback);
        }
        catch (StockException e) when (e.Code == DentalDomainErrorCodes.StockInsufficient)
        {
            var item = await _items.FindAsync(itemId);
            e.WithData("name", item?.Name ?? itemId.ToString());
            throw;
        }
    }

    /// <summary>Строки без партии разбиваются по партиям FEFO; строки с партией — берут её себестоимость.</summary>
    private async Task AllocateLinesAsync(StockDocument doc, Guid warehouseId, bool allowNegative)
    {
        foreach (var line in doc.Lines.ToList())
        {
            if (line.Qty <= 0) throw StockException.BadRequest(DentalDomainErrorCodes.StockQtyMustBePositive).WithData("name", "");
            if (line.BatchId is { } bid)
            {
                var batch = await _batches.GetAsync(bid);
                line.UnitCost = batch.UnitCost;
                line.RecalculateTotal();
                continue;
            }
            var alloc = await AllocateAsync(warehouseId, line.ItemId, line.Qty, allowNegative);
            var first = true;
            var originalQty = line.Qty;
            var originalInput = line.QtyInput;
            var unitId = line.UnitId;
            foreach (var a in alloc)
            {
                var target = line;
                if (!first)
                {
                    target = new StockDocumentLine(GuidGenerator.Create(), doc.Id, line.ItemId);
                    doc.Lines.Add(target);
                }
                target.BatchId = a.BatchId;
                target.Qty = a.Qty;
                target.UnitId = first ? unitId : null;
                target.QtyInput = first && unitId is not null ? decimal.Round(originalInput * (a.Qty / originalQty), 4) : a.Qty;
                target.UnitCost = a.UnitCost;
                target.RecalculateTotal();
                first = false;
            }
        }
        var batchIds = doc.Lines.Where(l => l.BatchId != null).Select(l => l.BatchId!.Value).Distinct().ToList();
        var batches = (await _batches.GetListAsync(b => batchIds.Contains(b.Id))).ToDictionary(b => b.Id);
        foreach (var l in doc.Lines.Where(l => l.BatchId != null))
        {
            var b = batches[l.BatchId!.Value];
            l.BatchNumber ??= b.BatchNumber;
            l.SerialNumber ??= b.SerialNumber;
            l.ExpiresAt ??= b.ExpiresAt;
        }
    }

    private async Task CreateShortageWriteoffAsync(StockDocument transfer, Warehouse to, List<(StockDocumentLine Line, decimal Qty)> shortage)
    {
        var reasonQ = (await _reasons.GetQueryableAsync()).Where(r => r.Type == WriteoffReasonType.Lost).OrderBy(r => r.CreationTime).Select(r => (Guid?)r.Id);
        var reason = await AsyncExecuter.FirstOrDefaultAsync(reasonQ);
        var wo = new StockDocument(GuidGenerator.Create(), CurrentTenant.Id, StockDocumentType.Writeoff, await NextNumberAsync(StockDocumentType.Writeoff))
        {
            Status = StockDocumentStatus.PendingApproval,
            WarehouseFromId = transfer.WarehouseFromId,
            WarehouseToId = to.Id,
            BranchId = to.BranchId,
            SourceDocumentId = transfer.Id,
            ReasonId = reason,
            Comment = $"Недостача при приёмке перемещения {transfer.Number}",
        };
        foreach (var (line, qty) in shortage)
        {
            var l = new StockDocumentLine(GuidGenerator.Create(), wo.Id, line.ItemId)
            {
                BatchId = line.BatchId, Qty = qty, QtyInput = qty, UnitCost = line.UnitCost, ExpectedQty = line.Qty, ActualQty = line.ActualQty,
                BatchNumber = line.BatchNumber, SerialNumber = line.SerialNumber, ExpiresAt = line.ExpiresAt,
            };
            l.RecalculateTotal();
            wo.Lines.Add(l);
        }
        wo.RecalculateTotal();
        await _documents.InsertAsync(wo, autoSave: true);
        await _approvals.RequestAsync(InventoryApprovalTypes.TransferShortage, nameof(StockDocument), wo.Id, wo.TotalCost,
            $"Недостача {FormatMoney(wo.TotalCost)} при приёмке {transfer.Number}", to.BranchId,
            new { transfer = transfer.Number, writeoff = wo.Number, wo.TotalCost });
    }

    /// <summary>Пересборка кэша остатков из журнала движений (текущий арендатор). Возвращает число изменённых строк.</summary>
    public async Task<int> RebuildBalancesAsync()
    {
        var movements = (await _movements.GetListAsync()).OrderBy(m => m.MovedAt).ThenBy(m => m.CreationTime).ToList();
        var balances = (await _balances.GetListAsync()).ToDictionary(b => (b.WarehouseId, b.ItemId, b.BatchId));
        var before = balances.ToDictionary(kv => kv.Key, kv => (kv.Value.Qty, kv.Value.AvgCost));
        foreach (var b in balances.Values) b.Reset();
        foreach (var m in movements)
        {
            var key = (m.WarehouseId, m.ItemId, m.BatchId);
            if (!balances.TryGetValue(key, out var b))
            {
                b = new StockBalance(GuidGenerator.Create(), CurrentTenant.Id, m.WarehouseId, m.ItemId, m.BatchId);
                await _balances.InsertAsync(b);
                balances[key] = b;
            }
            b.Apply(m.Qty, m.UnitCost, m.MovedAt);
        }
        await SaveAsync();
        return balances.Count(kv => !before.TryGetValue(kv.Key, out var old) || old.Qty != kv.Value.Qty || old.AvgCost != kv.Value.AvgCost);
    }

    // ================= Вспомогательное =================

    private async Task<Warehouse> GetWarehouseForOutAsync(Guid? id)
    {
        if (id is null) throw StockException.BadRequest(DentalDomainErrorCodes.StockWarehouseRequired);
        var w = await _warehouses.GetAsync(id.Value);
        w.EnsureNotLocked();
        return w;
    }

    private static void ValidateWarehouses(StockDocument d)
    {
        switch (d.Type)
        {
            case StockDocumentType.Receipt when d.WarehouseToId is null:
            case StockDocumentType.Writeoff or StockDocumentType.ReturnToSupplier or StockDocumentType.Inventory when d.WarehouseFromId is null:
            case StockDocumentType.Transfer when d.WarehouseFromId is null || d.WarehouseToId is null:
                throw StockException.BadRequest(DentalDomainErrorCodes.StockWarehouseRequired);
            case StockDocumentType.Transfer when d.WarehouseFromId == d.WarehouseToId:
                throw StockException.BadRequest(DentalDomainErrorCodes.StockSameWarehouse);
        }
    }

    private async Task<Guid?> BranchOfDocAsync(StockDocument d)
    {
        var id = d.Type == StockDocumentType.Receipt ? d.WarehouseToId : d.WarehouseFromId ?? d.WarehouseToId;
        return id is { } wid ? (await _warehouses.FindAsync(wid))?.BranchId : null;
    }

    private async Task SetLinesAsync(StockDocument doc, IReadOnlyList<StockLineData> lines)
    {
        doc.Lines.Clear();
        foreach (var l in lines) doc.Lines.Add(await NewLineAsync(doc, l));
        doc.RecalculateTotal();
    }

    private async Task<StockDocumentLine> NewLineAsync(StockDocument doc, StockLineData l)
    {
        var item = await AsyncExecuter.FirstOrDefaultAsync((await _items.WithDetailsAsync(i => i.Units)).Where(i => i.Id == l.ItemId))
                   ?? throw new EntityNotFoundException(typeof(Item), l.ItemId);
        if (l.Qty < 0) throw StockException.BadRequest(DentalDomainErrorCodes.StockQtyMustBePositive).WithData("name", item.Name);
        var factor = item.FactorOf(l.UnitId);
        var qty = l.Qty * factor;
        // Цена вводится за единицу ввода — храним себестоимость за базовую единицу.
        var unitCost = l.UnitCost is { } c ? decimal.Round(c / factor, 4) : 0m;
        if (doc.Type is StockDocumentType.Writeoff or StockDocumentType.Transfer or StockDocumentType.ReturnToSupplier && l.UnitCost is null && l.BatchId is { } bid)
        {
            unitCost = (await _batches.GetAsync(bid)).UnitCost;
        }
        var line = new StockDocumentLine(GuidGenerator.Create(), doc.Id, item.Id)
        {
            BatchId = l.BatchId, Qty = qty, QtyInput = l.Qty, UnitId = l.UnitId, UnitCost = unitCost,
            BatchNumber = NullIfEmpty(l.BatchNumber), SerialNumber = NullIfEmpty(l.SerialNumber), ExpiresAt = l.ExpiresAt, ActualQty = l.ActualQty,
        };
        line.RecalculateTotal();
        return line;
    }

    private async Task<Dictionary<Guid, Item>> ItemsAsync(IEnumerable<Guid> ids)
    {
        var list = ids.Distinct().ToList();
        return (await _items.GetListAsync(i => list.Contains(i.Id))).ToDictionary(i => i.Id);
    }

    private async Task<string> NextNumberAsync(StockDocumentType type) =>
        $"{StockDocument.Prefix(type)}-{await _numbers.NextAsync("stock:" + type):D6}";

    private async Task SaveAsync()
    {
        if (_uow.Current is { } uow) await uow.SaveChangesAsync();
    }

    private async Task<long> GetLongSettingAsync(string name) =>
        long.TryParse(await _settings.GetOrNullAsync(name), out var v) ? v : long.MaxValue;

    private async Task<bool> GetBoolSettingAsync(string name) =>
        bool.TryParse(await _settings.GetOrNullAsync(name), out var v) && v;

    private static string FormatMoney(long tiyn) => (tiyn / 100m).ToString("#,0.##", CultureInfo.GetCultureInfo("ru-RU")) + " ₸";

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
