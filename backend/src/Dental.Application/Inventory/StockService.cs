using Dental.Application.Common;
using Dental.Application.Orgs;
using Dental.Application.Permissions;
using Dental.Domain.Common;
using Dental.Domain.Inventory;
using Dental.Domain.Organizations;
using Dental.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Dental.Application.Inventory;

/// <summary>
/// Складской учёт через журнал движений. Проведение документа атомарно создаёт stock_movements
/// и обновляет кэш stock_balances в той же транзакции (строки остатков блокируются FOR UPDATE).
/// </summary>
public sealed class StockService(
    IAppDbContext db,
    ICurrentUser user,
    ITenantContext tenant,
    IAuditService audit,
    INotificationService notifications,
    ApprovalService approvals,
    TimeProvider clock) : IVisitStockConsumer
{
    private sealed record MoveSpec(Guid WarehouseId, Guid ItemId, Guid? BatchId, decimal Qty, decimal UnitCost, Guid? LineId, Guid? PatientId = null);

    // ================= Документы =================

    public async Task<PagedResult<StockDocumentListItem>> ListAsync(StockDocumentType? type, StockDocumentStatus? status, Guid? warehouseId, DateOnly? from, DateOnly? to,
        PageQuery page, CancellationToken ct)
    {
        var q = db.StockDocuments.AsNoTracking();
        if (type is { } t) q = q.Where(d => d.Type == t);
        if (status is { } s) q = q.Where(d => d.Status == s);
        if (warehouseId is { } w) q = q.Where(d => d.WarehouseFromId == w || d.WarehouseToId == w);
        if (from is { } f) { var fu = new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); q = q.Where(d => d.CreatedAt >= fu); }
        if (to is { } tt) { var tu = new DateTimeOffset(tt.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); q = q.Where(d => d.CreatedAt < tu); }
        if (!user.AllBranches)
        {
            var allowed = await AccessibleWarehouseIdsAsync(ct);
            q = q.Where(d => (d.WarehouseFromId != null && allowed.Contains(d.WarehouseFromId.Value)) || (d.WarehouseToId != null && allowed.Contains(d.WarehouseToId.Value)));
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(d => d.CreatedAt).Skip((page.SafePage - 1) * page.SafeSize).Take(page.SafeSize)
            .Select(d => new { d, Lines = d.Lines.Count }).ToListAsync(ct);
        var names = await NamesAsync(rows.Select(r => r.d).ToList(), ct);
        var items = rows.Select(r => new StockDocumentListItem(r.d.Id, r.d.Type, r.d.Number, r.d.Status,
            r.d.WarehouseFromId is { } wf ? names.Warehouses.GetValueOrDefault(wf) : null, r.d.WarehouseToId is { } wt ? names.Warehouses.GetValueOrDefault(wt) : null,
            r.d.SupplierId is { } sp ? names.Suppliers.GetValueOrDefault(sp) : null, r.d.ReasonId is { } rs ? names.Reasons.GetValueOrDefault(rs) : null,
            r.d.CreatedAt, r.d.PostedAt, r.d.TotalCost, r.Lines, r.d.Comment)).ToList();
        return new PagedResult<StockDocumentListItem>(items, total, page.SafePage, page.SafeSize);
    }

    public async Task<StockDocumentDto> GetAsync(Guid id, CancellationToken ct)
    {
        var d = await db.StockDocuments.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Документ");
        await EnsureDocAccessAsync(d, ct);
        return await MapAsync(d, ct);
    }

    public async Task<StockDocumentDto> CreateAsync(CreateStockDocumentRequest r, CancellationToken ct)
    {
        EnsureTypePermission(r.Type);
        var doc = new StockDocument
        {
            Type = r.Type,
            WarehouseFromId = r.WarehouseFromId,
            WarehouseToId = r.WarehouseToId,
            SupplierId = r.SupplierId,
            PurchaseOrderId = r.PurchaseOrderId,
            InvoiceNumber = r.InvoiceNumber.NullIfEmpty(),
            InvoiceDate = r.InvoiceDate,
            ReasonId = r.ReasonId,
            Comment = r.Comment.NullIfEmpty(),
            Number = await NextNumberAsync(r.Type, ct),
        };
        await ValidateWarehousesAsync(doc, ct);
        doc.BranchId = await BranchOfDocAsync(doc, ct);
        db.StockDocuments.Add(doc);

        if (r.Type == StockDocumentType.Inventory)
        {
            await StartInventoryAsync(doc, ct);
        }
        else
        {
            await SetLinesAsync(doc, r.Lines ?? [], ct);
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(doc.Id, ct);
    }

    public async Task<StockDocumentDto> UpdateAsync(Guid id, UpdateStockDocumentRequest r, CancellationToken ct)
    {
        var doc = await db.StockDocuments.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Документ");
        await EnsureDocAccessAsync(doc, ct);
        EnsureTypePermission(doc.Type);
        db.SetExpectedVersion(doc, r.Version);
        if (doc.Status != StockDocumentStatus.Draft) throw AppException.Conflict(ErrorCodes.DocumentNotEditable, "Редактировать можно только черновик");
        if (doc.Type != StockDocumentType.Inventory)
        {
            doc.WarehouseFromId = r.WarehouseFromId ?? doc.WarehouseFromId;
            doc.WarehouseToId = r.WarehouseToId ?? doc.WarehouseToId;
            await ValidateWarehousesAsync(doc, ct);
        }
        if (r.SupplierId is not null) doc.SupplierId = r.SupplierId;
        if (r.InvoiceNumber is not null) doc.InvoiceNumber = r.InvoiceNumber.NullIfEmpty();
        if (r.InvoiceDate is not null) doc.InvoiceDate = r.InvoiceDate;
        if (r.ReasonId is not null) doc.ReasonId = r.ReasonId;
        if (r.Comment is not null) doc.Comment = r.Comment.NullIfEmpty();
        if (r.Lines is not null)
        {
            if (doc.Type == StockDocumentType.Inventory)
            {
                // Для инвентаризации строки не заменяются: вводится только факт.
                foreach (var l in r.Lines)
                {
                    var line = doc.Lines.FirstOrDefault(x => x.ItemId == l.ItemId && x.BatchId == l.BatchId);
                    if (line is not null) line.ActualQty = l.ActualQty ?? l.Qty;
                    else doc.Lines.Add(await NewLineAsync(doc, l, ct, expected: 0));
                }
            }
            else
            {
                await SetLinesAsync(doc, r.Lines, ct);
            }
        }
        doc.BranchId = await BranchOfDocAsync(doc, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(doc.Id, ct);
    }

    /// <summary>Проведение документа (приход, списание, отправка перемещения, возврат поставщику, утверждение инвентаризации).</summary>
    public async Task<StockDocumentDto> PostAsync(Guid id, StockActionRequest r, CancellationToken ct)
    {
        await using var tx = await BeginAsync(ct);
        var doc = await LockDocAsync(id, ct);
        await EnsureDocAccessAsync(doc, ct);
        db.SetExpectedVersion(doc, r.Version);
        if (doc.Status != StockDocumentStatus.Draft)
            throw AppException.Conflict(ErrorCodes.DocumentInvalidState, "Провести можно только черновик");
        if (doc.Lines.Count == 0 && doc.Type != StockDocumentType.Inventory) throw AppException.BadRequest(ErrorCodes.LinesRequired, "Добавьте хотя бы одну строку");

        switch (doc.Type)
        {
            case StockDocumentType.Receipt:
                user.EnsurePermission(Perm.Inventory.Receive);
                await PostReceiptAsync(doc, ct);
                break;
            case StockDocumentType.Writeoff:
                user.EnsurePermission(Perm.Inventory.Writeoff);
                await PostWriteoffAsync(doc, ct);
                break;
            case StockDocumentType.Transfer:
                user.EnsurePermission(Perm.Inventory.TransferCreate);
                await SendTransferAsync(doc, ct);
                break;
            case StockDocumentType.ReturnToSupplier:
                user.EnsurePermission(Perm.Inventory.Writeoff);
                await PostReturnAsync(doc, ct);
                break;
            case StockDocumentType.Inventory:
                user.EnsurePermission(Perm.Inventory.CountApprove);
                await ApproveInventoryAsync(doc, r.Counts, ct);
                break;
            default:
                throw AppException.Conflict(ErrorCodes.DocumentInvalidState, "Этот тип документа проводится автоматически");
        }
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await GetAsync(doc.Id, ct);
    }

    /// <summary>Приёмка перемещения получателем: приход фактического количества; недостача — в документ списания (причина lost) на подтверждение.</summary>
    public async Task<StockDocumentDto> ReceiveAsync(Guid id, ReceiveTransferRequest r, CancellationToken ct)
    {
        user.EnsurePermission(Perm.Inventory.TransferReceive);
        await using var tx = await BeginAsync(ct);
        var doc = await LockDocAsync(id, ct);
        if (doc.Type != StockDocumentType.Transfer || doc.Status != StockDocumentStatus.InTransit)
            throw AppException.Conflict(ErrorCodes.DocumentInvalidState, "Принять можно только перемещение в пути");
        var to = await db.Warehouses.FirstAsync(w => w.Id == doc.WarehouseToId, ct);
        if (to.BranchId is { } b) user.EnsureBranchAccess(b);
        db.SetExpectedVersion(doc, r.Version);

        var actuals = (r.Lines ?? []).ToDictionary(l => l.LineId, l => l.ActualQty);
        var moves = new List<MoveSpec>();
        var shortage = new List<(StockDocumentLine Line, decimal Qty)>();
        foreach (var line in doc.Lines)
        {
            var actual = actuals.TryGetValue(line.Id, out var a) ? a : line.Qty;
            if (actual < 0 || actual > line.Qty) throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Фактическое количество не может быть больше отправленного");
            line.ActualQty = actual;
            if (actual > 0) moves.Add(new MoveSpec(to.Id, line.ItemId, line.BatchId, actual, line.UnitCost, line.Id));
            if (actual < line.Qty) shortage.Add((line, line.Qty - actual));
        }
        await ApplyMovesAsync(doc, moves, allowNegative: false, ct);
        doc.Status = StockDocumentStatus.Received;
        doc.ReceivedAt = clock.GetUtcNow();
        doc.ReceivedBy = user.UserId;
        if (r.Comment.NullIfEmpty() is { } c) doc.Comment = string.Join("\n", new[] { doc.Comment, c }.Where(x => x is not null));

        if (shortage.Count > 0) await CreateShortageWriteoffAsync(doc, to, shortage, ct);

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await GetAsync(doc.Id, ct);
    }

    /// <summary>
    /// Отмена: черновик/ожидающий — просто отменяется; проведённый приход или списание — сторно движений.
    /// </summary>
    public async Task<StockDocumentDto> CancelAsync(Guid id, StockActionRequest r, CancellationToken ct)
    {
        await using var tx = await BeginAsync(ct);
        var doc = await LockDocAsync(id, ct);
        await EnsureDocAccessAsync(doc, ct);
        EnsureTypePermission(doc.Type);
        db.SetExpectedVersion(doc, r.Version);
        switch (doc.Status)
        {
            case StockDocumentStatus.Draft or StockDocumentStatus.PendingApproval:
                if (doc.Type == StockDocumentType.Inventory) await UnlockWarehouseAsync(doc, ct);
                var pending = await db.ApprovalRequests.Where(a => a.EntityId == doc.Id && a.Status == ApprovalStatus.Pending).ToListAsync(ct);
                foreach (var p in pending) { p.Status = ApprovalStatus.Rejected; p.Comment = "Документ отменён"; p.DecidedAt = clock.GetUtcNow(); p.DecidedBy = user.UserId; }
                break;
            case StockDocumentStatus.Posted when doc.Type is StockDocumentType.Receipt or StockDocumentType.Writeoff or StockDocumentType.ReturnToSupplier:
                if (string.IsNullOrWhiteSpace(r.Comment)) throw AppException.BadRequest(ErrorCodes.CommentRequired, "Укажите причину отмены проведённого документа");
                await ReverseMovementsAsync(doc, ct);
                audit.Log(nameof(StockDocument), doc.Id, "storno", new { doc.Number, doc.TotalCost }, r.Comment, suspicious: true, branchId: doc.BranchId);
                break;
            default:
                throw AppException.Conflict(ErrorCodes.DocumentInvalidState, "Документ в этом статусе нельзя отменить");
        }
        doc.Status = StockDocumentStatus.Cancelled;
        if (r.Comment.NullIfEmpty() is { } c) doc.Comment = string.Join("\n", new[] { doc.Comment, "Отмена: " + c }.Where(x => x is not null));
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await GetAsync(doc.Id, ct);
    }

    // ================= Проведение по типам =================

    private async Task PostReceiptAsync(StockDocument doc, CancellationToken ct)
    {
        if (doc.SupplierId is null) throw AppException.BadRequest(ErrorCodes.SupplierRequired, "Выберите поставщика");
        if (doc.WarehouseToId is null) throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Выберите склад");
        var items = await ItemsAsync(doc.Lines.Select(l => l.ItemId), ct);
        var moves = new List<MoveSpec>();
        foreach (var line in doc.Lines)
        {
            var item = items[line.ItemId];
            if (line.Qty <= 0) throw AppException.BadRequest(ErrorCodes.ValidationFailed, $"Количество по «{item.Name}» должно быть больше нуля");
            if (item.TrackSerials && (line.Qty != 1 || string.IsNullOrWhiteSpace(line.SerialNumber)))
                throw AppException.BadRequest(ErrorCodes.SerialRequired, $"«{item.Name}»: укажите серийный номер, по одной строке на единицу");
            if (item.TrackBatches && string.IsNullOrWhiteSpace(line.BatchNumber) && string.IsNullOrWhiteSpace(line.SerialNumber))
                throw AppException.BadRequest(ErrorCodes.BatchRequired, $"«{item.Name}»: укажите номер партии");
            if (item.TrackExpiry && line.ExpiresAt is null)
                throw AppException.BadRequest(ErrorCodes.ExpiryRequired, $"«{item.Name}»: укажите срок годности");

            if (item.TrackBatches || item.TrackSerials || item.TrackExpiry)
            {
                var batch = new Batch
                {
                    ItemId = item.Id, BatchNumber = line.BatchNumber, SerialNumber = line.SerialNumber, ExpiresAt = line.ExpiresAt,
                    UnitCost = line.UnitCost, SupplierId = doc.SupplierId,
                };
                db.Batches.Add(batch);
                line.BatchId = batch.Id;
            }
            moves.Add(new MoveSpec(doc.WarehouseToId.Value, line.ItemId, line.BatchId, line.Qty, line.UnitCost, line.Id));
        }
        await db.SaveChangesAsync(ct);
        await ApplyMovesAsync(doc, moves, allowNegative: false, ct);
        doc.TotalCost = doc.Lines.Sum(l => l.TotalCost);
        MarkPosted(doc);

        // Последняя цена у поставщика.
        var supplierItems = await db.SupplierItems.Where(s => s.SupplierId == doc.SupplierId).ToDictionaryAsync(s => s.ItemId, ct);
        foreach (var g in doc.Lines.GroupBy(l => l.ItemId))
        {
            var price = (long)Math.Round(g.Last().UnitCost, MidpointRounding.AwayFromZero);
            if (supplierItems.TryGetValue(g.Key, out var si)) { si.LastPrice = price; si.LastPriceAt = clock.GetUtcNow(); }
            else db.SupplierItems.Add(new SupplierItem { SupplierId = doc.SupplierId!.Value, ItemId = g.Key, LastPrice = price, LastPriceAt = clock.GetUtcNow() });
        }

        // Приход по заказу поставщику: received_qty и статус заказа.
        if (doc.PurchaseOrderId is { } poId)
        {
            var po = await db.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == poId, ct);
            if (po is not null)
            {
                foreach (var g in doc.Lines.GroupBy(l => l.ItemId))
                {
                    var qty = g.Sum(l => l.Qty);
                    foreach (var pl in po.Lines.Where(x => x.ItemId == g.Key))
                    {
                        var take = Math.Min(qty, Math.Max(0, pl.Qty - pl.ReceivedQty));
                        pl.ReceivedQty += take;
                        qty -= take;
                        if (qty <= 0) break;
                    }
                    if (qty > 0 && po.Lines.FirstOrDefault(x => x.ItemId == g.Key) is { } first) first.ReceivedQty += qty;
                }
                po.RecalculateStatus();
            }
        }

        // Счёт поставщика по накладной.
        if (!string.IsNullOrWhiteSpace(doc.InvoiceNumber))
        {
            var supplier = await db.Suppliers.AsNoTracking().FirstAsync(s => s.Id == doc.SupplierId, ct);
            var date = doc.InvoiceDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            db.SupplierInvoices.Add(new SupplierInvoice
            {
                SupplierId = supplier.Id, PurchaseOrderId = doc.PurchaseOrderId, StockDocumentId = doc.Id, Number = doc.InvoiceNumber!, Date = date,
                Amount = doc.TotalCost, DueDate = supplier.PaymentTermsDays > 0 ? date.AddDays(supplier.PaymentTermsDays) : null,
            });
        }
    }

    private async Task PostWriteoffAsync(StockDocument doc, CancellationToken ct)
    {
        if (doc.ReasonId is null) throw AppException.BadRequest(ErrorCodes.ReasonRequired, "Укажите причину списания");
        var from = await GetWarehouseForOutAsync(doc.WarehouseFromId, ct);
        await AllocateLinesAsync(doc, from.Id, allowNegative: false, ct);
        doc.TotalCost = doc.Lines.Sum(l => l.TotalCost);

        if (!user.Limits.AllowsWriteoff(doc.TotalCost))
        {
            doc.Status = StockDocumentStatus.PendingApproval;
            await approvals.RequestAsync(ApprovalType.Writeoff, nameof(StockDocument), doc.Id, doc.TotalCost,
                $"Списание {doc.Number} на {new Money(doc.TotalCost)}", new { doc.Number, doc.TotalCost, lines = doc.Lines.Count }, doc.BranchId, ct);
            return;
        }
        await CompleteWriteoffAsync(doc, ct);
    }

    /// <summary>Проведение списания (сразу или после подтверждения).</summary>
    internal async Task CompleteWriteoffAsync(StockDocument doc, CancellationToken ct)
    {
        var moves = doc.Lines.Select(l => new MoveSpec(doc.WarehouseFromId!.Value, l.ItemId, l.BatchId, -l.Qty, l.UnitCost, l.Id)).ToList();
        if (doc.SourceDocumentId is not null)
        {
            // Недостача при перемещении: товар уже списан со склада-отправителя и не пришёл — движений нет.
            moves.Clear();
        }
        await ApplyMovesAsync(doc, moves, allowNegative: false, ct);
        MarkPosted(doc);
        var org = await db.Organizations.AsNoTracking().FirstAsync(ct);
        if (doc.TotalCost > org.Settings.SuspiciousWriteoffAmount)
            audit.Log(nameof(StockDocument), doc.Id, "writeoff_large", new { doc.Number, doc.TotalCost }, $"Списание {doc.Number} на {new Money(doc.TotalCost)}", suspicious: true, branchId: doc.BranchId);
    }

    private async Task SendTransferAsync(StockDocument doc, CancellationToken ct)
    {
        var from = await GetWarehouseForOutAsync(doc.WarehouseFromId, ct);
        if (doc.WarehouseToId is null || doc.WarehouseToId == doc.WarehouseFromId) throw AppException.BadRequest(ErrorCodes.SameWarehouse, "Выберите склад получателя");
        await AllocateLinesAsync(doc, from.Id, allowNegative: false, ct);
        var moves = doc.Lines.Select(l => new MoveSpec(from.Id, l.ItemId, l.BatchId, -l.Qty, l.UnitCost, l.Id)).ToList();
        await ApplyMovesAsync(doc, moves, allowNegative: false, ct);
        doc.TotalCost = doc.Lines.Sum(l => l.TotalCost);
        doc.Status = StockDocumentStatus.InTransit;
        doc.PostedAt = clock.GetUtcNow();
        doc.PostedBy = user.UserId;
    }

    private async Task PostReturnAsync(StockDocument doc, CancellationToken ct)
    {
        if (doc.SupplierId is null) throw AppException.BadRequest(ErrorCodes.SupplierRequired, "Выберите поставщика");
        var from = await GetWarehouseForOutAsync(doc.WarehouseFromId, ct);
        await AllocateLinesAsync(doc, from.Id, allowNegative: false, ct);
        var moves = doc.Lines.Select(l => new MoveSpec(from.Id, l.ItemId, l.BatchId, -l.Qty, l.UnitCost, l.Id)).ToList();
        await ApplyMovesAsync(doc, moves, allowNegative: false, ct);
        doc.TotalCost = doc.Lines.Sum(l => l.TotalCost);
        MarkPosted(doc);
        var invoice = await db.SupplierInvoices.Where(i => i.SupplierId == doc.SupplierId && i.Status != SupplierInvoiceStatus.Paid)
            .OrderByDescending(i => i.Date).FirstOrDefaultAsync(ct);
        if (invoice is not null)
        {
            invoice.ReturnedAmount += doc.TotalCost;
            invoice.RecalculateStatus();
        }
    }

    // ================= Инвентаризация =================

    private async Task StartInventoryAsync(StockDocument doc, CancellationToken ct)
    {
        user.EnsurePermission(Perm.Inventory.Count);
        var w = await db.Warehouses.FirstAsync(x => x.Id == doc.WarehouseFromId, ct);
        if (w.Locked) throw AppException.Conflict(ErrorCodes.WarehouseLocked, "На складе уже идёт инвентаризация");
        w.Locked = true;
        w.LockedByDocumentId = doc.Id;
        doc.SnapshotAt = clock.GetUtcNow();
        var balances = await db.StockBalances.AsNoTracking().Where(b => b.WarehouseId == w.Id && b.Qty != 0).ToListAsync(ct);
        var batchIds = balances.Where(b => b.BatchId != null).Select(b => b.BatchId!.Value).ToList();
        var batches = await db.Batches.AsNoTracking().Where(b => batchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        foreach (var b in balances)
        {
            var cost = b.BatchId is { } bid && batches.TryGetValue(bid, out var batch) ? batch.UnitCost : b.AvgCost;
            doc.Lines.Add(new StockDocumentLine
            {
                DocumentId = doc.Id, ItemId = b.ItemId, BatchId = b.BatchId, Qty = 0, QtyInput = 0, ExpectedQty = b.Qty, ActualQty = null, UnitCost = cost,
                BatchNumber = b.BatchId is { } x && batches.TryGetValue(x, out var bb) ? bb.BatchNumber : null,
                ExpiresAt = b.BatchId is { } y && batches.TryGetValue(y, out var be) ? be.ExpiresAt : null,
            });
        }
    }

    /// <summary>
    /// Утверждение: движения на разницу (факт − ожидаемое на момент старта). Движения визитов во время пересчёта
    /// уже учтены в остатке и не искажают результат: итог = факт + расход после снимка.
    /// </summary>
    private async Task ApproveInventoryAsync(StockDocument doc, IReadOnlyList<CountLineInput>? counts, CancellationToken ct)
    {
        foreach (var c in counts ?? [])
        {
            var line = doc.Lines.FirstOrDefault(l => l.Id == c.LineId) ?? throw AppException.NotFound("Строка");
            line.ActualQty = c.ActualQty;
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
            line.TotalCost = (long)Math.Round(diff * line.UnitCost, MidpointRounding.AwayFromZero);
            diffValue += Math.Abs(line.TotalCost);
            if (diff != 0) moves.Add(new MoveSpec(doc.WarehouseFromId!.Value, line.ItemId, line.BatchId, diff, line.UnitCost, line.Id));
        }
        await ApplyMovesAsync(doc, moves, allowNegative: true, ct);
        doc.TotalCost = doc.Lines.Sum(l => l.TotalCost);
        MarkPosted(doc);
        await UnlockWarehouseAsync(doc, ct);
        var org = await db.Organizations.AsNoTracking().FirstAsync(ct);
        if (diffValue > org.Settings.SuspiciousInventoryDiffAmount)
            audit.Log(nameof(StockDocument), doc.Id, "inventory_discrepancy", new { doc.Number, diffValue, net = doc.TotalCost },
                $"Расхождение инвентаризации {doc.Number}: {new Money(diffValue)}", suspicious: true, branchId: doc.BranchId);
    }

    private async Task UnlockWarehouseAsync(StockDocument doc, CancellationToken ct)
    {
        var w = await db.Warehouses.FirstOrDefaultAsync(x => x.Id == doc.WarehouseFromId, ct);
        if (w is not null && w.LockedByDocumentId == doc.Id)
        {
            w.Locked = false;
            w.LockedByDocumentId = null;
        }
    }

    // ================= Расход по визиту =================

    public async Task<(Guid DocumentId, IReadOnlyDictionary<Guid, (long Cost, Guid? BatchId)> Costs)> ConsumeAsync(
        Guid visitId, Guid branchId, Guid patientId, IReadOnlyList<(Guid UsageId, Guid ItemId, decimal Qty)> usages, CancellationToken ct)
    {
        var warehouse = await db.Warehouses.Where(w => w.BranchId == branchId && w.DeletedAt == null)
            .OrderBy(w => w.Type == WarehouseType.Cabinet ? 0 : 1).ThenBy(w => w.CreatedAt).FirstOrDefaultAsync(ct)
            ?? throw AppException.Conflict(ErrorCodes.ValidationFailed, "У филиала нет склада для списания материалов");
        var org = await db.Organizations.AsNoTracking().FirstAsync(ct);
        var doc = new StockDocument
        {
            Type = StockDocumentType.VisitConsumption, VisitId = visitId, BranchId = branchId, WarehouseFromId = warehouse.Id,
            Number = await NextNumberAsync(StockDocumentType.VisitConsumption, ct),
        };
        db.StockDocuments.Add(doc);
        var costs = new Dictionary<Guid, (long, Guid?)>();
        var moves = new List<MoveSpec>();
        var negative = new List<string>();
        var items = await ItemsAsync(usages.Select(u => u.ItemId), ct);
        foreach (var u in usages.Where(u => u.Qty > 0))
        {
            var allocation = await AllocateAsync(warehouse.Id, u.ItemId, u.Qty, org.Settings.AllowNegativeStock, ct);
            long cost = 0;
            Guid? batch = null;
            foreach (var a in allocation)
            {
                var line = new StockDocumentLine
                {
                    DocumentId = doc.Id, ItemId = u.ItemId, BatchId = a.BatchId, Qty = a.Qty, QtyInput = a.Qty, UnitCost = a.UnitCost,
                    TotalCost = (long)Math.Round(a.Qty * a.UnitCost, MidpointRounding.AwayFromZero),
                };
                doc.Lines.Add(line);
                moves.Add(new MoveSpec(warehouse.Id, u.ItemId, a.BatchId, -a.Qty, a.UnitCost, line.Id, items[u.ItemId].TrackSerials ? patientId : null));
                cost += line.TotalCost;
                batch ??= a.BatchId;
            }
            costs[u.UsageId] = (cost, batch);
        }
        await db.SaveChangesAsync(ct);
        var result = await ApplyMovesAsync(doc, moves, allowNegative: org.Settings.AllowNegativeStock, ct);
        negative.AddRange(result.Where(r => r.NewQty < 0).Select(r => items.TryGetValue(r.ItemId, out var i) ? i.Name : r.ItemId.ToString()));
        doc.TotalCost = doc.Lines.Sum(l => l.TotalCost);
        MarkPosted(doc);
        if (negative.Count > 0)
        {
            await notifications.NotifyByPermissionAsync(Perm.Inventory.Receive, "negative_stock",
                $"Отрицательный остаток на складе «{warehouse.Name}»", string.Join(", ", negative.Distinct()), nameof(StockDocument), doc.Id, branchId, ct);
        }
        return (doc.Id, costs);
    }

    public async Task ReverseAsync(Guid documentId, string reason, CancellationToken ct)
    {
        var doc = await db.StockDocuments.Include(d => d.Lines).FirstAsync(d => d.Id == documentId, ct);
        if (doc.Status != StockDocumentStatus.Posted) return;
        await ReverseMovementsAsync(doc, ct);
        doc.Status = StockDocumentStatus.Cancelled;
        doc.Comment = string.Join("\n", new[] { doc.Comment, "Сторно: " + reason }.Where(x => x is not null));
    }

    // ================= Движения и остатки =================

    private sealed record BalanceResult(Guid WarehouseId, Guid ItemId, Guid? BatchId, decimal NewQty);

    /// <summary>
    /// Блокирует строки остатков (FOR UPDATE), обновляет кэш upsert-ом и пишет журнал движений.
    /// Уход в минус запрещён, если allowNegative = false.
    /// </summary>
    private async Task<List<BalanceResult>> ApplyMovesAsync(StockDocument doc, IReadOnlyList<MoveSpec> moves, bool allowNegative, CancellationToken ct)
    {
        var orgId = tenant.RequiredOrganizationId;
        var now = clock.GetUtcNow();
        var results = new List<BalanceResult>();
        // Порядок блокировок фиксирован (склад, товар) — без взаимоблокировок при параллельных документах.
        foreach (var group in moves.GroupBy(m => (m.WarehouseId, m.ItemId)).OrderBy(g => g.Key.WarehouseId).ThenBy(g => g.Key.ItemId))
        {
            await LockBalancesAsync(group.Key.WarehouseId, group.Key.ItemId, ct);
            foreach (var m in group)
            {
                var id = Guid.CreateVersion7();
                var newQty = (await db.Database.SqlQuery<decimal>($"""
                    INSERT INTO stock_balances (id, organization_id, warehouse_id, item_id, batch_id, qty, avg_cost, created_at, updated_at)
                    VALUES ({id}, {orgId}, {m.WarehouseId}, {m.ItemId}, {m.BatchId}, {m.Qty}, {(m.Qty > 0 ? m.UnitCost : 0m)}, {now}, {now})
                    ON CONFLICT (warehouse_id, item_id, batch_id) DO UPDATE SET
                      avg_cost = CASE WHEN EXCLUDED.qty > 0
                        THEN ROUND((GREATEST(stock_balances.qty, 0) * stock_balances.avg_cost + EXCLUDED.qty * EXCLUDED.avg_cost)
                                   / NULLIF(GREATEST(stock_balances.qty, 0) + EXCLUDED.qty, 0), 4)
                        ELSE stock_balances.avg_cost END,
                      qty = stock_balances.qty + EXCLUDED.qty,
                      updated_at = EXCLUDED.updated_at
                    RETURNING qty AS "Value"
                    """).ToListAsync(ct))[0];
                if (newQty < 0 && !allowNegative)
                {
                    var name = await db.Items.Where(i => i.Id == m.ItemId).Select(i => i.Name).FirstOrDefaultAsync(ct);
                    throw new AppException(ErrorCodes.StockInsufficient, $"Недостаточно остатка: «{name}»", 409,
                        new Dictionary<string, object?> { ["itemId"] = m.ItemId, ["missing"] = -newQty });
                }
                db.StockMovements.Add(new StockMovement
                {
                    DocumentId = doc.Id, LineId = m.LineId, WarehouseId = m.WarehouseId, ItemId = m.ItemId, BatchId = m.BatchId, Qty = m.Qty,
                    UnitCost = m.UnitCost, MovedAt = now, PatientId = m.PatientId, DocumentType = doc.Type,
                });
                results.Add(new BalanceResult(m.WarehouseId, m.ItemId, m.BatchId, newQty));
            }
        }
        return results;
    }

    private async Task LockBalancesAsync(Guid warehouseId, Guid itemId, CancellationToken ct) =>
        await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM stock_balances WHERE warehouse_id = {warehouseId} AND item_id = {itemId} FOR UPDATE").ToListAsync(ct);

    private async Task ReverseMovementsAsync(StockDocument doc, CancellationToken ct)
    {
        var original = await db.StockMovements.AsNoTracking().Where(m => m.DocumentId == doc.Id).ToListAsync(ct);
        var moves = original.Select(m => new MoveSpec(m.WarehouseId, m.ItemId, m.BatchId, -m.Qty, m.UnitCost, m.LineId, m.PatientId)).ToList();
        await ApplyMovesAsync(doc, moves, allowNegative: doc.Type == StockDocumentType.VisitConsumption, ct);
    }

    /// <summary>FEFO-подбор партий на складе (строки остатков блокируются).</summary>
    private async Task<IReadOnlyList<BatchAllocation>> AllocateAsync(Guid warehouseId, Guid itemId, decimal qty, bool allowNegative, CancellationToken ct)
    {
        await LockBalancesAsync(warehouseId, itemId, ct);
        var stock = await (from b in db.StockBalances.AsNoTracking()
                           join bt in db.Batches.AsNoTracking() on b.BatchId equals bt.Id into bj
                           from bt in bj.DefaultIfEmpty()
                           where b.WarehouseId == warehouseId && b.ItemId == itemId
                           select new { b.BatchId, b.Qty, b.AvgCost, Expires = bt == null ? (DateOnly?)null : bt.ExpiresAt, Cost = bt == null ? (decimal?)null : bt.UnitCost, b.CreatedAt })
            .ToListAsync(ct);
        var fallback = stock.Count > 0 ? stock[^1].Cost ?? stock[^1].AvgCost
            : await db.Batches.Where(b => b.ItemId == itemId).OrderByDescending(b => b.CreatedAt).Select(b => (decimal?)b.UnitCost).FirstOrDefaultAsync(ct) ?? 0;
        return Fefo.Allocate(stock.Select(s => new BatchStock(s.BatchId, s.Expires, s.Qty, s.Cost ?? s.AvgCost, s.CreatedAt)), qty, allowNegative, fallback);
    }

    /// <summary>Строки без партии разбиваются по партиям FEFO; строки с партией — берут её себестоимость.</summary>
    private async Task AllocateLinesAsync(StockDocument doc, Guid warehouseId, bool allowNegative, CancellationToken ct)
    {
        var original = doc.Lines.ToList();
        foreach (var line in original)
        {
            if (line.Qty <= 0) throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Количество должно быть больше нуля");
            if (line.BatchId is { } bid)
            {
                var batch = await db.Batches.AsNoTracking().FirstAsync(b => b.Id == bid, ct);
                line.UnitCost = batch.UnitCost;
                line.TotalCost = (long)Math.Round(line.Qty * line.UnitCost, MidpointRounding.AwayFromZero);
                continue;
            }
            var alloc = await AllocateAsync(warehouseId, line.ItemId, line.Qty, allowNegative, ct);
            var first = true;
            foreach (var a in alloc)
            {
                var target = line;
                if (!first)
                {
                    target = new StockDocumentLine { DocumentId = doc.Id, ItemId = line.ItemId, UnitId = null };
                    doc.Lines.Add(target);
                }
                target.BatchId = a.BatchId;
                target.Qty = a.Qty;
                target.QtyInput = first && line.UnitId is not null ? line.QtyInput * (a.Qty / line.Qty) : a.Qty;
                target.UnitCost = a.UnitCost;
                target.TotalCost = (long)Math.Round(a.Qty * a.UnitCost, MidpointRounding.AwayFromZero);
                first = false;
            }
        }
        var batchIds = doc.Lines.Where(l => l.BatchId != null).Select(l => l.BatchId!.Value).Distinct().ToList();
        var batches = await db.Batches.AsNoTracking().Where(b => batchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        foreach (var l in doc.Lines.Where(l => l.BatchId != null))
        {
            var b = batches[l.BatchId!.Value];
            l.BatchNumber ??= b.BatchNumber;
            l.SerialNumber ??= b.SerialNumber;
            l.ExpiresAt ??= b.ExpiresAt;
        }
    }

    private async Task CreateShortageWriteoffAsync(StockDocument transfer, Warehouse to, List<(StockDocumentLine Line, decimal Qty)> shortage, CancellationToken ct)
    {
        var reason = await db.WriteoffReasons.Where(r => r.Type == WriteoffReasonType.Lost && r.DeletedAt == null).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
        var wo = new StockDocument
        {
            Type = StockDocumentType.Writeoff,
            Status = StockDocumentStatus.PendingApproval,
            Number = await NextNumberAsync(StockDocumentType.Writeoff, ct),
            WarehouseFromId = transfer.WarehouseFromId,
            WarehouseToId = to.Id,
            BranchId = to.BranchId,
            SourceDocumentId = transfer.Id,
            ReasonId = reason,
            Comment = $"Недостача при приёмке перемещения {transfer.Number}",
        };
        foreach (var (line, qty) in shortage)
        {
            wo.Lines.Add(new StockDocumentLine
            {
                DocumentId = wo.Id, ItemId = line.ItemId, BatchId = line.BatchId, Qty = qty, QtyInput = qty, UnitCost = line.UnitCost,
                TotalCost = (long)Math.Round(qty * line.UnitCost, MidpointRounding.AwayFromZero), ExpectedQty = line.Qty, ActualQty = line.ActualQty,
                BatchNumber = line.BatchNumber, SerialNumber = line.SerialNumber, ExpiresAt = line.ExpiresAt,
            });
        }
        wo.TotalCost = wo.Lines.Sum(l => l.TotalCost);
        db.StockDocuments.Add(wo);
        await approvals.RequestAsync(ApprovalType.TransferShortage, nameof(StockDocument), wo.Id, wo.TotalCost,
            $"Недостача {new Money(wo.TotalCost)} при приёмке {transfer.Number}", new { transfer = transfer.Number, writeoff = wo.Number, wo.TotalCost }, to.BranchId, ct);
    }

    // ================= Остатки, движения, карточка =================

    public async Task<IReadOnlyList<StockBalanceRow>> BalancesAsync(Guid? warehouseId, Guid? categoryId, bool belowMin, int? expiringDays, string? q, CancellationToken ct)
    {
        var warehouses = await AccessibleWarehousesAsync(ct);
        var whIds = warehouseId is { } w ? warehouses.Where(x => x.Id == w).Select(x => x.Id).ToList() : warehouses.Select(x => x.Id).ToList();
        var rows = await (from b in db.StockBalances.AsNoTracking()
                          join i in db.Items.AsNoTracking() on b.ItemId equals i.Id
                          join bt in db.Batches.AsNoTracking() on b.BatchId equals bt.Id into bj
                          from bt in bj.DefaultIfEmpty()
                          where whIds.Contains(b.WarehouseId) && b.Qty != 0
                          select new { b.WarehouseId, b.ItemId, i.Name, i.Sku, i.CategoryId, i.BaseUnit, b.Qty, Cost = bt == null ? b.AvgCost : bt.UnitCost, Expires = bt == null ? (DateOnly?)null : bt.ExpiresAt })
            .ToListAsync(ct);
        if (categoryId is { } c) rows = rows.Where(r => r.CategoryId == c).ToList();
        if (!string.IsNullOrWhiteSpace(q)) rows = rows.Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Sku.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        var levels = await db.ItemStockLevels.AsNoTracking().Where(l => whIds.Contains(l.WarehouseId)).ToDictionaryAsync(l => (l.ItemId, l.WarehouseId), ct);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var soon = today.AddDays(expiringDays ?? 30);
        var whNames = warehouses.ToDictionary(x => x.Id, x => x.Name);
        var result = rows.GroupBy(r => (r.WarehouseId, r.ItemId)).Select(g =>
        {
            var f = g.First();
            var qty = g.Sum(x => x.Qty);
            var amount = g.Sum(x => x.Qty * x.Cost);
            var level = levels.GetValueOrDefault((f.ItemId, f.WarehouseId));
            var nearest = g.Where(x => x.Qty > 0 && x.Expires != null).Select(x => x.Expires).Min();
            return new StockBalanceRow(f.WarehouseId, whNames.GetValueOrDefault(f.WarehouseId, "?"), f.ItemId, f.Name, f.Sku, f.CategoryId, f.BaseUnit.ToString(), qty,
                qty != 0 ? decimal.Round(amount / qty, 4) : 0, (long)Math.Round(amount, MidpointRounding.AwayFromZero), level?.MinQty, level?.OptimalQty, nearest,
                level is not null && qty < level.MinQty, nearest is not null && nearest <= soon && nearest >= today, nearest is not null && nearest < today);
        }).ToList();

        // Нулевые остатки ниже минимума тоже важны для «ниже минимума».
        if (belowMin)
        {
            foreach (var l in levels.Values.Where(l => l.MinQty > 0 && !result.Any(r => r.ItemId == l.ItemId && r.WarehouseId == l.WarehouseId)))
            {
                var item = await db.Items.AsNoTracking().FirstAsync(i => i.Id == l.ItemId, ct);
                if (categoryId is { } cc && item.CategoryId != cc) continue;
                result.Add(new StockBalanceRow(l.WarehouseId, whNames.GetValueOrDefault(l.WarehouseId, "?"), item.Id, item.Name, item.Sku, item.CategoryId,
                    item.BaseUnit.ToString(), 0, 0, 0, l.MinQty, l.OptimalQty, null, true, false, false));
            }
            result = result.Where(r => r.BelowMin).ToList();
        }
        if (expiringDays is not null) result = result.Where(r => r.Expiring || r.Expired).ToList();
        return result.OrderBy(r => r.WarehouseName).ThenBy(r => r.ItemName).ToList();
    }

    public async Task<CursorPage<StockMovementRow>> MovementsAsync(Guid? itemId, Guid? warehouseId, DateTimeOffset? from, DateTimeOffset? to, string? cursor, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 500);
        var whIds = (await AccessibleWarehousesAsync(ct)).Select(w => w.Id).ToList();
        var q = db.StockMovements.AsNoTracking().Where(m => whIds.Contains(m.WarehouseId));
        if (itemId is { } i) q = q.Where(m => m.ItemId == i);
        if (warehouseId is { } w) q = q.Where(m => m.WarehouseId == w);
        if (from is { } f) q = q.Where(m => m.MovedAt >= f);
        if (to is { } t) q = q.Where(m => m.MovedAt < t);
        if (Cursor.Decode(cursor) is { } c) q = q.Where(m => m.MovedAt < c.At || (m.MovedAt == c.At && m.Id.CompareTo(c.Id) < 0));
        var rows = await q.OrderByDescending(m => m.MovedAt).ThenByDescending(m => m.Id).Take(limit + 1).ToListAsync(ct);
        var page = rows.Take(limit).ToList();
        return new CursorPage<StockMovementRow>(await MapMovementsAsync(page, ct), rows.Count > limit ? Cursor.Encode(page[^1].MovedAt, page[^1].Id) : null);
    }

    public async Task<ItemCardDto> ItemCardAsync(Guid itemId, CancellationToken ct)
    {
        var item = await db.Items.AsNoTracking().Include(i => i.Units).FirstOrDefaultAsync(i => i.Id == itemId, ct) ?? throw AppException.NotFound("Товар");
        var warehouses = await AccessibleWarehousesAsync(ct);
        var whIds = warehouses.Select(w => w.Id).ToList();
        var rows = await (from b in db.StockBalances.AsNoTracking()
                          join bt in db.Batches.AsNoTracking() on b.BatchId equals bt.Id into bj
                          from bt in bj.DefaultIfEmpty()
                          where b.ItemId == itemId && whIds.Contains(b.WarehouseId) && b.Qty != 0
                          select new { b.WarehouseId, b.BatchId, b.Qty, b.AvgCost, Batch = bt }).ToListAsync(ct);
        var names = warehouses.ToDictionary(w => w.Id, w => w.Name);
        var balances = rows.Select(r => new ItemCardBatch(r.BatchId, r.Batch?.BatchNumber, r.Batch?.SerialNumber, r.Batch?.ExpiresAt, r.WarehouseId,
            names.GetValueOrDefault(r.WarehouseId, "?"), r.Qty, r.Batch?.UnitCost ?? r.AvgCost)).OrderBy(b => b.WarehouseName).ThenBy(b => b.ExpiresAt).ToList();
        var movements = await MovementsAsync(itemId, null, null, null, null, 200, ct);
        return new ItemCardDto(InventoryCatalogService.ToDto(item), balances.Sum(b => b.Qty),
            (long)Math.Round(balances.Sum(b => b.Qty * b.UnitCost), MidpointRounding.AwayFromZero), balances, movements.Items);
    }

    // ================= Вспомогательное =================

    private async Task<List<StockMovementRow>> MapMovementsAsync(List<StockMovement> page, CancellationToken ct)
    {
        var whIds = page.Select(m => m.WarehouseId).Distinct().ToList();
        var itemIds = page.Select(m => m.ItemId).Distinct().ToList();
        var batchIds = page.Where(m => m.BatchId != null).Select(m => m.BatchId!.Value).Distinct().ToList();
        var docIds = page.Select(m => m.DocumentId).Distinct().ToList();
        var wh = await db.Warehouses.AsNoTracking().Where(w => whIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name, ct);
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name, ct);
        var batches = await db.Batches.AsNoTracking().Where(b => batchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.BatchNumber ?? b.SerialNumber, ct);
        var docs = await db.StockDocuments.AsNoTracking().Where(d => docIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Number, ct);
        return page.Select(m => new StockMovementRow(m.Id, m.MovedAt, m.WarehouseId, wh.GetValueOrDefault(m.WarehouseId, "?"), m.ItemId, items.GetValueOrDefault(m.ItemId, "?"),
            m.BatchId, m.BatchId is { } b ? batches.GetValueOrDefault(b) : null, m.Qty, m.UnitCost, m.DocumentType, m.DocumentId, docs.GetValueOrDefault(m.DocumentId, ""))).ToList();
    }

    public async Task<List<Warehouse>> AccessibleWarehousesAsync(CancellationToken ct)
    {
        var q = db.Warehouses.AsNoTracking().Where(w => w.DeletedAt == null);
        if (!user.AllBranches) q = q.Where(w => w.BranchId == null || user.BranchIds.Contains(w.BranchId.Value));
        return await q.OrderBy(w => w.Name).ToListAsync(ct);
    }

    private async Task<List<Guid>> AccessibleWarehouseIdsAsync(CancellationToken ct) => (await AccessibleWarehousesAsync(ct)).Select(w => w.Id).ToList();

    private async Task EnsureDocAccessAsync(StockDocument d, CancellationToken ct)
    {
        if (user.AllBranches) return;
        var allowed = await AccessibleWarehouseIdsAsync(ct);
        if ((d.WarehouseFromId is { } f && allowed.Contains(f)) || (d.WarehouseToId is { } t && allowed.Contains(t))) return;
        throw new AppException(ErrorCodes.BranchForbidden, "Нет доступа к складу документа", 403);
    }

    private void EnsureTypePermission(StockDocumentType type)
    {
        var perm = type switch
        {
            StockDocumentType.Receipt => Perm.Inventory.Receive,
            StockDocumentType.Transfer => Perm.Inventory.TransferCreate,
            StockDocumentType.Writeoff or StockDocumentType.ReturnToSupplier => Perm.Inventory.Writeoff,
            StockDocumentType.Inventory => Perm.Inventory.Count,
            _ => Perm.Inventory.View,
        };
        user.EnsurePermission(perm);
    }

    private async Task<Warehouse> GetWarehouseForOutAsync(Guid? id, CancellationToken ct)
    {
        if (id is null) throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Выберите склад");
        var w = await db.Warehouses.FirstAsync(x => x.Id == id, ct);
        if (w.Locked) throw AppException.Conflict(ErrorCodes.WarehouseLocked, $"Склад «{w.Name}» заблокирован на время инвентаризации");
        return w;
    }

    private async Task ValidateWarehousesAsync(StockDocument d, CancellationToken ct)
    {
        switch (d.Type)
        {
            case StockDocumentType.Receipt when d.WarehouseToId is null:
                throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Выберите склад прихода");
            case StockDocumentType.Writeoff or StockDocumentType.ReturnToSupplier or StockDocumentType.Inventory when d.WarehouseFromId is null:
                throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Выберите склад");
            case StockDocumentType.Transfer when d.WarehouseFromId is null || d.WarehouseToId is null:
                throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Выберите склады отправителя и получателя");
            case StockDocumentType.Transfer when d.WarehouseFromId == d.WarehouseToId:
                throw AppException.BadRequest(ErrorCodes.SameWarehouse, "Склады отправителя и получателя совпадают");
        }
        var allowed = await AccessibleWarehouseIdsAsync(ct);
        // Для перемещения достаточно доступа к складу-отправителю.
        var check = d.Type == StockDocumentType.Transfer ? d.WarehouseFromId : d.WarehouseFromId ?? d.WarehouseToId;
        if (check is { } c && !allowed.Contains(c)) throw new AppException(ErrorCodes.BranchForbidden, "Нет доступа к складу", 403);
    }

    private async Task<Guid?> BranchOfDocAsync(StockDocument d, CancellationToken ct)
    {
        var id = d.Type == StockDocumentType.Receipt ? d.WarehouseToId : d.WarehouseFromId ?? d.WarehouseToId;
        return id is null ? null : await db.Warehouses.Where(w => w.Id == id).Select(w => w.BranchId).FirstOrDefaultAsync(ct);
    }

    private async Task SetLinesAsync(StockDocument doc, IReadOnlyList<StockLineInput> lines, CancellationToken ct)
    {
        doc.Lines.Clear();
        foreach (var l in lines) doc.Lines.Add(await NewLineAsync(doc, l, ct));
        doc.TotalCost = doc.Lines.Sum(x => x.TotalCost);
    }

    private async Task<StockDocumentLine> NewLineAsync(StockDocument doc, StockLineInput l, CancellationToken ct, decimal? expected = null)
    {
        var item = await db.Items.Include(i => i.Units).AsNoTracking().FirstOrDefaultAsync(i => i.Id == l.ItemId && i.DeletedAt == null, ct) ?? throw AppException.NotFound("Товар");
        var qty = item.ToBase(l.Qty, l.UnitId);
        var factor = l.UnitId is { } uid ? item.Units.First(u => u.Id == uid).FactorToBase : 1m;
        // Цена вводится за единицу ввода — храним себестоимость за базовую единицу.
        var unitCost = l.UnitCost is { } c ? decimal.Round(c / factor, 4) : 0m;
        if (doc.Type is StockDocumentType.Writeoff or StockDocumentType.Transfer or StockDocumentType.ReturnToSupplier && l.UnitCost is null && l.BatchId is { } bid)
            unitCost = await db.Batches.Where(b => b.Id == bid).Select(b => b.UnitCost).FirstAsync(ct);
        return new StockDocumentLine
        {
            DocumentId = doc.Id, ItemId = item.Id, BatchId = l.BatchId, Qty = qty, QtyInput = l.Qty, UnitId = l.UnitId, UnitCost = unitCost,
            TotalCost = (long)Math.Round(qty * unitCost, MidpointRounding.AwayFromZero), BatchNumber = l.BatchNumber.NullIfEmpty(),
            SerialNumber = l.SerialNumber.NullIfEmpty(), ExpiresAt = l.ExpiresAt, ExpectedQty = expected, ActualQty = l.ActualQty,
        };
    }

    private async Task<Dictionary<Guid, Item>> ItemsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Items.AsNoTracking().Where(i => list.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
    }

    private async Task<StockDocument> LockDocAsync(Guid id, CancellationToken ct)
    {
        await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM stock_documents WHERE id = {id} FOR UPDATE").ToListAsync(ct);
        return await db.StockDocuments.Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw AppException.NotFound("Документ");
    }

    private async Task<IDbContextTransaction?> BeginAsync(CancellationToken ct) =>
        db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct) : null;

    private void MarkPosted(StockDocument doc)
    {
        doc.Status = StockDocumentStatus.Posted;
        doc.PostedAt = clock.GetUtcNow();
        doc.PostedBy = user.IsAuthenticated ? user.UserId : null;
    }

    private async Task<string> NextNumberAsync(StockDocumentType type, CancellationToken ct) =>
        $"{StockDocument.Prefix(type)}-{await db.NextNumberAsync("stock:" + type, ct):D6}";

    private sealed record DocNames(Dictionary<Guid, string> Warehouses, Dictionary<Guid, string> Suppliers, Dictionary<Guid, string> Reasons, Dictionary<Guid, string> Users);

    private async Task<DocNames> NamesAsync(IReadOnlyList<StockDocument> docs, CancellationToken ct)
    {
        var wh = docs.SelectMany(d => new[] { d.WarehouseFromId, d.WarehouseToId }).Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
        var sp = docs.Where(d => d.SupplierId != null).Select(d => d.SupplierId!.Value).Distinct().ToList();
        var rs = docs.Where(d => d.ReasonId != null).Select(d => d.ReasonId!.Value).Distinct().ToList();
        var us = docs.SelectMany(d => new[] { d.CreatedBy, d.PostedBy }).Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
        return new DocNames(
            await db.Warehouses.AsNoTracking().Where(w => wh.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name, ct),
            await db.Suppliers.AsNoTracking().Where(s => sp.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct),
            await db.WriteoffReasons.AsNoTracking().Where(r => rs.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, ct),
            await db.Users.AsNoTracking().Where(u => us.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct));
    }

    private async Task<StockDocumentDto> MapAsync(StockDocument d, CancellationToken ct)
    {
        var names = await NamesAsync([d], ct);
        var itemIds = d.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Include(i => i.Units).Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var lines = d.Lines.OrderBy(l => items.GetValueOrDefault(l.ItemId)?.Name).ThenBy(l => l.ExpiresAt).Select(l =>
        {
            var item = items.GetValueOrDefault(l.ItemId);
            var unit = l.UnitId is { } u ? item?.Units.FirstOrDefault(x => x.Id == u)?.UnitName : null;
            return new StockDocumentLineDto(l.Id, l.ItemId, item?.Name ?? "?", item?.Sku ?? "", item?.BaseUnit.ToString() ?? "", l.BatchId, l.BatchNumber, l.SerialNumber,
                l.ExpiresAt, l.Qty, l.QtyInput, l.UnitId, unit, l.UnitCost, l.TotalCost, l.ExpectedQty, l.ActualQty);
        }).ToList();
        return new StockDocumentDto(d.Id, d.Type, d.Number, d.Status, d.BranchId, d.WarehouseFromId, d.WarehouseFromId is { } f ? names.Warehouses.GetValueOrDefault(f) : null,
            d.WarehouseToId, d.WarehouseToId is { } t ? names.Warehouses.GetValueOrDefault(t) : null, d.SupplierId, d.SupplierId is { } s ? names.Suppliers.GetValueOrDefault(s) : null,
            d.PurchaseOrderId, d.VisitId, d.SourceDocumentId, d.InvoiceNumber, d.InvoiceDate, d.ReasonId, d.ReasonId is { } r ? names.Reasons.GetValueOrDefault(r) : null,
            d.Comment, d.CreatedAt, d.CreatedBy, d.CreatedBy is { } cb ? names.Users.GetValueOrDefault(cb) : null, d.PostedAt,
            d.PostedBy is { } pb ? names.Users.GetValueOrDefault(pb) : null, d.ReceivedAt, d.TotalCost, d.Version, lines);
    }
}

/// <summary>Подтверждение списаний и недостач при перемещении.</summary>
public sealed class WriteoffApprovalHandler(IAppDbContext db, StockService stock) : IApprovalHandler
{
    public ApprovalType Type => ApprovalType.Writeoff;

    public async Task OnApprovedAsync(ApprovalRequest request, CancellationToken ct)
    {
        var doc = await db.StockDocuments.Include(d => d.Lines).FirstAsync(d => d.Id == request.EntityId, ct);
        if (doc.Status != StockDocumentStatus.PendingApproval) return;
        await stock.CompleteWriteoffAsync(doc, ct);
    }

    public async Task OnRejectedAsync(ApprovalRequest request, CancellationToken ct)
    {
        var doc = await db.StockDocuments.FirstAsync(d => d.Id == request.EntityId, ct);
        if (doc.Status == StockDocumentStatus.PendingApproval) doc.Status = StockDocumentStatus.Draft;
    }
}

public sealed class TransferShortageApprovalHandler(IAppDbContext db, StockService stock) : IApprovalHandler
{
    public ApprovalType Type => ApprovalType.TransferShortage;

    public async Task OnApprovedAsync(ApprovalRequest request, CancellationToken ct)
    {
        var doc = await db.StockDocuments.Include(d => d.Lines).FirstAsync(d => d.Id == request.EntityId, ct);
        if (doc.Status != StockDocumentStatus.PendingApproval) return;
        await stock.CompleteWriteoffAsync(doc, ct);
    }

    /// <summary>Недостачу нельзя «отменить»: при отклонении документ остаётся на разбор (черновик), товар по-прежнему не пришёл.</summary>
    public async Task OnRejectedAsync(ApprovalRequest request, CancellationToken ct)
    {
        var doc = await db.StockDocuments.FirstAsync(d => d.Id == request.EntityId, ct);
        if (doc.Status == StockDocumentStatus.PendingApproval) doc.Status = StockDocumentStatus.Draft;
    }
}
