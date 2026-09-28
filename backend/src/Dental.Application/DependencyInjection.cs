using Dental.Application.Auth;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Dental.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Use-case сервисы: все публичные sealed-классы *Service из сборки Application.
        var assembly = typeof(DependencyInjection).Assembly;
        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && t.Name.EndsWith("Service", StringComparison.Ordinal)
                                                             && t.Namespace?.StartsWith("Dental.Application", StringComparison.Ordinal) == true))
        {
            services.AddScoped(type);
        }
        // Обработчики подтверждений и хуки модулей.
        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(Orgs.IApprovalHandler).IsAssignableFrom(t)))
            services.AddScoped(typeof(Orgs.IApprovalHandler), type);
        services.AddScoped<Inventory.IVisitStockConsumer>(sp => sp.GetRequiredService<Inventory.StockService>());
        services.AddScoped<Schedule.IAppointmentArrivalHandler>(sp => sp.GetRequiredService<Visits.VisitService>());
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        _ = typeof(AuthService);
        return services;
    }
}
