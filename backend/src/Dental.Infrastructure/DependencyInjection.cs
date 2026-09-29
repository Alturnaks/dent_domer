using Dental.Application.Auth;
using Dental.Application.Common;
using Dental.Domain.Audit;
using Dental.Infrastructure.Messaging;
using Dental.Infrastructure.Persistence;
using Dental.Infrastructure.Persistence.Interceptors;
using Dental.Infrastructure.Services;
using Dental.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dental.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<RedisOptions>().Bind(configuration.GetSection(RedisOptions.Section));
        services.AddOptions<SmtpOptions>().Bind(configuration.GetSection(SmtpOptions.Section));
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.Section));
        services.AddOptions<AppOptions>().Bind(configuration.GetSection(AppOptions.Section));

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<TenantConnectionInterceptor>();
        services.AddScoped<AuditingInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var db = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            options.UseNpgsql(db.ConnectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    npgsql.EnableRetryOnFailure(0);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<TenantConnectionInterceptor>(), sp.GetRequiredService<AuditingInterceptor>());
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IOutbox, Outbox>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddSingleton<SystemDb>();
        services.AddSingleton<ISystemDb>(sp => sp.GetRequiredService<SystemDb>());
        services.AddSingleton<IMembershipCache, MembershipCache>();

        var redis = configuration.GetSection(RedisOptions.Section).Get<RedisOptions>();
        if (!string.IsNullOrWhiteSpace(redis?.ConnectionString))
        {
            services.AddStackExchangeRedisCache(o =>
            {
                o.Configuration = redis.ConnectionString;
                o.InstanceName = "dental:";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<IMessageProvider>(sp => new ConsoleMessageProvider(sp.GetRequiredService<ILogger<ConsoleMessageProvider>>(), MessageChannel.Whatsapp));
        services.AddSingleton<IMessageProvider>(sp => new ConsoleMessageProvider(sp.GetRequiredService<ILogger<ConsoleMessageProvider>>(), MessageChannel.Sms));

        // Фоновые задачи и outbox
        services.AddSingleton<Jobs.TenantJobRunner>();
        services.AddScoped<Jobs.IOutboxHandler, Jobs.SuspiciousEventHandler>();
        services.AddScoped<Jobs.AuditPartitionMaintenanceJob>();
        services.AddSingleton<Jobs.IRecurringJobDefinition>(new Jobs.RecurringJob<Jobs.AuditPartitionMaintenanceJob>("audit_partition_maintenance", "0 2 1 * *"));
        services.AddScoped<Application.Schedule.IReminderSender, Jobs.ReminderSender>();
        services.AddScoped<Jobs.IOutboxHandler, Jobs.WaitlistMatchHandler>();
        services.AddScoped<Jobs.SendRemindersJob>();
        services.AddScoped<Jobs.MarkNoShowsJob>();
        services.AddSingleton<Jobs.IRecurringJobDefinition>(new Jobs.RecurringJob<Jobs.SendRemindersJob>("send_reminders", "*/5 * * * *"));
        services.AddSingleton<Jobs.IRecurringJobDefinition>(new Jobs.RecurringJob<Jobs.MarkNoShowsJob>("mark_no_shows", "*/15 * * * *"));
        services.AddScoped<Jobs.GenerateReplenishmentRequestsJob>();
        // Ежедневно в 03:00 по часовому поясу организации: задача запускается ежечасно и обрабатывает организации, где сейчас 03:xx.
        services.AddSingleton<Jobs.IRecurringJobDefinition>(new Jobs.RecurringJob<Jobs.GenerateReplenishmentRequestsJob>("generate_replenishment_requests", "5 * * * *"));

        services.AddSingleton<Application.Common.ITableDocumentRenderer, Export.TableDocumentRenderer>();

        return services;
    }
}
