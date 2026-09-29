using System.Globalization;
using Dental.Application.Common;
using Dental.Application.Inventory;
using Dental.Application.Permissions;
using Dental.Domain.Common;
using Dental.Domain.Inventory;
using Dental.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace Dental.Application.Purchasing;

/// <summary>
/// Заявки на пополнение (ручные и автоматические), сводная потребность сети и её обработка
/// (перемещение с центрального склада, заказ поставщику, отклонение). SPEC §8.5.
/// </summary>
public sealed class PurchaseRequestService(
    IAppDbContext db,
    ICurrentUser user,
    StockService stock,
    PurchaseOrderService orders,
    INotificationService notifications,
    TimeProvider clock)
{
    private static readonly PurchaseRequestStatus[] Open = [PurchaseRequestStatus.Draft, PurchaseRequestStatus.Submitted];

    // ================= Заявки =================

    public async Task<PagedResult<PurchaseRequestListItem>> ListAsync(PurchaseRequestStatus? status, Guid? branchId, Guid? warehouseId, PurchaseRequestSource? source,
        PageQuery page, CancellationToken ct)
    {
        var q = VisibleRequests();
        if (status is { } s) q = q.Where(r => r.Status == s);
        if (branchId is { } b) q = q.Where(r => r.BranchId == b);
        if (warehouseId is { } w) q = q.Where(r => r.WarehouseId == w);
        if (source is { } src) q = q.Where(r => r.Source == src);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(r => r.CreatedAt).Skip((page.SafePage - 1) * page.SafeSize).Take(page.SafeSize)
            .Select(r => new { r, Count = r.Lines.Count, Qty = r.Lines.Sum(l => (decimal?)l.Qty) ?? 0 }).ToListAsync(ct);
        var names = await NamesAsync(rows.Select(x => x.r).ToList(), ct);
        var items = rows.Select(x => new PurchaseRequestListItem(x.r.Id, x.r.BranchId, x.r.BranchId is { } bb ? names.Branches.GetValueOrDefault(bb) : null,
            x.r.WarehouseId, names.Warehouses.GetValueOrDefault(x.r.WarehouseId, "?"), x.r.Status, x.r.Source, x.r.Comment, x.r.CreatedAt,
            x.r.CreatedBy is { } cb ? names.Users.GetValueOrDefault(cb) : null, x.r.UpdatedAt, x.Count, x.Qty)).ToList();
        return new PagedResult<PurchaseRequestListItem>(items, total, page.SafePage, page.SafeSize);
    }

    public async Task<PurchaseRequestDto> GetAsync(Guid id, CancellationToken ct)
    {
        var r = await VisibleRequests().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Заявка");
        return await MapAsync(r, ct);
    }

    public async Task<PurchaseRequestDto> CreateAsync(CreatePurchaseRequestRequest r, CancellationToken ct)
    {
        var warehouse = await GetWarehouseAsync(r.WarehouseId, ct);
        var request = new PurchaseRequest
        {
            BranchId = warehouse.BranchId,
            WarehouseId = warehouse.Id,
            Source = PurchaseRequestSource.Manual,
            Status = r.Submit ? PurchaseRequestStatus.Submitted : PurchaseRequestStatus.Draft,
            Comment = r.Comment.NullIfEmpty(),
        };
        await SetLinesAsync(request, r.Lines, ct);
        db.PurchaseRequests.Add(request);
        if (r.Submit) await NotifySubmittedAsync(request, warehouse, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(request.Id, ct);
    }

    public async Task<PurchaseRequestDto> UpdateAsync(Guid id, UpdatePurchaseRequestRequest r, CancellationToken ct)
    {
        var request = await LoadAsync(id, ct);
        if (request.Status != PurchaseRequestStatus.Draft) throw AppException.Conflict(ErrorCodes.DocumentNotEditable, "Редактировать можно только черновик заявки");
        if (r.Comment is not null) request.Comment = r.Comment.NullIfEmpty();
        if (r.Lines is not null) await SetLinesAsync(request, r.Lines, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<PurchaseRequestDto> SubmitAsync(Guid id, CancellationToken ct)
    {
        var request = await LoadAsync(id, ct);
        if (request.Status != PurchaseRequestStatus.Draft) throw AppException.Conflict(ErrorCodes.DocumentInvalidState, "Отправить можно только черновик заявки");
        if (request.Lines.Count == 0 || request.Lines.All(l => l.Qty <= 0)) throw AppException.BadRequest(ErrorCodes.LinesRequired, "В заявке нет позиций");
        request.Status = PurchaseRequestStatus.Submitted;
        var warehouse = await db.Warehouses.AsNoTracking().FirstAsync(w => w.Id == request.WarehouseId, ct);
        await NotifySubmittedAsync(request, warehouse, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Отклонение заявки. Отправленную отклоняет закупщик сети; свой черновик филиал может закрыть сам.</summary>
    public async Task<PurchaseRequestDto> RejectAsync(Guid id, PurchaseRequestCommentRequest r, CancellationToken ct)
    {
        var request = await LoadAsync(id, ct);
        if (!Open.Contains(request.Status)) throw AppException.Conflict(ErrorCodes.DocumentInvalidState, "Заявка уже обработана");
        if (request.Status == PurchaseRequestStatus.Submitted) user.EnsurePermission(Perm.Purchase.OrderCreate);
        if (r.Comment.NullIfEmpty() is not { } comment) throw AppException.BadRequest(ErrorCodes.CommentRequired, "Укажите причину отклонения");
        request.Status = PurchaseRequestStatus.Rejected;
        request.Comment = AppendComment(request.Comment, "Отклонено: " + comment);
        if (request.CreatedBy is { } author && author != user.UserId)
            notifications.Notify(author, "purchase_request", "Заявка на пополнение отклонена", comment, nameof(PurchaseRequest), request.Id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Отметить заявку обработанной вручную (например, закуплено вне системы).</summary>
    public async Task<PurchaseRequestDto> MarkProcessedAsync(Guid id, PurchaseRequestCommentRequest r, CancellationToken ct)
    {
        var request = await LoadAsync(id, ct);
        if (request.Status != PurchaseRequestStatus.Submitted) throw AppException.Conflict(ErrorCodes.DocumentInvalidState, "Отметить обработанной можно только отправленную заявку");
        request.Status = PurchaseRequestStatus.Processed;
        request.Comment = AppendComment(request.Comment, "Обработано вручную" + (r.Comment.NullIfEmpty() is { } c ? ": " + c : ""));
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ================= Автозаявки =================

    /// <summary>Автозаявки по кнопке: по складам, доступным пользователю (или по одному складу).</summary>
    public async Task<GenerateRequestsResult> GenerateAsync(Guid? warehouseId, CancellationToken ct)
    {
        var warehouses = await stock.AccessibleWarehousesAsync(ct);
        if (warehouseId is { } w)
        {
            warehouses = warehouses.Where(x => x.Id == w).ToList();
            if (warehouses.Count == 0) throw AppException.NotFound("Склад");
        }
        return await GenerateForAsync(warehouses, ct);
    }

    /// <summary>Автозаявки для всех складов организации (ночная задача, тенант задан явно).</summary>
    public async Task<GenerateRequestsResult> GenerateAllAsync(CancellationToken ct)
    {
        var warehouses = await db.Warehouses.AsNoTracking().Where(w => w.DeletedAt == null).ToListAsync(ct);
        return await GenerateForAsync(warehouses, ct);
    }

    /// <summary>
    /// Для каждого склада, где остаток &lt; min_qty: количество = optimal − остаток − в пути − уже заказано (Replenishment.SuggestedQty).
    /// «В пути» — перемещения на склад в черновике и в пути; «уже заказано» — неполученный остаток открытых заказов поставщикам
    /// и открытые ручные заявки. На склад — одна открытая автозаявка: при повторном запуске она обновляется, а если потребность отпала — закрывается.
    /// </summary>
    private async Task<GenerateRequestsResult> GenerateForAsync(IReadOnlyList<Warehouse> warehouses, CancellationToken ct)
    {
        var whIds = warehouses.Select(w => w.Id).ToList();
        var levels = await (from l in db.ItemStockLevels.AsNoTracking()
                            join i in db.Items.AsNoTracking() on l.ItemId equals i.Id
                            where whIds.Contains(l.WarehouseId) && l.MinQty > 0 && i.IsActive && i.DeletedAt == null
                            select l).ToListAsync(ct);
        var balances = await db.StockBalances.AsNoTracking().Where(b => whIds.Contains(b.WarehouseId))
            .GroupBy(b => new { b.WarehouseId, b.ItemId }).Select(g => new { g.Key.WarehouseId, g.Key.ItemId, Qty = g.Sum(x => x.Qty) })
            .ToDictionaryAsync(x => (x.WarehouseId, x.ItemId), x => x.Qty, ct);
        var inTransit = await (from d in db.StockDocuments.AsNoTracking()
                               join l in db.StockDocumentLines.AsNoTracking() on d.Id equals l.DocumentId
                               where d.Type == StockDocumentType.Transfer && (d.Status == StockDocumentStatus.Draft || d.Status == StockDocumentStatus.InTransit)
                                     && d.WarehouseToId != null && whIds.Contains(d.WarehouseToId.Value)
                               group l by new { To = d.WarehouseToId!.Value, l.ItemId } into g
                               select new { g.Key.To, g.Key.ItemId, Qty = g.Sum(x => x.Qty) }).ToDictionaryAsync(x => (x.To, x.ItemId), x => x.Qty, ct);
        var openOrderStatuses = new[] { PurchaseOrderStatus.Draft, PurchaseOrderStatus.PendingApproval, PurchaseOrderStatus.Sent, PurchaseOrderStatus.PartiallyReceived };
        var ordered = await (from o in db.PurchaseOrders.AsNoTracking()
                             join l in db.PurchaseOrderLines.AsNoTracking() on o.Id equals l.OrderId
                             where openOrderStatuses.Contains(o.Status) && whIds.Contains(o.WarehouseId) && l.Qty > l.ReceivedQty
                             group l by new { o.WarehouseId, l.ItemId } into g
                             select new { g.Key.WarehouseId, g.Key.ItemId, Qty = g.Sum(x => x.Qty - x.ReceivedQty) }).ToDictionaryAsync(x => (x.WarehouseId, x.ItemId), x => x.Qty, ct);
        var manual = await (from r in db.PurchaseRequests.AsNoTracking()
                            join l in db.PurchaseRequestLines.AsNoTracking() on r.Id equals l.RequestId
                            where r.Source == PurchaseRequestSource.Manual && Open.Contains(r.Status) && whIds.Contains(r.WarehouseId)
                            group l by new { r.WarehouseId, l.ItemId } into g
                            select new { g.Key.WarehouseId, g.Key.ItemId, Qty = g.Sum(x => x.Qty) }).ToDictionaryAsync(x => (x.WarehouseId, x.ItemId), x => x.Qty, ct);
        var openAuto = await db.PurchaseRequests.Include(r => r.Lines)
            .Where(r => r.Source == PurchaseRequestSource.Auto && Open.Contains(r.Status) && whIds.Contains(r.WarehouseId))
            .OrderBy(r => r.CreatedAt).ToListAsync(ct);

        int created = 0, updated = 0, closed = 0, lineCount = 0;
        var ids = new List<Guid>();
        var createdRequests = new List<(PurchaseRequest Request, Warehouse Warehouse)>();
        foreach (var w in warehouses)
        {
            var lines = new List<PurchaseRequestLine>();
            foreach (var level in levels.Where(l => l.WarehouseId == w.Id))
            {
                var key = (w.Id, level.ItemId);
                var qty = balances.GetValueOrDefault(key);
                var suggested = Replenishment.SuggestedQty(qty, level.MinQty, level.OptimalQty, inTransit.GetValueOrDefault(key),
                    ordered.GetValueOrDefault(key) + manual.GetValueOrDefault(key));
                if (suggested <= 0) continue;
                lines.Add(new PurchaseRequestLine { ItemId = level.ItemId, Qty = suggested, CurrentQty = qty, MinQty = level.MinQty, OptimalQty = level.OptimalQty });
            }

            var existing = openAuto.FirstOrDefault(r => r.WarehouseId == w.Id);
            if (lines.Count == 0)
            {
                if (existing is null) continue;
                existing.Status = PurchaseRequestStatus.Rejected;
                existing.Comment = AppendComment(existing.Comment, "Закрыта автоматически: остатки в норме");
                closed++;
                continue;
            }
            lineCount += lines.Count;
            if (existing is not null)
            {
                ids.Add(existing.Id);
                var same = existing.Lines.Count == lines.Count
                           && lines.All(n => existing.Lines.Any(o => o.ItemId == n.ItemId && o.Qty == n.Qty && o.CurrentQty == n.CurrentQty));
                if (same) continue;
                existing.Lines.Clear();
                foreach (var l in lines) { l.RequestId = existing.Id; existing.Lines.Add(l); }
                existing.UpdatedAt = clock.GetUtcNow();
                updated++;
                continue;
            }
            var request = new PurchaseRequest
            {
                BranchId = w.BranchId, WarehouseId = w.Id, Source = PurchaseRequestSource.Auto, Status = PurchaseRequestStatus.Draft,
                Comment = "Сформирована автоматически: остатки ниже минимума",
            };
            foreach (var l in lines) { l.RequestId = request.Id; request.Lines.Add(l); }
            db.PurchaseRequests.Add(request);
            createdRequests.Add((request, w));
            ids.Add(request.Id);
            created++;
        }

        foreach (var (request, w) in createdRequests)
        {
            await notifications.NotifyByPermissionAsync(Perm.Purchase.RequestCreate, "purchase_request", $"Автозаявка на пополнение: {w.Name}",
                $"Позиций ниже минимума: {request.Lines.Count}. Проверьте и отправьте заявку.", nameof(PurchaseRequest), request.Id, w.BranchId, ct);
        }
        await db.SaveChangesAsync(ct);
        return new GenerateRequestsResult(created, updated, closed, lineCount, ids);
    }

    // ================= Сводная потребность сети =================

    public async Task<NetworkDemandDto> DemandAsync(Guid? centralWarehouseId, CancellationToken ct)
    {
        var requests = await VisibleRequests().Include(r => r.Lines).Where(r => r.Status == PurchaseRequestStatus.Submitted).ToListAsync(ct);
        var centrals = await CentralWarehousesAsync(ct);
        var centralId = centralWarehouseId ?? centrals.FirstOrDefault()?.Id;

        var lines = requests.SelectMany(r => r.Lines.Where(l => l.Qty > 0).Select(l => (Request: r, Line: l))).ToList();
        var itemIds = lines.Select(x => x.Line.ItemId).Distinct().ToList();
        var whIds = lines.Select(x => x.Request.WarehouseId).Distinct().ToList();
        if (centralId is { } cid) whIds.Add(cid);

        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var warehouses = await db.Warehouses.AsNoTracking().Where(w => whIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, ct);
        var branchIds = warehouses.Values.Where(w => w.BranchId != null).Select(w => w.BranchId!.Value).Distinct().ToList();
        var branches = await db.Branches.AsNoTracking().Where(b => branchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var balances = await db.StockBalances.AsNoTracking().Where(b => whIds.Contains(b.WarehouseId) && itemIds.Contains(b.ItemId))
            .GroupBy(b => new { b.WarehouseId, b.ItemId }).Select(g => new { g.Key.WarehouseId, g.Key.ItemId, Qty = g.Sum(x => x.Qty) })
            .ToDictionaryAsync(x => (x.WarehouseId, x.ItemId), x => x.Qty, ct);
        var levels = await db.ItemStockLevels.AsNoTracking().Where(l => whIds.Contains(l.WarehouseId) && itemIds.Contains(l.ItemId))
            .ToDictionaryAsync(l => (l.WarehouseId, l.ItemId), ct);
        var prices = await SupplierPricesAsync(itemIds, ct);

        var rows = lines.GroupBy(x => (x.Line.ItemId, x.Request.WarehouseId)).Select(g =>
        {
            var item = items.GetValueOrDefault(g.Key.ItemId);
            var w = warehouses.GetValueOrDefault(g.Key.WarehouseId);
            var level = levels.GetValueOrDefault((g.Key.WarehouseId, g.Key.ItemId));
            var last = g.OrderByDescending(x => x.Request.UpdatedAt).First().Line;
            return new DemandRow(g.Key.ItemId, item?.Name ?? "?", item?.Sku ?? "", item?.BaseUnit.ToString() ?? "", g.Key.WarehouseId, w?.Name ?? "?", w?.BranchId,
                w?.BranchId is { } b ? branches.GetValueOrDefault(b) : null, g.Sum(x => x.Line.Qty), balances.GetValueOrDefault((g.Key.WarehouseId, g.Key.ItemId)),
                level?.MinQty ?? last.MinQty, level?.OptimalQty ?? last.OptimalQty,
                g.Select(x => x.Request.Id).Distinct().ToList(), g.Select(x => x.Line.Id).ToList());
        }).OrderBy(r => r.ItemName).ThenBy(r => r.BranchName ?? "").ToList();

        long estimated = 0;
        var itemRows = rows.GroupBy(r => r.ItemId).Select(g =>
        {
            var item = items.GetValueOrDefault(g.Key);
            var p = prices.GetValueOrDefault(g.Key) ?? [];
            var best = BestSupplier(p);
            var total = g.Sum(x => x.Qty);
            if (best is not null) estimated += (long)Math.Round(total * best.LastPrice, MidpointRounding.AwayFromZero);
            return new DemandItem(g.Key, item?.Name ?? "?", item?.Sku ?? "", item?.BaseUnit.ToString() ?? "", total,
                centralId is { } c ? balances.GetValueOrDefault((c, g.Key)) : 0, p, best?.SupplierId);
        }).OrderBy(i => i.ItemName).ToList();

        return new NetworkDemandDto(rows, itemRows, centrals.Select(c => new DemandWarehouseRef(c.Id, c.Name)).ToList(), centralId,
            requests.Count(r => r.Lines.Count > 0), estimated);
    }

    /// <summary>
    /// Обработка выбранных строк сводной потребности. Обработанные строки уходят из потребности: если выбрана вся заявка —
    /// она получает статус processed/rejected; если часть — строки переносятся в отдельную заявку с этим статусом
    /// (исходная остаётся отправленной с необработанными строками). Ничего не удаляется.
    /// </summary>
    public async Task<ProcessDemandResult> ProcessAsync(ProcessDemandRequest r, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var selected = await ResolveSelectionAsync(r, ct);
        if (selected.Count == 0) throw AppException.BadRequest(ErrorCodes.LinesRequired, "Не выбраны строки потребности");

        var transferIds = new List<Guid>();
        var orderIds = new List<Guid>();
        var numbers = new List<string>();
        string note;
        switch (r.Action)
        {
            case DemandAction.Transfer:
            {
                user.EnsurePermission(Perm.Inventory.TransferCreate);
                var centrals = await CentralWarehousesAsync(ct);
                var fromId = r.FromWarehouseId ?? centrals.FirstOrDefault()?.Id
                             ?? throw AppException.BadRequest(ErrorCodes.ValidationFailed, "В сети нет центрального склада");
                foreach (var group in selected.GroupBy(s => s.WarehouseId))
                {
                    if (group.Key == fromId) throw AppException.BadRequest(ErrorCodes.SameWarehouse, "Склад-получатель совпадает со складом-отправителем");
                    var lines = group.GroupBy(s => s.ItemId)
                        .Select(g => new StockLineInput(g.Key, g.First().Qty ?? g.Sum(x => x.Lines.Sum(l => l.Qty)), null, null, null, null, null, null, null))
                        .Where(l => l.Qty > 0).ToList();
                    if (lines.Count == 0) continue;
                    var doc = await stock.CreateAsync(new CreateStockDocumentRequest(StockDocumentType.Transfer, fromId, group.Key, null, null, null, null, null,
                        AppendComment("По заявке на пополнение", r.Comment.NullIfEmpty()), lines), ct);
                    transferIds.Add(doc.Id);
                    numbers.Add(doc.Number);
                }
                note = "Перемещение " + string.Join(", ", numbers);
                break;
            }
            case DemandAction.Order:
            {
                user.EnsurePermission(Perm.Purchase.OrderCreate);
                var prices = await SupplierPricesAsync(selected.Select(s => s.ItemId).Distinct().ToList(), ct);
                var itemNames = await ItemNamesAsync(selected.Select(s => s.ItemId), ct);
                var planned = new List<(Guid SupplierId, Guid WarehouseId, Guid ItemId, decimal Qty, long? Price)>();
                foreach (var s in selected)
                {
                    var p = prices.GetValueOrDefault(s.ItemId) ?? [];
                    var supplierId = s.SupplierId ?? r.SupplierId ?? BestSupplier(p)?.SupplierId
                                     ?? throw AppException.BadRequest(ErrorCodes.SupplierRequired, $"«{itemNames.GetValueOrDefault(s.ItemId, "?")}»: выберите поставщика",
                                         new Dictionary<string, object?> { ["itemId"] = s.ItemId });
                    var qty = s.Qty ?? s.Lines.Sum(l => l.Qty);
                    if (qty > 0) planned.Add((supplierId, r.DeliverToWarehouseId ?? s.WarehouseId, s.ItemId, qty, s.UnitPrice));
                }
                foreach (var g in planned.GroupBy(p => (p.SupplierId, p.WarehouseId)))
                {
                    var lines = g.GroupBy(x => x.ItemId).Select(x => (x.Key, x.Sum(y => y.Qty), x.Select(y => y.Price).FirstOrDefault(y => y != null))).ToList();
                    var order = await orders.NewOrderAsync(g.Key.SupplierId, g.Key.WarehouseId, null, AppendComment("По заявкам на пополнение", r.Comment.NullIfEmpty()), lines, ct);
                    orderIds.Add(order.Id);
                    numbers.Add(order.Number);
                }
                note = "Заказ поставщику " + string.Join(", ", numbers);
                break;
            }
            case DemandAction.Reject:
                user.EnsurePermission(Perm.Purchase.OrderCreate);
                note = "Отклонено: " + (r.Comment.NullIfEmpty() ?? throw AppException.BadRequest(ErrorCodes.CommentRequired, "Укажите причину отклонения"));
                break;
            default:
                throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Неизвестное действие");
        }

        var status = r.Action == DemandAction.Reject ? PurchaseRequestStatus.Rejected : PurchaseRequestStatus.Processed;
        var processed = await CloseLinesAsync(selected.SelectMany(s => s.Lines).ToList(), status, note, ct);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return new ProcessDemandResult(r.Action, transferIds, orderIds, processed, numbers);
    }

    private sealed record Selection(Guid ItemId, Guid WarehouseId, decimal? Qty, Guid? SupplierId, long? UnitPrice, List<PurchaseRequestLine> Lines);

    private async Task<List<Selection>> ResolveSelectionAsync(ProcessDemandRequest r, CancellationToken ct)
    {
        var submitted = VisibleRequests().Where(x => x.Status == PurchaseRequestStatus.Submitted);
        var result = new List<Selection>();
        if (r.RequestIds is { Count: > 0 } requestIds)
        {
            var reqs = await db.PurchaseRequests.Include(x => x.Lines).Where(x => requestIds.Contains(x.Id)).ToListAsync(ct);
            var visible = await submitted.Where(x => requestIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(ct);
            if (visible.Count != requestIds.Distinct().Count())
                throw AppException.Conflict(ErrorCodes.DocumentInvalidState, "Обработать можно только отправленные заявки");
            foreach (var g in reqs.SelectMany(x => x.Lines.Select(l => (x.WarehouseId, Line: l))).GroupBy(x => (x.Line.ItemId, x.WarehouseId)))
                result.Add(new Selection(g.Key.ItemId, g.Key.WarehouseId, null, null, null, g.Select(x => x.Line).ToList()));
        }
        foreach (var row in r.Rows ?? [])
        {
            var reqIds = await submitted.Where(x => x.WarehouseId == row.WarehouseId).Select(x => x.Id).ToListAsync(ct);
            var lines = await db.PurchaseRequestLines.Where(l => l.ItemId == row.ItemId && reqIds.Contains(l.RequestId)).ToListAsync(ct);
            if (lines.Count == 0) throw AppException.Conflict(ErrorCodes.DocumentInvalidState, "Строка потребности уже обработана. Обновите страницу.");
            if (row.Qty is < 0) throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Количество не может быть отрицательным");
            var existing = result.FirstOrDefault(s => s.ItemId == row.ItemId && s.WarehouseId == row.WarehouseId);
            if (existing is not null) result.Remove(existing);
            result.Add(new Selection(row.ItemId, row.WarehouseId, row.Qty, row.SupplierId, row.UnitPrice, lines));
        }
        return result;
    }

    /// <summary>Закрывает строки заявок: целиком выбранная заявка меняет статус, частично выбранная — делится.</summary>
    private async Task<int> CloseLinesAsync(List<PurchaseRequestLine> lines, PurchaseRequestStatus status, string note, CancellationToken ct)
    {
        var reqIds = lines.Select(l => l.RequestId).Distinct().ToList();
        var requests = await db.PurchaseRequests.Where(x => reqIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var counts = await db.PurchaseRequestLines.Where(l => reqIds.Contains(l.RequestId)).GroupBy(l => l.RequestId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        foreach (var g in lines.GroupBy(l => l.RequestId))
        {
            var request = requests[g.Key];
            var selectedIds = g.Select(l => l.Id).Distinct().ToList();
            if (selectedIds.Count >= counts.GetValueOrDefault(g.Key))
            {
                request.Status = status;
                request.Comment = AppendComment(request.Comment, note);
                continue;
            }
            var part = new PurchaseRequest
            {
                BranchId = request.BranchId, WarehouseId = request.WarehouseId, Source = request.Source, Status = status,
                Comment = AppendComment($"Часть заявки от {request.CreatedAt.UtcDateTime.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}", note),
            };
            db.PurchaseRequests.Add(part);
            foreach (var line in g.DistinctBy(l => l.Id)) line.RequestId = part.Id;
            request.UpdatedAt = clock.GetUtcNow();
        }
        return lines.DistinctBy(l => l.Id).Count();
    }

    // ================= Вспомогательное =================

    private IQueryable<PurchaseRequest> VisibleRequests()
    {
        var q = db.PurchaseRequests.AsNoTracking();
        if (!user.AllBranches) q = q.Where(r => r.BranchId == null || user.BranchIds.Contains(r.BranchId.Value));
        return q;
    }

    private async Task<PurchaseRequest> LoadAsync(Guid id, CancellationToken ct)
    {
        var request = await db.PurchaseRequests.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw AppException.NotFound("Заявка");
        if (request.BranchId is { } b) user.EnsureBranchAccess(b);
        return request;
    }

    private async Task<Warehouse> GetWarehouseAsync(Guid id, CancellationToken ct)
    {
        var w = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct) ?? throw AppException.NotFound("Склад");
        if (w.BranchId is { } b) user.EnsureBranchAccess(b);
        return w;
    }

    private async Task<List<Warehouse>> CentralWarehousesAsync(CancellationToken ct) =>
        await db.Warehouses.AsNoTracking().Where(w => w.BranchId == null && w.DeletedAt == null)
            .OrderBy(w => w.Type == WarehouseType.Central ? 0 : 1).ThenBy(w => w.CreatedAt).ToListAsync(ct);

    private async Task SetLinesAsync(PurchaseRequest request, IReadOnlyList<PurchaseRequestLineInput> input, CancellationToken ct)
    {
        var itemIds = input.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id) && i.DeletedAt == null).Select(i => i.Id).ToListAsync(ct);
        if (items.Count != itemIds.Count) throw AppException.NotFound("Товар");
        var balances = await db.StockBalances.AsNoTracking().Where(b => b.WarehouseId == request.WarehouseId && itemIds.Contains(b.ItemId))
            .GroupBy(b => b.ItemId).Select(g => new { g.Key, Qty = g.Sum(x => x.Qty) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var levels = await db.ItemStockLevels.AsNoTracking().Where(l => l.WarehouseId == request.WarehouseId && itemIds.Contains(l.ItemId))
            .ToDictionaryAsync(l => l.ItemId, ct);
        request.Lines.Clear();
        foreach (var g in input.Where(l => l.Qty > 0).GroupBy(l => l.ItemId))
        {
            var level = levels.GetValueOrDefault(g.Key);
            request.Lines.Add(new PurchaseRequestLine
            {
                RequestId = request.Id, ItemId = g.Key, Qty = g.Sum(x => x.Qty), CurrentQty = balances.GetValueOrDefault(g.Key),
                MinQty = level?.MinQty ?? 0, OptimalQty = level?.OptimalQty ?? 0,
            });
        }
    }

    private async Task NotifySubmittedAsync(PurchaseRequest request, Warehouse warehouse, CancellationToken ct) =>
        await notifications.NotifyByPermissionAsync(Perm.Purchase.OrderCreate, "purchase_request", $"Заявка на пополнение: {warehouse.Name}",
            $"Позиций: {request.Lines.Count}", nameof(PurchaseRequest), request.Id, null, ct);

    private async Task<Dictionary<Guid, IReadOnlyList<SupplierPriceDto>>> SupplierPricesAsync(IReadOnlyList<Guid> itemIds, CancellationToken ct)
    {
        var rows = await (from si in db.SupplierItems.AsNoTracking()
                          join s in db.Suppliers.AsNoTracking() on si.SupplierId equals s.Id
                          where itemIds.Contains(si.ItemId) && s.DeletedAt == null
                          select new { si.ItemId, si.SupplierId, s.Name, si.LastPrice, si.LastPriceAt }).ToListAsync(ct);
        return rows.GroupBy(r => r.ItemId).ToDictionary(g => g.Key,
            g => (IReadOnlyList<SupplierPriceDto>)g.OrderBy(x => x.LastPrice <= 0 ? long.MaxValue : x.LastPrice).ThenBy(x => x.Name)
                .Select(x => new SupplierPriceDto(x.SupplierId, x.Name, x.LastPrice, x.LastPriceAt)).ToList());
    }

    /// <summary>Лучшая цена — минимальная положительная последняя цена; если цен нет — первый поставщик позиции.</summary>
    private static SupplierPriceDto? BestSupplier(IReadOnlyList<SupplierPriceDto> prices) =>
        prices.Where(p => p.LastPrice > 0).MinBy(p => p.LastPrice) ?? (prices.Count > 0 ? prices[0] : null);

    private async Task<Dictionary<Guid, string>> ItemNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Items.AsNoTracking().Where(i => list.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name, ct);
    }

    private static string AppendComment(string? current, string? add) =>
        add is null ? current ?? "" : string.IsNullOrWhiteSpace(current) ? add : current + "\n" + add;

    private sealed record Names(Dictionary<Guid, string> Warehouses, Dictionary<Guid, string> Branches, Dictionary<Guid, string> Users);

    private async Task<Names> NamesAsync(IReadOnlyList<PurchaseRequest> requests, CancellationToken ct)
    {
        var wh = requests.Select(r => r.WarehouseId).Distinct().ToList();
        var br = requests.Where(r => r.BranchId != null).Select(r => r.BranchId!.Value).Distinct().ToList();
        var us = requests.Where(r => r.CreatedBy != null).Select(r => r.CreatedBy!.Value).Distinct().ToList();
        return new Names(
            await db.Warehouses.AsNoTracking().Where(w => wh.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name, ct),
            await db.Branches.AsNoTracking().Where(b => br.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, ct),
            await db.Users.AsNoTracking().Where(u => us.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct));
    }

    private async Task<PurchaseRequestDto> MapAsync(PurchaseRequest r, CancellationToken ct)
    {
        var names = await NamesAsync([r], ct);
        var itemIds = r.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var lines = r.Lines.OrderBy(l => items.GetValueOrDefault(l.ItemId)?.Name).Select(l =>
        {
            var item = items.GetValueOrDefault(l.ItemId);
            return new PurchaseRequestLineDto(l.Id, l.ItemId, item?.Name ?? "?", item?.Sku ?? "", item?.BaseUnit.ToString() ?? "", l.Qty, l.CurrentQty, l.MinQty, l.OptimalQty);
        }).ToList();
        return new PurchaseRequestDto(r.Id, r.BranchId, r.BranchId is { } b ? names.Branches.GetValueOrDefault(b) : null, r.WarehouseId,
            names.Warehouses.GetValueOrDefault(r.WarehouseId, "?"), r.Status, r.Source, r.Comment, r.CreatedAt,
            r.CreatedBy is { } cb ? names.Users.GetValueOrDefault(cb) : null, r.UpdatedAt, lines);
    }
}
