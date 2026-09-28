using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Dental.Api.Auth;
using Dental.Api.Endpoints;
using Dental.Application;
using Dental.Application.Common;
using Dental.Infrastructure;
using Dental.Infrastructure.Persistence.Interceptors;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace Dental.Api.Infrastructure;

public static class ServiceRegistration
{
    public static WebApplicationBuilder AddDentalApi(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var config = builder.Configuration;

        builder.Host.UseSerilog((ctx, sp, lc) =>
        {
            lc.ReadFrom.Configuration(ctx.Configuration)
              .Enrich.FromLogContext()
              .Destructure.With<SensitiveDestructuringPolicy>()
              .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture);
            var seq = ctx.Configuration["Seq:ServerUrl"];
            if (!string.IsNullOrWhiteSpace(seq)) lc.WriteTo.Seq(seq, formatProvider: System.Globalization.CultureInfo.InvariantCulture);
        });

        services.AddInfrastructure(config);
        services.AddApplication();
        services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>(includeInternalTypes: true);

        services.AddScoped<CurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
        services.AddScoped<IAuditActor>(sp => sp.GetRequiredService<CurrentUser>());
        services.AddSingleton<Application.Auth.ITokenService, TokenService>();
        services.AddSingleton<Application.Auth.IPasswordHashing, PasswordHashing>();

        services.AddOptions<JwtOptions>().Bind(config.GetSection(JwtOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.RequireHttpsMetadata = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = TokenService.SigningKey(jwt),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name",
                };
            });
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ErrorAuthorizationResultHandler>();

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
            o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });

        services.AddOpenApi(o =>
        {
            o.AddDocumentTransformer((doc, _, _) =>
            {
                doc.Info.Title = "Dental Admin API";
                doc.Info.Version = "v1";
                return Task.CompletedTask;
            });
            o.AddDocumentTransformer<BearerSecurityTransformer>();
        });

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = 429;
            o.OnRejected = async (ctx, ct) =>
                await ErrorResponses.WriteAsync(ctx.HttpContext, 429, ErrorCodes.RateLimited, "Слишком много попыток. Повторите через минуту.");
            o.AddPolicy(AuthEndpoints.AuthRateLimitPolicy, http =>
                RateLimitPartition.GetFixedWindowLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("RateLimit:AuthPerMinute", 60), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

        var frontend = config["App:FrontendUrl"] ?? "http://localhost:3000";
        services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(frontend.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .AllowAnyHeader().AllowAnyMethod().AllowCredentials().WithExposedHeaders("Content-Disposition", "Idempotent-Replayed")));

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("dental-api"))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddSource("Npgsql");
                if (!string.IsNullOrWhiteSpace(config["OTEL_EXPORTER_OTLP_ENDPOINT"])) t.AddOtlpExporter();
            });

        services.AddHealthChecks();
        return builder;
    }

    public static WebApplication UseDentalApi(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.Use(async (ctx, next) =>
        {
            var h = ctx.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            if (ctx.Request.Path.StartsWithSegments("/scalar"))
                h["Content-Security-Policy"] = "default-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.jsdelivr.net data: blob:; connect-src 'self'";
            else
                h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
            await next();
        });
        if (!app.Environment.IsDevelopment()) app.UseHsts();
        app.UseSerilogRequestLogging();
        app.UseCors();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseMiddleware<TenantMiddleware>();
        app.UseAuthorization();
        return app;
    }
}

/// <summary>Схема Bearer в OpenAPI-документе.</summary>
internal sealed class BearerSecurityTransformer : Microsoft.AspNetCore.OpenApi.IOpenApiDocumentTransformer
{
    public Task TransformAsync(Microsoft.OpenApi.OpenApiDocument document, Microsoft.AspNetCore.OpenApi.OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new Microsoft.OpenApi.OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, Microsoft.OpenApi.IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new Microsoft.OpenApi.OpenApiSecurityScheme
        {
            Type = Microsoft.OpenApi.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
        };
        return Task.CompletedTask;
    }
}
