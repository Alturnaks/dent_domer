using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Branches;
using Dental.Inventory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Dental.Data.Seed;

/// <summary>
/// Демо-данные склада: причины списания, склады (центральный + по филиалу), категории, ~55 позиций номенклатуры,
/// 5 поставщиков с ценами, нормы остатков, проведённые приходы (есть истекающие/просроченные партии и позиции ниже минимума),
/// перемещение в пути и черновик списания. Идемпотентно: выполняется, только если номенклатуры ещё нет.
/// </summary>
[ExposeServices(typeof(IDentalDemoModuleSeeder), IncludeSelf = true)]
public class InventoryDemoDataSeedContributor : IDentalDemoModuleSeeder, ITransientDependency
{
    private sealed record ItemDef(string Sku, string Name, string Manufacturer, string Category, BaseUnit Unit, string? PackName, decimal PackFactor,
        long PackPriceTenge, bool Batches = false, bool Expiry = false, bool Serials = false, int ShelfMonths = 0);

    private static readonly ItemDef[] ItemDefs =
    [
        new("A-001", "Ультракаин Д-С форте 1:100 000, карпула 1,7 мл", "Sanofi", "Анестетики", BaseUnit.Pcs, "упаковка (100 карп.)", 100, 38_000, true, true, ShelfMonths: 24),
        new("A-002", "Септанест 1:100 000, карпула 1,7 мл", "Septodont", "Анестетики", BaseUnit.Pcs, "упаковка (50 карп.)", 50, 21_000, true, true, ShelfMonths: 24),
        new("A-003", "Убистезин форте, карпула 1,7 мл", "3M", "Анестетики", BaseUnit.Pcs, "упаковка (50 карп.)", 50, 20_500, true, true, ShelfMonths: 24),
        new("A-004", "Лидокаин спрей 10%, 38 г", "Egis", "Анестетики", BaseUnit.Pcs, null, 1, 4_500, true, true, ShelfMonths: 24),
        new("A-005", "Иглы карпульные 30G 0,3×21 мм", "Septoject", "Анестетики", BaseUnit.Pcs, "упаковка (100 шт.)", 100, 6_000),
        new("A-006", "Бензокаин гель 20%, 30 г", "Premier", "Анестетики", BaseUnit.Pcs, null, 1, 3_800, true, true, ShelfMonths: 18),
        new("F-001", "Filtek Z250 A2, шприц 4 г", "3M", "Пломбировочные материалы", BaseUnit.G, "шприц 4 г", 4, 12_000, true, true, ShelfMonths: 30),
        new("F-002", "Filtek Z250 A3, шприц 4 г", "3M", "Пломбировочные материалы", BaseUnit.G, "шприц 4 г", 4, 12_000, true, true, ShelfMonths: 30),
        new("F-003", "Estelite Sigma Quick A2, шприц 4 г", "Tokuyama", "Пломбировочные материалы", BaseUnit.G, "шприц 4 г", 4, 11_600, true, true, ShelfMonths: 30),
        new("F-004", "Filtek Ultimate Flowable A2, шприц 2 г", "3M", "Пломбировочные материалы", BaseUnit.G, "шприц 2 г", 2, 9_000, true, true, ShelfMonths: 30),
        new("F-005", "Адгезив Single Bond Universal, 5 мл", "3M", "Пломбировочные материалы", BaseUnit.Ml, "флакон 5 мл", 5, 25_000, true, true, ShelfMonths: 24),
        new("F-006", "Гель для травления 37%, шприц 5 мл", "Voco", "Пломбировочные материалы", BaseUnit.Ml, "шприц 5 мл", 5, 2_500),
        new("F-007", "Стеклоиономерный цемент Fuji IX GP, 15 г", "GC", "Пломбировочные материалы", BaseUnit.G, "упаковка 15 г", 15, 18_000, true, true, ShelfMonths: 36),
        new("F-008", "Дентин-паста (временная пломба), 50 г", "ВладМиВа", "Пломбировочные материалы", BaseUnit.G, "банка 50 г", 50, 2_500),
        new("F-009", "Кальцимол LC (прокладка), шприц 2,5 г", "Voco", "Пломбировочные материалы", BaseUnit.G, "шприц 2,5 г", 2.5m, 6_500, true, true, ShelfMonths: 24),
        new("E-001", "Файлы ProTaper Gold, 25 мм", "Dentsply Sirona", "Эндодонтия", BaseUnit.Pcs, "блистер (6 шт.)", 6, 18_000),
        new("E-002", "K-файлы №15–40, 25 мм", "Mani", "Эндодонтия", BaseUnit.Pcs, "блистер (6 шт.)", 6, 2_400),
        new("E-003", "Гуттаперчевые штифты 04", "Dentsply Sirona", "Эндодонтия", BaseUnit.Pcs, "упаковка (120 шт.)", 120, 3_600),
        new("E-004", "Силер AH Plus, 2×4 мл", "Dentsply Sirona", "Эндодонтия", BaseUnit.Ml, "набор 8 мл", 8, 22_000, true, true, ShelfMonths: 24),
        new("E-005", "Гипохлорит натрия 3%, 500 мл", "Омега-Дент", "Эндодонтия", BaseUnit.Ml, "флакон 500 мл", 500, 3_000),
        new("E-006", "Бумажные штифты 04", "Meta Biomed", "Эндодонтия", BaseUnit.Pcs, "упаковка (200 шт.)", 200, 2_000),
        new("C-001", "Перчатки нитриловые, размер M", "Benovy", "Расходные материалы", BaseUnit.Pcs, "коробка (100 шт.)", 100, 4_500),
        new("C-002", "Маски медицинские трёхслойные", "Medicom", "Расходные материалы", BaseUnit.Pcs, "коробка (50 шт.)", 50, 1_500),
        new("C-003", "Слюноотсосы одноразовые", "Medicom", "Расходные материалы", BaseUnit.Pcs, "упаковка (100 шт.)", 100, 2_000),
        new("C-004", "Нагрудные салфетки", "Medicom", "Расходные материалы", BaseUnit.Pcs, "упаковка (500 шт.)", 500, 9_000),
        new("C-005", "Ватные валики №2", "Celluron", "Расходные материалы", BaseUnit.Pcs, "упаковка (1000 шт.)", 1000, 3_000),
        new("C-006", "Коффердам латексный, 15×15 см", "Sanctuary", "Расходные материалы", BaseUnit.Pcs, "упаковка (36 шт.)", 36, 7_200),
        new("C-007", "Стаканы одноразовые 180 мл", "Medicom", "Расходные материалы", BaseUnit.Pcs, "упаковка (100 шт.)", 100, 1_200),
        new("B-001", "Бор алмазный шаровидный FG", "Komet", "Боры и инструменты", BaseUnit.Pcs, "упаковка (5 шт.)", 5, 3_500),
        new("B-002", "Бор твердосплавный FG", "Komet", "Боры и инструменты", BaseUnit.Pcs, "упаковка (5 шт.)", 5, 5_000),
        new("S-001", "Шовный материал Викрил 4-0", "Ethicon", "Хирургия и имплантация", BaseUnit.Pcs, "упаковка (12 шт.)", 12, 24_000, true, true, ShelfMonths: 36),
        new("S-002", "Лезвия скальпеля №15", "Swann-Morton", "Хирургия и имплантация", BaseUnit.Pcs, "упаковка (100 шт.)", 100, 6_000),
        new("S-003", "Имплант Osstem TS III SA 4,0×10 мм", "Osstem", "Хирургия и имплантация", BaseUnit.Pcs, null, 1, 85_000, true, true, true, ShelfMonths: 60),
        new("S-004", "Имплант Straumann BLT SLActive 4,1×10 мм", "Straumann", "Хирургия и имплантация", BaseUnit.Pcs, null, 1, 185_000, true, true, true, ShelfMonths: 60),
        new("S-005", "Формирователь десны Osstem", "Osstem", "Хирургия и имплантация", BaseUnit.Pcs, null, 1, 9_000, true),
        new("S-006", "Костный материал Bio-Oss 0,5 г", "Geistlich", "Хирургия и имплантация", BaseUnit.Pcs, null, 1, 65_000, true, true, ShelfMonths: 36),
        new("S-007", "Гемостатическая губка", "Лужские материалы", "Хирургия и имплантация", BaseUnit.Pcs, "упаковка (10 шт.)", 10, 5_000),
        new("S-008", "Абатмент стандартный Osstem", "Osstem", "Хирургия и имплантация", BaseUnit.Pcs, null, 1, 22_000, true),
        new("O-001", "Слепочная масса Speedex Putty, 910 мл", "Coltene", "Ортопедия", BaseUnit.Ml, "банка 910 мл", 910, 16_380, true, true, ShelfMonths: 24),
        new("O-002", "А-силикон Express корригирующий, 2×50 мл", "3M", "Ортопедия", BaseUnit.Ml, "набор 100 мл", 100, 14_000, true, true, ShelfMonths: 24),
        new("O-003", "Цемент RelyX U200, 11 г", "3M", "Ортопедия", BaseUnit.G, "шприц 11 г", 11, 33_000, true, true, ShelfMonths: 24),
        new("O-004", "Protemp 4 (временные коронки), 50 г", "3M", "Ортопедия", BaseUnit.G, "картридж 50 г", 50, 28_000, true, true, ShelfMonths: 24),
        new("H-001", "Порошок Air-Flow Classic, 300 г", "EMS", "Гигиена и профилактика", BaseUnit.G, "банка 300 г", 300, 12_000),
        new("H-002", "Полировочная паста Detartrine, 45 г", "Septodont", "Гигиена и профилактика", BaseUnit.G, "туба 45 г", 45, 4_500),
        new("H-003", "Фторлак, 25 мл", "Омега-Дент", "Гигиена и профилактика", BaseUnit.Ml, "флакон 25 мл", 25, 4_000, true, true, ShelfMonths: 24),
        new("H-004", "Opalescence Boost 40% (отбеливание)", "Ultradent", "Гигиена и профилактика", BaseUnit.Pcs, null, 1, 45_000, true, true, ShelfMonths: 12),
        new("H-005", "Герметик Fissurit FX, 2 мл", "Voco", "Гигиена и профилактика", BaseUnit.Ml, "шприц 2 мл", 2, 5_000, true, true, ShelfMonths: 24),
        new("D-001", "Бланидас (дезсредство), 1 л", "АКВА-РАЗ", "Дезинфекция и стерилизация", BaseUnit.Ml, "флакон 1 л", 1000, 6_000),
        new("D-002", "Пакеты для стерилизации 90×250", "Steriking", "Дезинфекция и стерилизация", BaseUnit.Pcs, "упаковка (200 шт.)", 200, 6_000),
        new("R-001", "Брекеты металлические MBT (1 челюсть)", "3M Unitek", "Ортодонтия", BaseUnit.Pack, null, 1, 28_000, true),
        new("R-002", "Брекеты керамические Clarity (1 челюсть)", "3M Unitek", "Ортодонтия", BaseUnit.Pack, null, 1, 65_000, true),
        new("R-003", "Дуга NiTi 0,014", "Ormco", "Ортодонтия", BaseUnit.Pcs, "упаковка (10 шт.)", 10, 12_000),
        new("R-004", "Лигатуры эластичные", "Ormco", "Ортодонтия", BaseUnit.Pcs, "упаковка (1000 шт.)", 1000, 3_000),
        new("P-001", "Нитрат серебра 30%, 10 мл", "ВладМиВа", "Детская стоматология", BaseUnit.Ml, "флакон 10 мл", 10, 3_000),
    ];

    private sealed record SupplierDef(string Name, string Bin, string Contact, string Phone, string Email, int Terms, string Notes, string[] Prefixes, decimal PriceFactor);

    private static readonly SupplierDef[] SupplierDefs =
    [
        new("ТОО «Дентал Маркет Казахстан»", "110540012345", "Серикбаев Ерлан", "+77012223344", "order@dentalmarket.kz", 14,
            "Доставка по Алматы на следующий день, в Астану — 3 дня", ["A-", "F-", "E-", "B-", "H-", "P-", "O-"], 1.00m),
        new("ТОО «СтомТехнологии»", "080940004567", "Литвинова Оксана", "+77073334455", "sales@stomtech.kz", 30,
            "Отсрочка 30 дней, минимальный заказ 100 000 ₸", ["A-", "F-", "E-", "O-", "R-", "H-"], 1.04m),
        new("ТОО «ДентаЛюкс Астана»", "150140021890", "Жумабеков Асхат", "+77754445566", "info@dentalux.kz", 7,
            "Склад в Астане, самовывоз", ["A-", "F-", "C-", "D-", "H-", "P-"], 0.97m),
        new("ТОО «Имплант Систем KZ»", "120640033112", "Ким Виктор", "+77015556677", "implant@isystem.kz", 30,
            "Официальный дистрибьютор Osstem и Straumann", ["S-"], 1.00m),
        new("ТОО «МедСнаб Центр»", "091240015678", "Абдуллаева Сауле", "+77026667788", "zakaz@medsnab.kz", 10,
            "Расходники и дезсредства, оптовые цены", ["C-", "D-", "S-002", "S-007"], 0.95m),
    ];

    /// <summary>Позиции с особым сроком годности в демо: истекает скоро / уже истёк.</summary>
    private static readonly Dictionary<string, int> ExpiryOverrideDays = new() { ["A-004"] = 20, ["H-004"] = 12, ["F-009"] = -10, ["A-006"] = 45 };

    /// <summary>Позиции, которых в филиалах заведомо мало (ниже минимума).</summary>
    private static readonly HashSet<string> LowInBranches = ["C-001", "A-001", "F-001", "E-003", "S-003"];

    private readonly IRepository<Branch, Guid> _branches;
    private readonly IRepository<Warehouse, Guid> _warehouses;
    private readonly IRepository<ItemCategory, Guid> _categories;
    private readonly IRepository<Item, Guid> _items;
    private readonly IRepository<Supplier, Guid> _suppliers;
    private readonly IRepository<SupplierItem, Guid> _supplierItems;
    private readonly IRepository<ItemStockLevel, Guid> _levels;
    private readonly IRepository<WriteoffReason, Guid> _reasons;
    private readonly StockManager _stock;
    private readonly ICurrentTenant _currentTenant;
    private readonly IGuidGenerator _guid;
    private readonly IUnitOfWorkManager _uowManager;
    private readonly IConfiguration _configuration;
    private readonly IClock _clock;

    public ILogger<InventoryDemoDataSeedContributor> Logger { get; set; } = NullLogger<InventoryDemoDataSeedContributor>.Instance;

    public InventoryDemoDataSeedContributor(
        IRepository<Branch, Guid> branches,
        IRepository<Warehouse, Guid> warehouses,
        IRepository<ItemCategory, Guid> categories,
        IRepository<Item, Guid> items,
        IRepository<Supplier, Guid> suppliers,
        IRepository<SupplierItem, Guid> supplierItems,
        IRepository<ItemStockLevel, Guid> levels,
        IRepository<WriteoffReason, Guid> reasons,
        StockManager stock,
        ICurrentTenant currentTenant,
        IGuidGenerator guid,
        IUnitOfWorkManager uowManager,
        IConfiguration configuration,
        IClock clock)
    {
        _branches = branches;
        _warehouses = warehouses;
        _categories = categories;
        _items = items;
        _suppliers = suppliers;
        _supplierItems = supplierItems;
        _levels = levels;
        _reasons = reasons;
        _stock = stock;
        _currentTenant = currentTenant;
        _guid = guid;
        _uowManager = uowManager;
        _configuration = configuration;
        _clock = clock;
    }

    public int Order => 100;

    public async Task SeedDemoAsync(Guid tenantId)
    {
        if (!_configuration.GetValue("Dental:SeedDemo", true))
        {
            return;
        }

        using (_currentTenant.Change(tenantId))
        {
            using (var uow = _uowManager.Begin(requiresNew: true, isTransactional: true))
            {
                if (await _items.GetCountAsync() > 0 || await _branches.GetCountAsync() == 0)
                {
                    Logger.LogInformation("Inventory demo seed skipped (already seeded or no branches)");
                    return;
                }
                await SeedAsync(tenantId);
                await uow.CompleteAsync();
            }
        }
    }

    private async Task SeedAsync(Guid tenantId)
    {
        // Причины списания
        foreach (var (name, type) in new[]
                 {
                     ("Истёк срок годности", WriteoffReasonType.Expired), ("Повреждение при хранении", WriteoffReasonType.Damaged),
                     ("Брак производителя", WriteoffReasonType.Defect), ("Утеря / недостача", WriteoffReasonType.Lost), ("Прочее", WriteoffReasonType.Other),
                 })
        {
            await _reasons.InsertAsync(new WriteoffReason(_guid.Create(), tenantId, name, type));
        }

        // Склады
        var central = await _warehouses.InsertAsync(new Warehouse(_guid.Create(), tenantId, WarehouseType.Central, null, "Центральный склад"), autoSave: true);
        var branchWarehouses = new List<Warehouse>();
        foreach (var b in (await _branches.GetListAsync()).OrderBy(b => b.CreationTime).ThenBy(b => b.Name))
        {
            var shortName = b.Name.Contains('—') ? b.Name[(b.Name.LastIndexOf('—') + 1)..].Trim() : b.Name;
            branchWarehouses.Add(await _warehouses.InsertAsync(new Warehouse(_guid.Create(), tenantId, WarehouseType.Branch, b.Id, $"Склад филиала {shortName}")
            {
                ParentWarehouseId = central.Id,
            }, autoSave: true));
        }

        // Категории и номенклатура
        var categories = new Dictionary<string, ItemCategory>();
        var root = await _categories.InsertAsync(new ItemCategory(_guid.Create(), tenantId, "Материалы"));
        foreach (var name in ItemDefs.Select(d => d.Category).Distinct())
        {
            categories[name] = await _categories.InsertAsync(new ItemCategory(_guid.Create(), tenantId, name, root.Id));
        }
        var items = new Dictionary<string, Item>();
        var index = 0;
        foreach (var d in ItemDefs)
        {
            var item = new Item(_guid.Create(), tenantId, d.Sku, d.Name, d.Unit)
            {
                CategoryId = categories[d.Category].Id, Manufacturer = d.Manufacturer,
                Barcode = "4607" + (100000000 + index++ * 7919).ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            item.SetTracking(d.Batches, d.Serials, d.Expiry);
            if (d.PackName is not null && d.PackFactor != 1)
            {
                item.SetUnits([(null, d.PackName, d.PackFactor)], _guid.Create);
            }
            items[d.Sku] = await _items.InsertAsync(item);
        }
        await SaveAsync();

        // Поставщики и их цены (тиыны за базовую единицу)
        var suppliers = new List<(Supplier Supplier, SupplierDef Def)>();
        var rnd = new Random(42);
        foreach (var s in SupplierDefs)
        {
            var supplier = new Supplier(_guid.Create(), tenantId, s.Name)
            {
                ContactPerson = s.Contact, Phone = s.Phone, Email = s.Email, Whatsapp = s.Phone, PaymentTermsDays = s.Terms, Notes = s.Notes,
            };
            supplier.SetBin(s.Bin);
            await _suppliers.InsertAsync(supplier);
            suppliers.Add((supplier, s));
            var n = 0;
            foreach (var d in ItemDefs.Where(d => s.Prefixes.Any(p => d.Sku.StartsWith(p, StringComparison.Ordinal))))
            {
                var jitter = 1m + rnd.Next(-3, 4) / 100m;
                var si = new SupplierItem(_guid.Create(), tenantId, supplier.Id, items[d.Sku].Id) { SupplierSku = $"{s.Bin[..3]}-{++n:D4}" };
                si.SetPrice(Math.Max(1, MoneyMath.Round(BasePrice(d) * s.PriceFactor * jitter)), _clock.Now.AddDays(-120));
                await _supplierItems.InsertAsync(si);
            }
        }

        // Нормы остатков: филиал — min 1 упаковка (импланты — 2 шт.), opt 3 упаковки; центральный — 2 и 6 упаковок.
        foreach (var d in ItemDefs)
        {
            var pack = d.PackFactor;
            var (bMin, bOpt) = d.Serials ? (2m, 4m) : (pack, pack * 3);
            foreach (var w in branchWarehouses)
            {
                await _levels.InsertAsync(new ItemStockLevel(_guid.Create(), tenantId, items[d.Sku].Id, w.Id, bMin, bOpt));
            }
            await _levels.InsertAsync(new ItemStockLevel(_guid.Create(), tenantId, items[d.Sku].Id, central.Id, pack * 2, pack * 6));
        }
        await SaveAsync();

        Supplier SupplierFor(ItemDef d) =>
            suppliers.First(s => s.Def.Prefixes.Any(p => d.Sku.StartsWith(p, StringComparison.Ordinal))).Supplier;

        // Приход на центральный склад — по поставщикам (5 упаковок каждой позиции).
        var batchNo = 0;
        foreach (var group in ItemDefs.GroupBy(SupplierFor))
        {
            var lines = new List<StockLineData>();
            foreach (var d in group)
            {
                lines.AddRange(ReceiptLines(d, items[d.Sku], d.Serials ? 3 : 5, ref batchNo));
            }
            await PostReceiptAsync(group.Key.Id, central.Id, lines, $"СФ-{1000 + batchNo}", -20);
        }

        // Приходы в филиалы: по 2 упаковки; «дефицитные» позиции — меньше минимума.
        var whIndex = 0;
        foreach (var w in branchWarehouses)
        {
            foreach (var group in ItemDefs.GroupBy(SupplierFor))
            {
                var lines = new List<StockLineData>();
                foreach (var d in group)
                {
                    var low = LowInBranches.Contains(d.Sku) && whIndex != 0;
                    if (low && d.PackFactor > 1)
                    {
                        // Меньше одной упаковки в базовых единицах.
                        lines.AddRange(ReceiptLinesBase(d, items[d.Sku], Math.Max(1, Math.Floor(d.PackFactor / 3)), ref batchNo));
                    }
                    else
                    {
                        lines.AddRange(ReceiptLines(d, items[d.Sku], low ? 1 : 2, ref batchNo));
                    }
                }
                await PostReceiptAsync(group.Key.Id, w.Id, lines, $"СФ-{2000 + batchNo}", -10);
            }
            whIndex++;
        }

        // Перемещение с центрального склада в первый филиал — в пути.
        if (branchWarehouses.Count > 0)
        {
            var transfer = await _stock.CreateAsync(StockDocumentType.Transfer,
                new StockDocumentHeader(central.Id, branchWarehouses[0].Id, Comment: "Пополнение расходников"),
                [
                    new StockLineData(items["C-001"].Id, 2, items["C-001"].Units.First().Id),
                    new StockLineData(items["C-002"].Id, 2, items["C-002"].Units.First().Id),
                    new StockLineData(items["A-001"].Id, 50),
                ]);
            await _stock.PostAsync(transfer.Id);

            // Черновик списания просроченной партии.
            var expiredReason = (await _reasons.GetListAsync(r => r.Type == WriteoffReasonType.Expired)).First();
            await _stock.CreateAsync(StockDocumentType.Writeoff,
                new StockDocumentHeader(central.Id, null, ReasonId: expiredReason.Id, Comment: "Просрочка Кальцимол LC"),
                [new StockLineData(items["F-009"].Id, 5)]);
        }

        Logger.LogInformation("Inventory demo seed: {Items} items, {Suppliers} suppliers, {Warehouses} warehouses", items.Count, suppliers.Count, branchWarehouses.Count + 1);
    }

    private async Task PostReceiptAsync(Guid supplierId, Guid warehouseId, List<StockLineData> lines, string invoice, int daysAgo)
    {
        var doc = await _stock.CreateAsync(StockDocumentType.Receipt,
            new StockDocumentHeader(null, warehouseId, supplierId, InvoiceNumber: invoice, InvoiceDate: DateOnly.FromDateTime(_clock.Now.AddDays(daysAgo))), lines);
        await _stock.PostAsync(doc.Id);
    }

    /// <summary>Строки прихода в упаковках (серийные — по одной строке на единицу).</summary>
    private IEnumerable<StockLineData> ReceiptLines(ItemDef d, Item item, decimal packs, ref int batchNo)
    {
        var unitId = item.Units.FirstOrDefault()?.Id;
        var packPrice = d.PackPriceTenge * 100;
        var lines = new List<StockLineData>();
        if (d.Serials)
        {
            for (var i = 0; i < packs; i++)
            {
                batchNo++;
                lines.Add(new StockLineData(item.Id, 1, null, null, packPrice, $"L{batchNo:D4}", $"SN{DateTime.UtcNow:yy}{batchNo:D6}", Expiry(d)));
            }
            return lines;
        }
        batchNo++;
        lines.Add(new StockLineData(item.Id, packs, unitId, null, unitId is null ? packPrice : packPrice, d.Batches ? $"L{batchNo:D4}" : null, null, d.Expiry ? Expiry(d) : null));
        return lines;
    }

    /// <summary>Строка прихода в базовых единицах.</summary>
    private IEnumerable<StockLineData> ReceiptLinesBase(ItemDef d, Item item, decimal qty, ref int batchNo)
    {
        batchNo++;
        return [new StockLineData(item.Id, qty, null, null, MoneyMath.Round(BasePrice(d)), d.Batches ? $"L{batchNo:D4}" : null, null, d.Expiry ? Expiry(d) : null)];
    }

    private DateOnly Expiry(ItemDef d)
    {
        var today = DateOnly.FromDateTime(_clock.Now);
        return ExpiryOverrideDays.TryGetValue(d.Sku, out var days) ? today.AddDays(days) : today.AddMonths(Math.Max(6, d.ShelfMonths));
    }

    /// <summary>Себестоимость базовой единицы, тиыны (дробная).</summary>
    private static decimal BasePrice(ItemDef d) => d.PackPriceTenge * 100m / d.PackFactor;

    private async Task SaveAsync()
    {
        if (_uowManager.Current is { } uow) await uow.SaveChangesAsync();
    }
}
