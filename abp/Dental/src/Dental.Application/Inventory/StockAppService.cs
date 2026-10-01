using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Permissions;
using Dental.Staff;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Dental.Inventory;

/// <summary>Складские документы, остатки, движения, карточка товара. Логика проведения — в <see cref="StockManager"/>.</summary>
[Authorize(DentalPermissions.Inventory.View)]
public class StockAppService : DentalAppService, IStockAppService
{
    private readonly StockManager _manager;
    private readonly IRepository<StockDocument, Guid> _documents;
    private readonly IRepository<StockDocumentLine, Guid> _lines;
    private readonly IRepository<StockBalance, Guid> _balances;
    private readonly IRepository<StockMovement, Guid> _movements;
    private readonly IRepository<Batch, Guid> _batches;
    private readonly IRepository<Item, Guid> _items;
    private readonly IRepository<ItemCategory, Guid> _categories;
    private readonly IRepository<ItemStockLevel, Guid> _levels;
    private readonly IRepository<Warehouse, Guid> _warehouses;
    private readonly IRepository<Supplier, Guid> _suppliers;
    private readonly IRepository<WriteoffReason, Guid> _reasons;
    private readonly IRepository<Employee, Guid> _employees;

    public StockAppService(
        StockManager manager,
        IRepository<StockDocument, Guid> documents,
        IRepository<StockDocumentLine, Guid> lines,
        IRepository<StockBalance, Guid> balances,
        IRepository<StockMovement, Guid> movements,
        IRepository<Batch, Guid> batches,
        IRepository<Item, Guid> items,
        IRepository<ItemCategory, Guid> categories,
        IRepository<ItemStockLevel, Guid> levels,
        IRepository<Warehouse, Guid> warehouses,
        IRepository<Supplier, Guid> suppliers,
        IRepository<WriteoffReason, Guid> reasons,
        IRepository<Employee, Guid> employees)
    {
        _manager = manager;
        _documents = documents;
        _lines = lines;
        _balances = balances;
        _movements = movements;
        _batches = batches;
        _items = items;
        _categories = categories;
        _levels = levels;
        _warehouses = warehouses;
        _suppliers = suppliers;
        _reasons = reasons;
        _employees = employees;
    }

    // ================= Документы =================

    public async Task<PagedResultDto<StockDocumentListItemDto>> GetListAsync(GetStockDocumentListInput input)
    {
        var q = (await _documents.GetQueryableAsync())
            .WhereIf(input.Type.HasValue, d => d.Type == input.Type)
            .WhereIf(input.Status.HasValue, d => d.Status == input.Status)
            .WhereIf(input.WarehouseId.HasValue, d => d.WarehouseFromId == input.WarehouseId || d.WarehouseToId == input.WarehouseId);
        if (input.From is { } f)
        {
            var fu = f.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(d => d.CreationTime >= fu);
        }
        if (input.To is { } t)
        {
            var tu = t.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(d => d.CreationTime < tu);
        }
        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var term = input.Filter.Trim().ToLower();
            q = q.Where(d => d.Number.ToLower().Contains(term) || (d.InvoiceNumber != null && d.InvoiceNumber.ToLower().Contains(term)) || (d.Comment != null && d.Comment.ToLower().Contains(term)));
        }
        if (!(await BranchScope.GetAsync()).AllBranches)
        {
            var allowed = (await AccessibleWarehousesAsync()).Select(w => w.Id).ToList();
            q = q.Where(d => (d.WarehouseFromId != null && allowed.Contains(d.WarehouseFromId.Value)) || (d.WarehouseToId != null && allowed.Contains(d.WarehouseToId.Value)));
        }
        var total = await AsyncExecuter.CountAsync(q);
        var docs = await AsyncExecuter.ToListAsync(q.OrderByDescending(d => d.CreationTime).Skip(input.SkipCount).Take(input.MaxResultCount));
        var ids = docs.Select(d => d.Id).ToList();
        var counts = (await AsyncExecuter.ToListAsync((await _lines.GetQueryableAsync()).Where(l => ids.Contains(l.DocumentId))
            .GroupBy(l => l.DocumentId).Select(g => new { g.Key, Count = g.Count() }))).ToDictionary(x => x.Key, x => x.Count);
        var names = await NamesAsync(docs);
        return new PagedResultDto<StockDocumentListItemDto>(total, docs.Select(d => new StockDocumentListItemDto
        {
            Id = d.Id, Type = d.Type, Number = d.Number, Status = d.Status,
            WarehouseFromName = d.WarehouseFromId is { } wf ? names.Warehouses.GetValueOrDefault(wf) : null,
            WarehouseToName = d.WarehouseToId is { } wt ? names.Warehouses.GetValueOrDefault(wt) : null,
            SupplierName = d.SupplierId is { } s ? names.Suppliers.GetValueOrDefault(s) : null,
            ReasonName = d.ReasonId is { } r ? names.Reasons.GetValueOrDefault(r) : null,
            CreationTime = d.CreationTime, PostedAt = d.PostedAt, TotalCost = d.TotalCost, LinesCount = counts.GetValueOrDefault(d.Id), Comment = d.Comment,
        }).ToList());
    }

    public async Task<StockDocumentDto> GetAsync(Guid id)
    {
        var doc = await _manager.GetWithLinesAsync(id);
        await EnsureDocAccessAsync(doc);
        return await MapAsync(doc);
    }

    public async Task<StockDocumentDto> CreateAsync(CreateStockDocumentDto input)
    {
        await EnsureTypePermissionAsync(input.Type);
        await EnsureWarehouseAccessAsync(input.Type == StockDocumentType.Transfer ? input.WarehouseFromId : input.WarehouseFromId ?? input.WarehouseToId);
        var doc = await _manager.CreateAsync(input.Type, ToHeader(input, input.PurchaseOrderId), input.Lines?.Select(ToLine).ToList());
        return await MapAsync(doc);
    }

    public async Task<StockDocumentDto> UpdateAsync(Guid id, UpdateStockDocumentDto input)
    {
        var doc = await _manager.GetWithLinesAsync(id);
        await EnsureDocAccessAsync(doc);
        await EnsureTypePermissionAsync(doc.Type);
        SetStamp(doc, input.ConcurrencyStamp);
        if (doc.Type != StockDocumentType.Inventory)
        {
            var from = input.WarehouseFromId ?? doc.WarehouseFromId;
            var to = input.WarehouseToId ?? doc.WarehouseToId;
            await EnsureWarehouseAccessAsync(doc.Type == StockDocumentType.Transfer ? from : from ?? to);
        }
        await _manager.UpdateAsync(doc, ToHeader(input, null), input.Lines?.Select(ToLine).ToList());
        return await MapAsync(doc);
    }

    public async Task<StockDocumentDto> PostAsync(Guid id, StockActionDto input)
    {
        var doc = await _manager.GetWithLinesAsync(id);
        await EnsureDocAccessAsync(doc);
        await AuthorizationService.CheckAsync(doc.Type switch
        {
            StockDocumentType.Receipt => DentalPermissions.Inventory.Receive,
            StockDocumentType.Transfer => DentalPermissions.Inventory.TransferCreate,
            StockDocumentType.Inventory => DentalPermissions.Inventory.CountApprove,
            _ => DentalPermissions.Inventory.Writeoff,
        });
        SetStamp(doc, input.ConcurrencyStamp);
        var counts = input.Counts?.ToDictionary(c => c.LineId, c => c.ActualQty);
        doc = await _manager.PostAsync(id, counts);
        return await MapAsync(doc);
    }

    public async Task<StockDocumentDto> ReceiveAsync(Guid id, ReceiveTransferDto input)
    {
        await AuthorizationService.CheckAsync(DentalPermissions.Inventory.TransferReceive);
        var doc = await _manager.GetWithLinesAsync(id);
        if (doc.WarehouseToId is { } to)
        {
            var w = await _warehouses.GetAsync(to);
            if (w.BranchId is { } b) await BranchScope.EnsureCanAccessAsync(b);
        }
        SetStamp(doc, input.ConcurrencyStamp);
        doc = await _manager.ReceiveAsync(id, input.Lines?.ToDictionary(l => l.LineId, l => l.ActualQty), input.Comment);
        return await MapAsync(doc);
    }

    public async Task<StockDocumentDto> CancelAsync(Guid id, StockActionDto input)
    {
        var doc = await _manager.GetWithLinesAsync(id);
        await EnsureDocAccessAsync(doc);
        if (doc.Type == StockDocumentType.VisitConsumption)
        {
            throw new StockException(DentalDomainErrorCodes.StockDocumentInvalidState);
        }
        await EnsureTypePermissionAsync(doc.Type);
        SetStamp(doc, input.ConcurrencyStamp);
        doc = await _manager.CancelAsync(id, input.Comment);
        return await MapAsync(doc);
    }

    // ================= Остатки, движения, карточка =================

    public async Task<ListResultDto<StockBalanceRowDto>> GetBalancesAsync(GetBalancesInput input)
    {
        var warehouses = await AccessibleWarehousesAsync();
        var whIds = input.WarehouseId is { } w ? warehouses.Where(x => x.Id == w).Select(x => x.Id).ToList() : warehouses.Select(x => x.Id).ToList();
        var whNames = warehouses.ToDictionary(x => x.Id, x => x.Name);
        var balances = await _balances.GetListAsync(b => whIds.Contains(b.WarehouseId) && b.Qty != 0);
        var batchIds = balances.Where(b => b.BatchId != null).Select(b => b.BatchId!.Value).Distinct().ToList();
        var batches = (await _batches.GetListAsync(b => batchIds.Contains(b.Id))).ToDictionary(b => b.Id);
        var items = (await _items.GetListAsync()).ToDictionary(i => i.Id);
        var categories = (await _categories.GetListAsync()).ToDictionary(c => c.Id, c => c.Name);
        var categoryIds = input.CategoryId is { } cid ? CategoryWithChildren(cid, (await _categories.GetListAsync()).ToDictionary(c => c.Id, c => c.ParentId)) : null;
        var levels = (await _levels.GetListAsync(l => whIds.Contains(l.WarehouseId))).ToDictionary(l => (l.ItemId, l.WarehouseId));
        var today = DateOnly.FromDateTime(Clock.Now);
        var soon = today.AddDays(input.ExpiringDays ?? InventoryConsts.DefaultExpiringDays);
        var term = input.Filter?.Trim();

        bool Matches(Item i) =>
            (categoryIds is null || (i.CategoryId is { } c && categoryIds.Contains(c)))
            && (string.IsNullOrEmpty(term) || i.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || i.Sku.Contains(term, StringComparison.OrdinalIgnoreCase));

        var result = balances.Where(b => items.ContainsKey(b.ItemId) && Matches(items[b.ItemId]))
            .GroupBy(b => (b.WarehouseId, b.ItemId)).Select(g =>
            {
                var item = items[g.Key.ItemId];
                var rows = g.Select(b => (b.Qty, Cost: b.BatchId is { } id && batches.TryGetValue(id, out var bt) ? bt.UnitCost : b.AvgCost,
                    Expires: b.BatchId is { } id2 && batches.TryGetValue(id2, out var bt2) ? bt2.ExpiresAt : null)).ToList();
                var qty = rows.Sum(x => x.Qty);
                var amount = rows.Sum(x => x.Qty * x.Cost);
                var level = levels.GetValueOrDefault((item.Id, g.Key.WarehouseId));
                var nearest = rows.Where(x => x.Qty > 0 && x.Expires != null).Select(x => x.Expires).Min();
                return new StockBalanceRowDto
                {
                    WarehouseId = g.Key.WarehouseId, WarehouseName = whNames.GetValueOrDefault(g.Key.WarehouseId, "?"), ItemId = item.Id, ItemName = item.Name,
                    ItemSku = item.Sku, CategoryId = item.CategoryId, CategoryName = item.CategoryId is { } c ? categories.GetValueOrDefault(c) : null,
                    BaseUnit = item.BaseUnit, Qty = qty, AvgCost = qty != 0 ? decimal.Round(amount / qty, 4) : 0, Amount = MoneyMath.Round(amount),
                    MinQty = level?.MinQty, OptimalQty = level?.OptimalQty, NearestExpiry = nearest,
                    BelowMin = level is not null && qty < level.MinQty,
                    Expiring = nearest is not null && nearest <= soon && nearest >= today,
                    Expired = nearest is not null && nearest < today,
                };
            }).ToList();

        // Нулевые остатки ниже минимума тоже важны для «ниже минимума».
        if (input.BelowMin)
        {
            foreach (var l in levels.Values.Where(l => l.MinQty > 0 && !result.Any(r => r.ItemId == l.ItemId && r.WarehouseId == l.WarehouseId)))
            {
                if (!items.TryGetValue(l.ItemId, out var item) || !Matches(item)) continue;
                result.Add(new StockBalanceRowDto
                {
                    WarehouseId = l.WarehouseId, WarehouseName = whNames.GetValueOrDefault(l.WarehouseId, "?"), ItemId = item.Id, ItemName = item.Name, ItemSku = item.Sku,
                    CategoryId = item.CategoryId, CategoryName = item.CategoryId is { } c ? categories.GetValueOrDefault(c) : null, BaseUnit = item.BaseUnit,
                    MinQty = l.MinQty, OptimalQty = l.OptimalQty, BelowMin = true,
                });
            }
            result = result.Where(r => r.BelowMin).ToList();
        }
        if (input.ExpiringDays is not null) result = result.Where(r => r.Expiring || r.Expired).ToList();
        return new ListResultDto<StockBalanceRowDto>(result.OrderBy(r => r.WarehouseName).ThenBy(r => r.ItemName).ToList());
    }

    public async Task<PagedResultDto<StockMovementRowDto>> GetMovementsAsync(GetMovementsInput input)
    {
        var whIds = (await AccessibleWarehousesAsync()).Select(w => w.Id).ToList();
        var q = (await _movements.GetQueryableAsync()).Where(m => whIds.Contains(m.WarehouseId))
            .WhereIf(input.ItemId.HasValue, m => m.ItemId == input.ItemId)
            .WhereIf(input.WarehouseId.HasValue, m => m.WarehouseId == input.WarehouseId)
            .WhereIf(input.From.HasValue, m => m.MovedAt >= input.From)
            .WhereIf(input.To.HasValue, m => m.MovedAt < input.To);
        var total = await AsyncExecuter.CountAsync(q);
        var page = await AsyncExecuter.ToListAsync(q.OrderByDescending(m => m.MovedAt).ThenByDescending(m => m.CreationTime)
            .Skip(input.SkipCount).Take(Math.Clamp(input.MaxResultCount, 1, 500)));
        return new PagedResultDto<StockMovementRowDto>(total, await MapMovementsAsync(page));
    }

    public async Task<ItemCardDto> GetItemCardAsync(Guid itemId)
    {
        var item = await AsyncExecuter.FirstOrDefaultAsync((await _items.WithDetailsAsync(i => i.Units)).Where(i => i.Id == itemId))
                   ?? throw new Volo.Abp.Domain.Entities.EntityNotFoundException(typeof(Item), itemId);
        var warehouses = await AccessibleWarehousesAsync();
        var names = warehouses.ToDictionary(w => w.Id, w => w.Name);
        var whIds = names.Keys.ToList();
        var balances = await _balances.GetListAsync(b => b.ItemId == itemId && whIds.Contains(b.WarehouseId) && b.Qty != 0);
        var batchIds = balances.Where(b => b.BatchId != null).Select(b => b.BatchId!.Value).ToList();
        var batches = (await _batches.GetListAsync(b => batchIds.Contains(b.Id))).ToDictionary(b => b.Id);
        var rows = balances.Select(b =>
        {
            var bt = b.BatchId is { } id && batches.TryGetValue(id, out var x) ? x : null;
            return new ItemCardBatchDto
            {
                BatchId = b.BatchId, BatchNumber = bt?.BatchNumber, SerialNumber = bt?.SerialNumber, ExpiresAt = bt?.ExpiresAt, WarehouseId = b.WarehouseId,
                WarehouseName = names.GetValueOrDefault(b.WarehouseId, "?"), Qty = b.Qty, UnitCost = bt?.UnitCost ?? b.AvgCost,
            };
        }).OrderBy(b => b.WarehouseName).ThenBy(b => b.ExpiresAt).ToList();
        var levels = (await _levels.GetListAsync(l => l.ItemId == itemId)).Where(l => names.ContainsKey(l.WarehouseId))
            .Select(l => new StockLevelDto { WarehouseId = l.WarehouseId, WarehouseName = names[l.WarehouseId], MinQty = l.MinQty, OptimalQty = l.OptimalQty })
            .OrderBy(l => l.WarehouseName).ToList();
        var movements = await GetMovementsAsync(new GetMovementsInput { ItemId = itemId, MaxResultCount = 200 });
        var categories = (await _categories.GetListAsync()).ToDictionary(c => c.Id, c => c.Name);
        return new ItemCardDto
        {
            Item = InventoryCatalogAppService.ToDto(item, categories),
            TotalQty = rows.Sum(b => b.Qty),
            TotalAmount = MoneyMath.Round(rows.Sum(b => b.Qty * b.UnitCost)),
            Balances = rows,
            Levels = levels,
            Movements = movements.Items.ToList(),
        };
    }

    public async Task<ListResultDto<AvailableBatchDto>> GetAvailableBatchesAsync(Guid warehouseId, Guid itemId)
    {
        await EnsureWarehouseAccessAsync(warehouseId);
        var balances = await _balances.GetListAsync(b => b.WarehouseId == warehouseId && b.ItemId == itemId && b.Qty > 0);
        var batchIds = balances.Where(b => b.BatchId != null).Select(b => b.BatchId!.Value).ToList();
        var batches = (await _batches.GetListAsync(b => batchIds.Contains(b.Id))).ToDictionary(b => b.Id);
        var rows = balances.Select(b =>
        {
            var bt = b.BatchId is { } id && batches.TryGetValue(id, out var x) ? x : null;
            return new AvailableBatchDto
            {
                BatchId = b.BatchId, BatchNumber = bt?.BatchNumber, SerialNumber = bt?.SerialNumber, ExpiresAt = bt?.ExpiresAt, Qty = b.Qty,
                UnitCost = bt?.UnitCost ?? b.AvgCost,
            };
        }).OrderBy(b => b.ExpiresAt is null ? 1 : 0).ThenBy(b => b.ExpiresAt).ToList();
        return new ListResultDto<AvailableBatchDto>(rows);
    }

    [Authorize(DentalPermissions.Inventory.CountApprove)]
    public async Task<int> RebuildBalancesAsync() => await _manager.RebuildBalancesAsync();

    // ================= Вспомогательное =================

    private async Task<List<Warehouse>> AccessibleWarehousesAsync()
    {
        var scope = await BranchScope.GetAsync();
        return (await _warehouses.GetListAsync())
            .Where(w => w.BranchId is null || scope.AllBranches || scope.Contains(w.BranchId.Value))
            .OrderBy(w => w.Name).ToList();
    }

    private async Task EnsureWarehouseAccessAsync(Guid? warehouseId)
    {
        if (warehouseId is not { } id) return;
        var w = await _warehouses.GetAsync(id);
        if (w.BranchId is { } b) await BranchScope.EnsureCanAccessAsync(b);
    }

    private async Task EnsureDocAccessAsync(StockDocument d)
    {
        var scope = await BranchScope.GetAsync();
        if (scope.AllBranches) return;
        var allowed = (await AccessibleWarehousesAsync()).Select(w => w.Id).ToHashSet();
        if ((d.WarehouseFromId is { } f && allowed.Contains(f)) || (d.WarehouseToId is { } t && allowed.Contains(t))) return;
        throw new BusinessException(DentalDomainErrorCodes.BranchAccessDenied);
    }

    private async Task EnsureTypePermissionAsync(StockDocumentType type) =>
        await AuthorizationService.CheckAsync(type switch
        {
            StockDocumentType.Receipt => DentalPermissions.Inventory.Receive,
            StockDocumentType.Transfer => DentalPermissions.Inventory.TransferCreate,
            StockDocumentType.Writeoff or StockDocumentType.ReturnToSupplier => DentalPermissions.Inventory.Writeoff,
            StockDocumentType.Inventory => DentalPermissions.Inventory.Count,
            _ => DentalPermissions.Inventory.View,
        });

    /// <summary>Клиент шлёт ConcurrencyStamp загруженной версии — ABP сравнит его при сохранении (409 при конфликте).</summary>
    private static void SetStamp(StockDocument doc, string? stamp)
    {
        if (!string.IsNullOrEmpty(stamp)) doc.ConcurrencyStamp = stamp;
    }

    private static HashSet<Guid> CategoryWithChildren(Guid root, IReadOnlyDictionary<Guid, Guid?> parents)
    {
        var result = new HashSet<Guid> { root };
        bool added;
        do
        {
            added = false;
            foreach (var (id, parent) in parents)
            {
                if (parent is { } p && result.Contains(p) && result.Add(id)) added = true;
            }
        } while (added);
        return result;
    }

    private static StockDocumentHeader ToHeader(StockDocumentHeaderDto d, Guid? purchaseOrderId) =>
        new(d.WarehouseFromId, d.WarehouseToId, d.SupplierId, purchaseOrderId, d.InvoiceNumber, d.InvoiceDate, d.ReasonId, d.Comment);

    private static StockLineData ToLine(StockLineInputDto l) =>
        new(l.ItemId, l.Qty, l.UnitId, l.BatchId, l.UnitCost, l.BatchNumber, l.SerialNumber, l.ExpiresAt, l.ActualQty);

    private sealed record DocNames(Dictionary<Guid, string> Warehouses, Dictionary<Guid, string> Suppliers, Dictionary<Guid, string> Reasons, Dictionary<Guid, string> Users);

    private async Task<DocNames> NamesAsync(IReadOnlyList<StockDocument> docs)
    {
        var wh = docs.SelectMany(d => new[] { d.WarehouseFromId, d.WarehouseToId }).Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
        var sp = docs.Where(d => d.SupplierId != null).Select(d => d.SupplierId!.Value).Distinct().ToList();
        var rs = docs.Where(d => d.ReasonId != null).Select(d => d.ReasonId!.Value).Distinct().ToList();
        var us = docs.SelectMany(d => new[] { d.CreatorId, d.PostedBy }).Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
        return new DocNames(
            (await _warehouses.GetListAsync(w => wh.Contains(w.Id))).ToDictionary(w => w.Id, w => w.Name),
            (await _suppliers.GetListAsync(s => sp.Contains(s.Id))).ToDictionary(s => s.Id, s => s.Name),
            (await _reasons.GetListAsync(r => rs.Contains(r.Id))).ToDictionary(r => r.Id, r => r.Name),
            (await _employees.GetListAsync(e => us.Contains(e.UserId))).GroupBy(e => e.UserId).ToDictionary(g => g.Key, g => g.First().FullName));
    }

    private async Task<StockDocumentDto> MapAsync(StockDocument d)
    {
        var names = await NamesAsync([d]);
        var itemIds = d.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = (await AsyncExecuter.ToListAsync((await _items.WithDetailsAsync(i => i.Units)).Where(i => itemIds.Contains(i.Id)))).ToDictionary(i => i.Id);
        var lines = d.Lines.OrderBy(l => items.GetValueOrDefault(l.ItemId)?.Name).ThenBy(l => l.ExpiresAt).Select(l =>
        {
            var item = items.GetValueOrDefault(l.ItemId);
            return new StockDocumentLineDto
            {
                Id = l.Id, ItemId = l.ItemId, ItemName = item?.Name ?? "?", ItemSku = item?.Sku ?? "", BaseUnit = item?.BaseUnit ?? BaseUnit.Pcs, BatchId = l.BatchId,
                BatchNumber = l.BatchNumber, SerialNumber = l.SerialNumber, ExpiresAt = l.ExpiresAt, Qty = l.Qty, QtyInput = l.QtyInput, UnitId = l.UnitId,
                UnitName = l.UnitId is { } u ? item?.Units.FirstOrDefault(x => x.Id == u)?.UnitName : null, UnitCost = l.UnitCost, TotalCost = l.TotalCost,
                ExpectedQty = l.ExpectedQty, ActualQty = l.ActualQty,
            };
        }).ToList();
        return new StockDocumentDto
        {
            Id = d.Id, Type = d.Type, Number = d.Number, Status = d.Status, BranchId = d.BranchId,
            WarehouseFromId = d.WarehouseFromId, WarehouseFromName = d.WarehouseFromId is { } f ? names.Warehouses.GetValueOrDefault(f) : null,
            WarehouseToId = d.WarehouseToId, WarehouseToName = d.WarehouseToId is { } t ? names.Warehouses.GetValueOrDefault(t) : null,
            SupplierId = d.SupplierId, SupplierName = d.SupplierId is { } s ? names.Suppliers.GetValueOrDefault(s) : null,
            PurchaseOrderId = d.PurchaseOrderId, VisitId = d.VisitId, SourceDocumentId = d.SourceDocumentId, InvoiceNumber = d.InvoiceNumber, InvoiceDate = d.InvoiceDate,
            ReasonId = d.ReasonId, ReasonName = d.ReasonId is { } r ? names.Reasons.GetValueOrDefault(r) : null, Comment = d.Comment,
            CreationTime = d.CreationTime, CreatorName = d.CreatorId is { } cb ? names.Users.GetValueOrDefault(cb) : null,
            PostedAt = d.PostedAt, PostedByName = d.PostedBy is { } pb ? names.Users.GetValueOrDefault(pb) : null, ReceivedAt = d.ReceivedAt,
            SnapshotAt = d.SnapshotAt, TotalCost = d.TotalCost, ConcurrencyStamp = d.ConcurrencyStamp, Lines = lines,
        };
    }

    private async Task<List<StockMovementRowDto>> MapMovementsAsync(List<StockMovement> page)
    {
        var whIds = page.Select(m => m.WarehouseId).Distinct().ToList();
        var itemIds = page.Select(m => m.ItemId).Distinct().ToList();
        var batchIds = page.Where(m => m.BatchId != null).Select(m => m.BatchId!.Value).Distinct().ToList();
        var docIds = page.Select(m => m.DocumentId).Distinct().ToList();
        var wh = (await _warehouses.GetListAsync(w => whIds.Contains(w.Id))).ToDictionary(w => w.Id, w => w.Name);
        var items = (await _items.GetListAsync(i => itemIds.Contains(i.Id))).ToDictionary(i => i.Id, i => i.Name);
        var batches = (await _batches.GetListAsync(b => batchIds.Contains(b.Id))).ToDictionary(b => b.Id, b => b.BatchNumber ?? b.SerialNumber);
        var docs = (await _documents.GetListAsync(d => docIds.Contains(d.Id))).ToDictionary(d => d.Id, d => d.Number);
        return page.Select(m => new StockMovementRowDto
        {
            Id = m.Id, MovedAt = m.MovedAt, WarehouseId = m.WarehouseId, WarehouseName = wh.GetValueOrDefault(m.WarehouseId, "?"), ItemId = m.ItemId,
            ItemName = items.GetValueOrDefault(m.ItemId, "?"), BatchId = m.BatchId, BatchNumber = m.BatchId is { } b ? batches.GetValueOrDefault(b) : null,
            Qty = m.Qty, UnitCost = m.UnitCost, DocumentType = m.DocumentType, DocumentId = m.DocumentId, DocumentNumber = docs.GetValueOrDefault(m.DocumentId, ""),
        }).ToList();
    }
}
