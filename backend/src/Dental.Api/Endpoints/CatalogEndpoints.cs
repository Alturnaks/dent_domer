using Dental.Api.Auth;
using Dental.Api.Infrastructure;
using Dental.Application.Catalog;
using Dental.Application.Common;
using Dental.Application.Inventory;
using Dental.Application.Permissions;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Api.Endpoints;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1").WithTags("Catalog").RequireAuthorization();

        g.MapGet("/service-categories", async (CatalogService s, CancellationToken ct) => TypedResults.Ok(await s.ListCategoriesAsync(ct))).WithName("ListServiceCategories");
        g.MapPost("/service-categories", async (ServiceCategoryRequest r, CatalogService s, CancellationToken ct) => TypedResults.Ok(await s.CreateCategoryAsync(r, ct)))
            .RequirePermission(Perm.Catalog.PricesManage).Validate<ServiceCategoryRequest>().WithName("CreateServiceCategory");
        g.MapPatch("/service-categories/{id:guid}", async (Guid id, ServiceCategoryRequest r, CatalogService s, CancellationToken ct) => TypedResults.Ok(await s.UpdateCategoryAsync(id, r, ct)))
            .RequirePermission(Perm.Catalog.PricesManage).Validate<ServiceCategoryRequest>().WithName("UpdateServiceCategory");

        g.MapGet("/services", async ([FromQuery(Name = "category_id")] Guid? categoryId, string? q, [FromQuery(Name = "include_inactive")] bool? includeInactive,
                [FromQuery(Name = "branch_id")] Guid? branchId, CatalogService s, CancellationToken ct) =>
                TypedResults.Ok(await s.ListServicesAsync(categoryId, q, includeInactive ?? false, branchId, ct)))
            .WithName("ListServices");
        g.MapPost("/services", async (ServiceRequest r, CatalogService s, CancellationToken ct) => TypedResults.Ok(await s.CreateServiceAsync(r, ct)))
            .RequirePermission(Perm.Catalog.PricesManage).Validate<ServiceRequest>().WithName("CreateService");
        g.MapPatch("/services/{id:guid}", async (Guid id, ServiceRequest r, CatalogService s, CancellationToken ct) => TypedResults.Ok(await s.UpdateServiceAsync(id, r, ct)))
            .RequirePermission(Perm.Catalog.PricesManage).Validate<ServiceRequest>().WithName("UpdateService");

        g.MapGet("/price-lists", async (CatalogService s, CancellationToken ct) => TypedResults.Ok(await s.ListPriceListsAsync(ct)))
            .RequirePermission(Perm.Catalog.PricesManage, Perm.Reports.Finance).WithName("ListPriceLists");
        g.MapPost("/price-lists", async (PriceListRequest r, CatalogService s, CancellationToken ct) => TypedResults.Ok(await s.CreatePriceListAsync(r, ct)))
            .RequirePermission(Perm.Catalog.PricesManage).Validate<PriceListRequest>().WithName("CreatePriceList");
        g.MapPatch("/price-lists/{id:guid}", async (Guid id, PriceListRequest r, CatalogService s, CancellationToken ct) => TypedResults.Ok(await s.UpdatePriceListAsync(id, r, ct)))
            .RequirePermission(Perm.Catalog.PricesManage).Validate<PriceListRequest>().WithName("UpdatePriceList");
        g.MapGet("/price-lists/{id:guid}/items", async (Guid id, CatalogService s, CancellationToken ct) => TypedResults.Ok(await s.ListPriceItemsAsync(id, ct)))
            .RequirePermission(Perm.Catalog.PricesManage, Perm.Reports.Finance).WithName("ListPriceListItems");
        g.MapPut("/price-lists/{id:guid}/items", async (Guid id, List<PriceListItemInput> items, CatalogService s, CancellationToken ct) => TypedResults.Ok(await s.SetPriceItemsAsync(id, items, ct)))
            .RequirePermission(Perm.Catalog.PricesManage).WithName("SetPriceListItems");
        g.MapPost("/price-lists/{id:guid}/bulk-update", async (Guid id, BulkPriceUpdateRequest r, CatalogService s, CancellationToken ct) => TypedResults.Ok(await s.BulkUpdateAsync(id, r, ct)))
            .RequirePermission(Perm.Catalog.PricesManage).Validate<BulkPriceUpdateRequest>().WithName("BulkUpdatePrices");

        g.MapGet("/services/{id:guid}/tech-cards", async (Guid id, CatalogService s, CancellationToken ct) => TypedResults.Ok(await s.ListTechCardsAsync(id, ct)))
            .WithName("ListTechCards");
        g.MapPost("/services/{id:guid}/tech-cards", async (Guid id, TechCardRequest r, CatalogService s, CancellationToken ct) => TypedResults.Ok(await s.CreateTechCardVersionAsync(id, r, ct)))
            .RequirePermission(Perm.Catalog.TechCardsManage).Validate<TechCardRequest>().WithName("CreateTechCardVersion");

        return app;
    }

    public static IEndpointRouteBuilder MapInventoryCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1").WithTags("InventoryCatalog").RequireAuthorization();
        string[] view = [Perm.Inventory.View, Perm.Inventory.ItemsManage, Perm.Catalog.TechCardsManage, Perm.Visits.Complete];

        g.MapGet("/warehouses", async ([FromQuery(Name = "branch_id")] Guid? branchId, InventoryCatalogService s, CancellationToken ct) => TypedResults.Ok(await s.ListWarehousesAsync(branchId, ct)))
            .RequirePermission(view).WithName("ListWarehouses");
        g.MapPost("/warehouses", async (WarehouseRequest r, InventoryCatalogService s, CancellationToken ct) => TypedResults.Ok(await s.CreateWarehouseAsync(r, ct)))
            .RequirePermission(Perm.Inventory.ItemsManage, Perm.Org.SettingsManage).Validate<WarehouseRequest>().WithName("CreateWarehouse");
        g.MapPatch("/warehouses/{id:guid}", async (Guid id, WarehouseRequest r, InventoryCatalogService s, CancellationToken ct) => TypedResults.Ok(await s.UpdateWarehouseAsync(id, r, ct)))
            .RequirePermission(Perm.Inventory.ItemsManage, Perm.Org.SettingsManage).Validate<WarehouseRequest>().WithName("UpdateWarehouse");

        g.MapGet("/item-categories", async (InventoryCatalogService s, CancellationToken ct) => TypedResults.Ok(await s.ListItemCategoriesAsync(ct)))
            .RequirePermission(view).WithName("ListItemCategories");
        g.MapPost("/item-categories", async (ItemCategoryRequest r, InventoryCatalogService s, CancellationToken ct) => TypedResults.Ok(await s.CreateItemCategoryAsync(r, ct)))
            .RequirePermission(Perm.Inventory.ItemsManage).WithName("CreateItemCategory");
        g.MapPatch("/item-categories/{id:guid}", async (Guid id, ItemCategoryRequest r, InventoryCatalogService s, CancellationToken ct) => TypedResults.Ok(await s.UpdateItemCategoryAsync(id, r, ct)))
            .RequirePermission(Perm.Inventory.ItemsManage).WithName("UpdateItemCategory");

        g.MapGet("/items", async (string? q, [FromQuery(Name = "category_id")] Guid? categoryId, [FromQuery(Name = "include_inactive")] bool? includeInactive,
                int? page, [FromQuery(Name = "page_size")] int? pageSize, InventoryCatalogService s, CancellationToken ct) =>
                TypedResults.Ok(await s.ListItemsAsync(q, categoryId, includeInactive ?? false, new PageQuery(page ?? 1, pageSize ?? 50), ct)))
            .RequirePermission(view).WithName("ListItems");
        g.MapGet("/items/{id:guid}", async (Guid id, InventoryCatalogService s, CancellationToken ct) => TypedResults.Ok(await s.GetItemAsync(id, ct)))
            .RequirePermission(view).WithName("GetItem");
        g.MapPost("/items", async (ItemRequest r, InventoryCatalogService s, CancellationToken ct) => TypedResults.Ok(await s.CreateItemAsync(r, ct)))
            .RequirePermission(Perm.Inventory.ItemsManage).Validate<ItemRequest>().WithName("CreateItem");
        g.MapPatch("/items/{id:guid}", async (Guid id, ItemRequest r, InventoryCatalogService s, CancellationToken ct) => TypedResults.Ok(await s.UpdateItemAsync(id, r, ct)))
            .RequirePermission(Perm.Inventory.ItemsManage).Validate<ItemRequest>().WithName("UpdateItem");
        g.MapGet("/items/{id:guid}/stock-levels", async (Guid id, InventoryCatalogService s, CancellationToken ct) => TypedResults.Ok(await s.GetStockLevelsAsync(id, ct)))
            .RequirePermission(view).WithName("GetStockLevels");
        g.MapPut("/items/{id:guid}/stock-levels", async (Guid id, List<StockLevelInput> levels, InventoryCatalogService s, CancellationToken ct) => TypedResults.Ok(await s.SetStockLevelsAsync(id, levels, ct)))
            .RequirePermission(Perm.Inventory.ItemsManage).WithName("SetStockLevels");
        g.MapPut("/stock-levels/bulk", async (List<BulkStockLevelInput> levels, InventoryCatalogService s, CancellationToken ct) => TypedResults.Ok(await s.BulkSetStockLevelsAsync(levels, ct)))
            .RequirePermission(Perm.Inventory.ItemsManage).WithName("BulkSetStockLevels");

        string[] suppliersView = [Perm.Inventory.View, Perm.Purchase.OrderCreate, Perm.Purchase.RequestCreate, Perm.Inventory.Receive];
        g.MapGet("/suppliers", async (string? q, int? page, [FromQuery(Name = "page_size")] int? pageSize, InventoryCatalogService s, CancellationToken ct) =>
                TypedResults.Ok(await s.ListSuppliersAsync(q, new PageQuery(page ?? 1, pageSize ?? 100), ct)))
            .RequirePermission(suppliersView).WithName("ListSuppliers");
        g.MapGet("/suppliers/{id:guid}", async (Guid id, InventoryCatalogService s, CancellationToken ct) => TypedResults.Ok(await s.GetSupplierAsync(id, ct)))
            .RequirePermission(suppliersView).WithName("GetSupplier");
        g.MapPost("/suppliers", async (SupplierRequest r, InventoryCatalogService s, CancellationToken ct) => TypedResults.Ok(await s.CreateSupplierAsync(r, ct)))
            .RequirePermission(Perm.Purchase.OrderCreate, Perm.Inventory.ItemsManage).Validate<SupplierRequest>().WithName("CreateSupplier");
        g.MapPatch("/suppliers/{id:guid}", async (Guid id, SupplierRequest r, InventoryCatalogService s, CancellationToken ct) => TypedResults.Ok(await s.UpdateSupplierAsync(id, r, ct)))
            .RequirePermission(Perm.Purchase.OrderCreate, Perm.Inventory.ItemsManage).Validate<SupplierRequest>().WithName("UpdateSupplier");
        g.MapGet("/suppliers/{id:guid}/items", async (Guid id, InventoryCatalogService s, CancellationToken ct) => TypedResults.Ok(await s.GetSupplierItemsAsync(id, ct)))
            .RequirePermission(suppliersView).WithName("GetSupplierItems");
        g.MapPut("/suppliers/{id:guid}/items", async (Guid id, List<SupplierItemInput> items, InventoryCatalogService s, CancellationToken ct) => TypedResults.Ok(await s.SetSupplierItemsAsync(id, items, ct)))
            .RequirePermission(Perm.Purchase.OrderCreate, Perm.Inventory.ItemsManage).WithName("SetSupplierItems");

        return app;
    }
}
