namespace Dental.Api.Endpoints;

public static class EndpointRegistry
{
    /// <summary>Все модули API (/api/v1). Каждый модуль — свой MapGroup.</summary>
    public static IEndpointRouteBuilder MapDentalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapAuthEndpoints();
        app.MapOrgEndpoints();
        app.MapCatalogEndpoints();
        app.MapInventoryCatalogEndpoints();
        app.MapPatientEndpoints();
        app.MapScheduleEndpoints();
        app.MapNotificationEndpoints();
        app.MapVisitEndpoints();
        app.MapCashEndpoints();
        app.MapInventoryEndpoints();
        app.MapPurchasingEndpoints();
        app.MapAuditEndpoints();
        app.MapPayrollEndpoints();
        return app;
    }
}
