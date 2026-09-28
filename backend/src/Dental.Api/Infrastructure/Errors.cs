using System.Text.Json;
using Dental.Application.Common;
using Dental.Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Dental.Api.Infrastructure;

/// <summary>Единый формат ошибки: { "error": { "code", "message", "details" } }.</summary>
public sealed record ErrorBody(string Code, string Message, IReadOnlyDictionary<string, object?>? Details);
public sealed record ErrorResponse(ErrorBody Error);

public static class ErrorResponses
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, int status, string code, string message, IReadOnlyDictionary<string, object?>? details = null)
    {
        if (context.Response.HasStarted) return Task.CompletedTask;
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new ErrorResponse(new ErrorBody(code, message, details)), Json));
    }

    public static IResult Result(int status, string code, string message, IReadOnlyDictionary<string, object?>? details = null) =>
        Results.Json(new ErrorResponse(new ErrorBody(code, message, details)), Json, statusCode: status);
}

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case DomainException de:
                await ErrorResponses.WriteAsync(httpContext, de.Status, de.Code, de.Message, de.Details);
                return true;

            case ValidationException ve:
                var fields = ve.Errors
                    .GroupBy(e => JsonNamingPolicy.CamelCase.ConvertName(e.PropertyName))
                    .ToDictionary(g => g.Key, g => (object?)g.Select(e => new { code = e.ErrorCode, message = e.ErrorMessage }).ToList());
                await ErrorResponses.WriteAsync(httpContext, 400, ErrorCodes.ValidationFailed, "Проверьте заполнение полей", fields);
                return true;

            case DbUpdateConcurrencyException:
                await ErrorResponses.WriteAsync(httpContext, 409, ErrorCodes.ConcurrencyConflict,
                    "Запись изменена другим пользователем. Обновите страницу и повторите.");
                return true;

            case DbUpdateException { InnerException: PostgresException pg } when pg.SqlState == PostgresErrorCodes.ExclusionViolation:
            case PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation }:
                await ErrorResponses.WriteAsync(httpContext, 409, ErrorCodes.SlotConflict, "Время уже занято другой записью");
                return true;

            case DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pgu }:
                await ErrorResponses.WriteAsync(httpContext, 409, "UNIQUE_VIOLATION", "Такая запись уже существует",
                    new Dictionary<string, object?> { ["constraint"] = pgu.ConstraintName });
                return true;

            case BadHttpRequestException bad:
                await ErrorResponses.WriteAsync(httpContext, bad.StatusCode, ErrorCodes.ValidationFailed, "Некорректный запрос",
                    env.IsDevelopment() ? new Dictionary<string, object?> { ["reason"] = bad.Message } : null);
                return true;

            case OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested:
                return true;

            default:
                logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
                await ErrorResponses.WriteAsync(httpContext, 500, ErrorCodes.InternalError, "Внутренняя ошибка сервера",
                    env.IsDevelopment() ? new Dictionary<string, object?> { ["exception"] = exception.GetType().Name, ["message"] = exception.Message } : null);
                return true;
        }
    }
}

/// <summary>FluentValidation через endpoint filter: валидирует аргументы, для которых зарегистрирован IValidator.</summary>
public sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var validator = context.HttpContext.RequestServices.GetService<IValidator<T>>();
        if (validator is not null)
        {
            var arg = context.Arguments.OfType<T>().FirstOrDefault();
            if (arg is null)
                return ErrorResponses.Result(400, ErrorCodes.ValidationFailed, "Пустое тело запроса");
            var result = await validator.ValidateAsync(arg, context.HttpContext.RequestAborted);
            if (!result.IsValid) throw new ValidationException(result.Errors);
        }
        return await next(context);
    }
}

public static class ValidationExtensions
{
    public static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>()
            .ProducesProblem(400);
}
