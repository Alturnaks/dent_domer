using System.Globalization;
using System.Text;
using Dental.Application.Common;
using Dental.Application.Inventory;
using Dental.Application.Orgs;
using Dental.Application.Permissions;
using Dental.Domain.Common;
using Dental.Domain.Inventory;
using Dental.Domain.Organizations;
using Dental.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace Dental.Application.Purchasing;

/// <summary>
/// Заказы поставщикам: черновик → отправка (сверх порога — подтверждение) → приёмка приходными документами → получен.
/// Приход по заказу обновляет received_qty и статус заказа (StockService.PostReceiptAsync).
/// </summary>
public sealed class PurchaseOrderService(
    IAppDbContext db,
    ICurrentUser user,
    IAuditService audit,
    ApprovalService approvals,
    StockService stock,
    ITableDocumentRenderer renderer,
    TimeProvider clock)
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public async Task<PagedResult<PurchaseOrderListItem>> ListAsync(PurchaseOrderStatus? status, Guid? supplierId, Guid? warehouseId, string? q, PageQuery page,
        CancellationToken ct)
    {
        var query = await VisibleOrdersAsync(ct);
        if (status is { } s) query = query.Where(o => o.Status == s);
        if (supplierId is { } sp) query = query.Where(o => o.SupplierId == sp);
        if (warehouseId is { } w) query = query.Where(o => o.WarehouseId == w);
        if (q.NullIfEmpty() is { } text) query = query.Where(o => EF.Functions.ILike(o.Number, $"%{text}%"));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(o => o.CreatedAt).Skip((page.SafePage - 1) * page.SafeSize).Take(page.SafeSize)
            .Select(o => new
            {
                o,
                Count = o.Lines.Count,
                Qty = o.Lines.Sum(l => (decimal?)l.Qty) ?? 0,
                Received = o.Lines.Sum(l => (decimal?)(l.ReceivedQty > l.Qty ? l.Qty : l.ReceivedQty)) ?? 0,
            }).ToListAsync(ct);
        var suppliers = await SupplierNamesAsync(rows.Select(r => r.o.SupplierId), ct);
        var warehouses = await WarehouseNamesAsync(rows.Select(r => r.o.WarehouseId), ct);
        var items = rows.Select(r => new PurchaseOrderListItem(r.o.Id, r.o.Number, r.o.Status, r.o.SupplierId, suppliers.GetValueOrDefault(r.o.SupplierId, "?"),
            r.o.WarehouseId, warehouses.GetValueOrDefault(r.o.WarehouseId, "?"), r.o.ExpectedAt, r.o.Total, r.o.SentVia, r.o.SentAt, r.o.CreatedAt, r.Count,
            r.Qty > 0 ? decimal.Round(r.Received / r.Qty * 100, 1) : 0)).ToList();
        return new PagedResult<PurchaseOrderListItem>(items, total, page.SafePage, page.SafeSize);
    }

    public async Task<PurchaseOrderDto> GetAsync(Guid id, CancellationToken ct)
    {
        var order = await (await VisibleOrdersAsync(ct)).Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw AppException.NotFound("Заказ");
        return await MapAsync(order, ct);
    }

    public async Task<PurchaseOrderDto> CreateAsync(CreatePurchaseOrderRequest r, CancellationToken ct)
    {
        var order = await NewOrderAsync(r.SupplierId, r.WarehouseId, r.ExpectedAt, r.Comment,
            r.Lines.Select(l => (l.ItemId, l.Qty, l.UnitPrice)), ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(order.Id, ct);
    }

    /// <summary>Новый черновик заказа (без сохранения — вызывающий сохраняет в своей транзакции). Цена по умолчанию — последняя цена поставщика.</summary>
    public async Task<PurchaseOrder> NewOrderAsync(Guid supplierId, Guid warehouseId, DateOnly? expectedAt, string? comment,
        IEnumerable<(Guid ItemId, decimal Qty, long? UnitPrice)> lines, CancellationToken ct)
    {
        user.EnsurePermission(Perm.Purchase.OrderCreate);
        await EnsureSupplierAsync(supplierId, ct);
        await EnsureWarehouseAsync(warehouseId, ct);
        var order = new PurchaseOrder
        {
            SupplierId = supplierId,
            WarehouseId = warehouseId,
            ExpectedAt = expectedAt,
            Comment = comment.NullIfEmpty(),
        };
        await SetLinesAsync(order, lines.ToList(), ct);
        order.Number = $"ЗКП-{await db.NextNumberAsync("purchase_order", ct):D6}";
        db.PurchaseOrders.Add(order);
        return order;
    }

    public async Task<PurchaseOrderDto> UpdateAsync(Guid id, UpdatePurchaseOrderRequest r, CancellationToken ct)
    {
        var order = await LoadAsync(id, ct);
        db.SetExpectedVersion(order, r.Version);
        if (order.Status != PurchaseOrderStatus.Draft) throw AppException.Conflict(ErrorCodes.DocumentNotEditable, "Редактировать можно только черновик заказа");
        if (r.SupplierId is { } s && s != order.SupplierId)
        {
            await EnsureSupplierAsync(s, ct);
            order.SupplierId = s;
        }
        if (r.WarehouseId is { } w && w != order.WarehouseId)
        {
            await EnsureWarehouseAsync(w, ct);
            order.WarehouseId = w;
        }
        if (r.ExpectedAt is not null) order.ExpectedAt = r.ExpectedAt;
        if (r.Comment is not null) order.Comment = r.Comment.NullIfEmpty();
        if (r.Lines is not null)
        {
            if (r.Lines.Any(l => l.Qty <= 0)) throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Количество должно быть больше нуля");
            await SetLinesAsync(order, r.Lines.Select(l => (l.ItemId, l.Qty, l.UnitPrice)).ToList(), ct);
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Отправка поставщику (фиксируется способ). Сумма выше порога организации → статус pending_approval и запрос на подтверждение;
    /// после подтверждения заказ считается отправленным. Повторная отправка отправленного заказа только обновляет способ и время.
    /// </summary>
    public async Task<PurchaseOrderDto> SendAsync(Guid id, SendPurchaseOrderRequest r, CancellationToken ct)
    {
        var order = await LoadAsync(id, ct);
        db.SetExpectedVersion(order, r.Version);
        if (order.Status is PurchaseOrderStatus.Sent or PurchaseOrderStatus.PartiallyReceived)
        {
            order.SentVia = r.SentVia;
            order.SentAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return await GetAsync(id, ct);
        }
        if (order.Status != PurchaseOrderStatus.Draft) throw AppException.Conflict(ErrorCodes.DocumentInvalidState, "Отправить можно только черновик заказа");
        if (order.Lines.Count == 0) throw AppException.BadRequest(ErrorCodes.LinesRequired, "Добавьте хотя бы одну позицию");
        order.SentVia = r.SentVia;
        var org = await db.Organizations.AsNoTracking().FirstAsync(ct);
        if (order.Total > org.Settings.PurchaseOrderApprovalThreshold)
        {
            order.Status = PurchaseOrderStatus.PendingApproval;
            var supplier = await db.Suppliers.AsNoTracking().Where(s => s.Id == order.SupplierId).Select(s => s.Name).FirstAsync(ct);
            var branchId = await db.Warehouses.Where(w => w.Id == order.WarehouseId).Select(w => w.BranchId).FirstOrDefaultAsync(ct);
            await approvals.RequestAsync(ApprovalType.PurchaseOrder, nameof(PurchaseOrder), order.Id, order.Total,
                $"Заказ {order.Number} поставщику «{supplier}» на {new Money(order.Total)} (порог {new Money(org.Settings.PurchaseOrderApprovalThreshold)})",
                new { order.Number, supplier, order.Total, lines = order.Lines.Count, sentVia = r.SentVia }, branchId, ct);
        }
        else
        {
            MarkSent(order, clock.GetUtcNow());
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    internal static void MarkSent(PurchaseOrder order, DateTimeOffset now)
    {
        order.Status = PurchaseOrderStatus.Sent;
        order.SentAt = now;
        order.RecalculateStatus();
    }

    public async Task<PurchaseOrderDto> CancelAsync(Guid id, PurchaseOrderActionRequest r, CancellationToken ct)
    {
        var order = await LoadAsync(id, ct);
        db.SetExpectedVersion(order, r.Version);
        if (order.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.PendingApproval or PurchaseOrderStatus.Sent))
            throw AppException.Conflict(ErrorCodes.DocumentInvalidState, "Заказ в этом статусе нельзя отменить");
        if (order.Lines.Any(l => l.ReceivedQty > 0)) throw AppException.Conflict(ErrorCodes.DocumentInvalidState, "По заказу уже есть приёмка");
        var wasSent = order.Status == PurchaseOrderStatus.Sent;
        if (wasSent && r.Comment.NullIfEmpty() is null) throw AppException.BadRequest(ErrorCodes.CommentRequired, "Укажите причину отмены отправленного заказа");
        var pending = await db.ApprovalRequests.Where(a => a.EntityId == order.Id && a.Status == ApprovalStatus.Pending).ToListAsync(ct);
        foreach (var p in pending) { p.Status = ApprovalStatus.Rejected; p.Comment = "Заказ отменён"; p.DecidedAt = clock.GetUtcNow(); p.DecidedBy = user.UserId; }
        var drafts = await db.StockDocuments.Where(d => d.PurchaseOrderId == order.Id && d.Status == StockDocumentStatus.Draft).ToListAsync(ct);
        foreach (var d in drafts) d.Status = StockDocumentStatus.Cancelled;
        order.Status = PurchaseOrderStatus.Cancelled;
        if (r.Comment.NullIfEmpty() is { } c) order.Comment = string.IsNullOrWhiteSpace(order.Comment) ? "Отмена: " + c : order.Comment + "\nОтмена: " + c;
        if (wasSent) audit.Log(nameof(PurchaseOrder), order.Id, "cancel_sent", new { order.Number, order.Total }, r.Comment);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Приёмка по заказу: черновик прихода на неполученный остаток по цене заказа (или уже открытый черновик прихода по этому заказу).
    /// Проведение прихода обновит received_qty и статус заказа.
    /// </summary>
    public async Task<StockDocumentDto> ReceiveAsync(Guid id, CancellationToken ct)
    {
        user.EnsurePermission(Perm.Inventory.Receive);
        var order = await LoadAsync(id, ct);
        if (order.Status is not (PurchaseOrderStatus.Sent or PurchaseOrderStatus.PartiallyReceived))
            throw AppException.Conflict(ErrorCodes.DocumentInvalidState, "Принять можно только отправленный заказ");
        var draft = await db.StockDocuments.AsNoTracking().Where(d => d.PurchaseOrderId == order.Id && d.Status == StockDocumentStatus.Draft)
            .OrderByDescending(d => d.CreatedAt).Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct);
        if (draft is { } existing) return await stock.GetAsync(existing, ct);
        var lines = order.Lines.Where(l => l.Qty > l.ReceivedQty)
            .Select(l => new StockLineInput(l.ItemId, l.Qty - l.ReceivedQty, null, null, l.UnitPrice, null, null, null, null)).ToList();
        if (lines.Count == 0) throw AppException.Conflict(ErrorCodes.DocumentInvalidState, "Заказ уже получен полностью");
        return await stock.CreateAsync(new CreateStockDocumentRequest(StockDocumentType.Receipt, null, order.WarehouseId, order.SupplierId, order.Id,
            null, null, null, $"Приёмка по заказу {order.Number}", lines), ct);
    }

    // ================= Экспорт =================

    public async Task<ExportFile> ExportAsync(Guid id, string? format, CancellationToken ct)
    {
        var o = await GetAsync(id, ct);
        var org = await db.Organizations.AsNoTracking().FirstAsync(ct);
        var address = await (from w in db.Warehouses.AsNoTracking()
                             join b in db.Branches.AsNoTracking() on w.BranchId equals b.Id
                             where w.Id == o.WarehouseId
                             select b.Address).FirstOrDefaultAsync(ct);
        var fileBase = o.Number;
        switch ((format ?? "text").Trim().ToLowerInvariant())
        {
            case "text" or "txt" or "whatsapp":
                return new ExportFile(Encoding.UTF8.GetBytes(ToText(o, org.Name, address)), "text/plain; charset=utf-8", fileBase + ".txt");
            case "csv":
                return new ExportFile(ToCsv(o), "text/csv; charset=utf-8", fileBase + ".csv");
            case "xlsx" or "excel":
                return new ExportFile(renderer.ToXlsx(ToTable(o, org.Name, address)), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileBase + ".xlsx");
            case "pdf":
                return new ExportFile(renderer.ToPdf(ToTable(o, org.Name, address)), "application/pdf", fileBase + ".pdf");
            default:
                throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Формат выгрузки: text, csv, xlsx или pdf");
        }
    }

    private static string Qty(decimal q) => q.ToString("0.###", Ru);

    private static string UnitShort(string baseUnit) => baseUnit switch
    {
        "Pcs" => "шт",
        "G" => "г",
        "Ml" => "мл",
        "Pack" => "уп",
        _ => baseUnit,
    };

    /// <summary>Текст для WhatsApp/почты: нумерованный список позиций с ценами и итог.</summary>
    internal static string ToText(PurchaseOrderDto o, string orgName, string? address)
    {
        var sb = new StringBuilder();
        sb.Append("Заказ ").Append(o.Number).Append(" от ").AppendLine(o.CreatedAt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
        sb.Append("Поставщик: ").AppendLine(o.SupplierName);
        sb.Append("Покупатель: ").AppendLine(orgName);
        sb.Append("Доставка: ").Append(o.WarehouseName);
        if (!string.IsNullOrWhiteSpace(address)) sb.Append(", ").Append(address);
        sb.AppendLine();
        if (o.ExpectedAt is { } d) sb.Append("Желаемая дата поставки: ").AppendLine(d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
        sb.AppendLine();
        var i = 0;
        foreach (var l in o.Lines)
        {
            sb.Append(++i).Append(". ").Append(l.ItemName);
            var sku = l.SupplierSku ?? l.ItemSku;
            if (!string.IsNullOrWhiteSpace(sku)) sb.Append(" (арт. ").Append(sku).Append(')');
            sb.Append(" — ").Append(Qty(l.Qty)).Append(' ').Append(UnitShort(l.BaseUnit));
            if (l.UnitPrice > 0) sb.Append(" × ").Append(new Money(l.UnitPrice)).Append(" = ").Append(new Money(l.Total));
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.Append("Итого: ").AppendLine(new Money(o.Total).ToString());
        if (!string.IsNullOrWhiteSpace(o.Comment)) sb.Append("Комментарий: ").AppendLine(o.Comment);
        return sb.ToString().TrimEnd();
    }

    /// <summary>CSV для Excel (разделитель «;», UTF-8 с BOM).</summary>
    private static byte[] ToCsv(PurchaseOrderDto o)
    {
        static string Esc(string? s) => s is null ? "" : s.Contains(';') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : s;
        var sb = new StringBuilder();
        sb.AppendLine("№;Артикул;Наименование;Ед.;Количество;Цена, ₸;Сумма, ₸");
        var i = 0;
        foreach (var l in o.Lines)
        {
            sb.Append(++i).Append(';').Append(Esc(l.SupplierSku ?? l.ItemSku)).Append(';').Append(Esc(l.ItemName)).Append(';').Append(UnitShort(l.BaseUnit)).Append(';')
                .Append(Qty(l.Qty)).Append(';').Append((l.UnitPrice / 100m).ToString("0.00", Ru)).Append(';').Append((l.Total / 100m).ToString("0.00", Ru)).AppendLine();
        }
        sb.Append(";;Итого;;;;").Append((o.Total / 100m).ToString("0.00", Ru)).AppendLine();
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static TableDocument ToTable(PurchaseOrderDto o, string orgName, string? address)
    {
        var header = new List<string>
        {
            $"Дата: {o.CreatedAt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}",
            $"Поставщик: {o.SupplierName}",
            $"Покупатель: {orgName}",
            $"Доставка: {o.WarehouseName}{(string.IsNullOrWhiteSpace(address) ? "" : ", " + address)}",
        };
        if (o.ExpectedAt is { } d) header.Add($"Желаемая дата поставки: {d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}");
        var columns = new List<TableColumn>
        {
            new("№", true, "0", 0.4f), new("Артикул", RelativeWidth: 1.2f), new("Наименование", RelativeWidth: 4), new("Ед.", RelativeWidth: 0.6f),
            new("Кол-во", true, "#,##0.###", 1), new("Цена, ₸", true, "#,##0.00", 1.2f), new("Сумма, ₸", true, "#,##0.00", 1.3f),
        };
        var rows = o.Lines.Select((l, i) => (IReadOnlyList<object?>)new object?[]
        {
            i + 1, l.SupplierSku ?? l.ItemSku, l.ItemName, UnitShort(l.BaseUnit), l.Qty, l.UnitPrice / 100m, l.Total / 100m,
        }).ToList();
        var footer = new List<string> { $"Итого: {new Money(o.Total)}" };
        if (!string.IsNullOrWhiteSpace(o.Comment)) footer.Add($"Комментарий: {o.Comment}");
        return new TableDocument($"Заказ поставщику {o.Number}", header, columns, rows, footer);
    }

    // ================= Вспомогательное =================

    private async Task<IQueryable<PurchaseOrder>> VisibleOrdersAsync(CancellationToken ct)
    {
        var q = db.PurchaseOrders.AsNoTracking();
        if (user.AllBranches) return q;
        var allowed = (await stock.AccessibleWarehousesAsync(ct)).Select(w => w.Id).ToList();
        return q.Where(o => allowed.Contains(o.WarehouseId));
    }

    private async Task<PurchaseOrder> LoadAsync(Guid id, CancellationToken ct)
    {
        var order = await db.PurchaseOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw AppException.NotFound("Заказ");
        await EnsureWarehouseAsync(order.WarehouseId, ct);
        return order;
    }

    private async Task EnsureSupplierAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Suppliers.AnyAsync(s => s.Id == id && s.DeletedAt == null, ct)) throw AppException.NotFound("Поставщик");
    }

    private async Task EnsureWarehouseAsync(Guid id, CancellationToken ct)
    {
        var w = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Склад");
        if (w.BranchId is { } b) user.EnsureBranchAccess(b);
    }

    private async Task SetLinesAsync(PurchaseOrder order, IReadOnlyList<(Guid ItemId, decimal Qty, long? UnitPrice)> input, CancellationToken ct)
    {
        var itemIds = input.Select(l => l.ItemId).Distinct().ToList();
        var found = await db.Items.AsNoTracking().CountAsync(i => itemIds.Contains(i.Id) && i.DeletedAt == null, ct);
        if (found != itemIds.Count) throw AppException.NotFound("Товар");
        var prices = await db.SupplierItems.AsNoTracking().Where(s => s.SupplierId == order.SupplierId && itemIds.Contains(s.ItemId))
            .ToDictionaryAsync(s => s.ItemId, s => s.LastPrice, ct);
        order.Lines.Clear();
        foreach (var l in input.Where(l => l.Qty > 0))
        {
            order.Lines.Add(new PurchaseOrderLine
            {
                OrderId = order.Id, ItemId = l.ItemId, Qty = l.Qty, UnitPrice = l.UnitPrice ?? prices.GetValueOrDefault(l.ItemId),
            });
        }
        order.Total = order.Lines.Sum(LineTotal);
    }

    private static long LineTotal(PurchaseOrderLine l) => (long)Math.Round(l.Qty * l.UnitPrice, MidpointRounding.AwayFromZero);

    private async Task<Dictionary<Guid, string>> SupplierNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Suppliers.AsNoTracking().Where(s => list.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
    }

    private async Task<Dictionary<Guid, string>> WarehouseNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Warehouses.AsNoTracking().Where(w => list.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Name, ct);
    }

    private async Task<PurchaseOrderDto> MapAsync(PurchaseOrder o, CancellationToken ct)
    {
        var supplier = await db.Suppliers.AsNoTracking().FirstAsync(s => s.Id == o.SupplierId, ct);
        var warehouse = await db.Warehouses.AsNoTracking().FirstAsync(w => w.Id == o.WarehouseId, ct);
        var itemIds = o.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var skus = await db.SupplierItems.AsNoTracking().Where(s => s.SupplierId == o.SupplierId && itemIds.Contains(s.ItemId))
            .ToDictionaryAsync(s => s.ItemId, s => s.SupplierSku, ct);
        var lines = o.Lines.OrderBy(l => items.GetValueOrDefault(l.ItemId)?.Name).Select(l =>
        {
            var item = items.GetValueOrDefault(l.ItemId);
            return new PurchaseOrderLineDto(l.Id, l.ItemId, item?.Name ?? "?", item?.Sku ?? "", item?.BaseUnit.ToString() ?? "", skus.GetValueOrDefault(l.ItemId).NullIfEmpty(),
                l.Qty, l.UnitPrice, LineTotal(l), l.ReceivedQty, Math.Max(0, l.Qty - l.ReceivedQty));
        }).ToList();
        var receipts = await db.StockDocuments.AsNoTracking().Where(d => d.PurchaseOrderId == o.Id).OrderBy(d => d.CreatedAt)
            .Select(d => new PurchaseOrderDocRef(d.Id, d.Number, d.Status, d.CreatedAt, d.PostedAt, d.TotalCost)).ToListAsync(ct);
        var invoices = await db.SupplierInvoices.AsNoTracking().Where(i => i.PurchaseOrderId == o.Id).OrderBy(i => i.Date)
            .Select(i => new PurchaseOrderInvoiceRef(i.Id, i.Number, i.Date, i.Amount, i.PaidAmount, i.Status)).ToListAsync(ct);
        var threshold = (await db.Organizations.AsNoTracking().FirstAsync(ct)).Settings.PurchaseOrderApprovalThreshold;
        var createdBy = o.CreatedBy is { } cb ? await db.Users.AsNoTracking().Where(u => u.Id == cb).Select(u => u.FullName).FirstOrDefaultAsync(ct) : null;
        return new PurchaseOrderDto(o.Id, o.Number, o.Status, o.SupplierId, supplier.Name, supplier.Phone, supplier.Whatsapp, supplier.Email, o.WarehouseId,
            warehouse.Name, warehouse.BranchId, o.ExpectedAt, o.Total, o.SentVia, o.SentAt, o.Comment, o.CreatedAt, createdBy, o.Version,
            o.Total > threshold, threshold, lines, receipts, invoices);
    }
}

/// <summary>Подтверждение заказа поставщику сверх порога: одобрен → отправлен, отклонён → снова черновик.</summary>
public sealed class PurchaseOrderApprovalHandler(IAppDbContext db, TimeProvider clock) : IApprovalHandler
{
    public ApprovalType Type => ApprovalType.PurchaseOrder;

    public async Task OnApprovedAsync(ApprovalRequest request, CancellationToken ct)
    {
        var order = await db.PurchaseOrders.Include(o => o.Lines).FirstAsync(o => o.Id == request.EntityId, ct);
        if (order.Status != PurchaseOrderStatus.PendingApproval) return;
        PurchaseOrderService.MarkSent(order, clock.GetUtcNow());
    }

    public async Task OnRejectedAsync(ApprovalRequest request, CancellationToken ct)
    {
        var order = await db.PurchaseOrders.FirstAsync(o => o.Id == request.EntityId, ct);
        if (order.Status == PurchaseOrderStatus.PendingApproval)
        {
            order.Status = PurchaseOrderStatus.Draft;
            order.SentVia = null;
        }
    }
}
