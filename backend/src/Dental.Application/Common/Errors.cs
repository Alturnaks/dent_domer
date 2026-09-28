using Dental.Domain.Common;

namespace Dental.Application.Common;

/// <summary>Ошибка приложения с HTTP-статусом. Бросается из сервисов, превращается в единый формат ответа.</summary>
public sealed class AppException : DomainException
{
    public AppException(string code, string message, int status = 400, IReadOnlyDictionary<string, object?>? details = null)
        : base(code, message, details, status) { }

    public AppException() : base() { }
    public AppException(string message) : base(message) { }
    public AppException(string message, Exception inner) : base(message, inner) { }

    public static AppException NotFound(string what = "Объект") => new(ErrorCodes.NotFound, $"{what} не найден", 404);
    public static AppException Forbidden(string message = "Недостаточно прав") => new(ErrorCodes.Forbidden, message, 403);
    public static AppException Conflict(string code, string message, IReadOnlyDictionary<string, object?>? details = null) => new(code, message, 409, details);
    public static AppException BadRequest(string code, string message, IReadOnlyDictionary<string, object?>? details = null) => new(code, message, 400, details);
    public static AppException Unprocessable(string code, string message, IReadOnlyDictionary<string, object?>? details = null) => new(code, message, 422, details);
}

/// <summary>Ошибка в Result.</summary>
public sealed record Error(string Code, string Message, int Status = 400, IReadOnlyDictionary<string, object?>? Details = null)
{
    public AppException ToException() => new(Code, Message, Status, Details);
}

/// <summary>Результат use-case: значение или ошибка.</summary>
public readonly record struct Result<T>
{
    private Result(T? value, Error? error) { Value = value; Error = error; }

    public T? Value { get; }
    public Error? Error { get; }
    public bool IsSuccess => Error is null;

    public static Result<T> Ok(T value) => new(value, null);
    public static Result<T> Fail(Error error) => new(default, error);

    public static implicit operator Result<T>(T value) => Ok(value);
    public static implicit operator Result<T>(Error error) => Fail(error);

    /// <summary>Значение или исключение (для кода, которому удобнее исключения).</summary>
    public T Unwrap() => IsSuccess ? Value! : throw Error!.ToException();
}

/// <summary>Страница справочника (offset-пагинация).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>Страница журнала (курсорная пагинация).</summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);

public static class Cursor
{
    /// <summary>Курсор = base64 от "{ticks}|{id}" последнего элемента (сортировка по времени desc, id desc).</summary>
    public static string Encode(DateTimeOffset at, Guid id) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{at.UtcTicks}|{id}"));

    public static (DateTimeOffset At, Guid Id)? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|');
            return (new DateTimeOffset(long.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), TimeSpan.Zero), Guid.Parse(parts[1]));
        }
        catch (FormatException)
        {
            throw AppException.BadRequest(ErrorCodes.ValidationFailed, "Некорректный курсор");
        }
    }
}
