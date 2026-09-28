using Dental.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Dental.Application.Common;

public sealed record PageQuery(int Page = 1, int PageSize = 50)
{
    public int SafePage => Math.Max(1, Page);
    public int SafeSize => Math.Clamp(PageSize, 1, 500);
}

public static class QueryExtensions
{
    public static async Task<PagedResult<TOut>> ToPagedAsync<TIn, TOut>(this IQueryable<TIn> query, PageQuery page, Func<TIn, TOut> map, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page.SafePage - 1) * page.SafeSize).Take(page.SafeSize).ToListAsync(ct);
        return new PagedResult<TOut>(items.Select(map).ToList(), total, page.SafePage, page.SafeSize);
    }

    public static IQueryable<T> NotDeleted<T>(this IQueryable<T> q) where T : ISoftDeletable => q.Where(x => x.DeletedAt == null);

    public static async Task<T> GetOrThrowAsync<T>(this IQueryable<T> q, Guid id, string what, CancellationToken ct) where T : Entity =>
        await q.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound(what);

    /// <summary>Клиент прислал version: сравниваем с ним, а не с загруженным значением (оптимистическая блокировка).</summary>
    public static void SetExpectedVersion<T>(this IAppDbContext db, T entity, int? version) where T : class, IVersioned
    {
        if (version is null) return;
        if (db is DbContext ctx) ctx.Entry(entity).Property(nameof(IVersioned.Version)).OriginalValue = version.Value;
        if (entity.Version != version.Value) throw new AppException(ErrorCodes.ConcurrencyConflict, "Запись изменена другим пользователем. Обновите страницу и повторите.", 409);
    }

    public static string? NullIfEmpty(this string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
