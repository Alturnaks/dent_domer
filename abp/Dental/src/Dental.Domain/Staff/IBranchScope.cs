using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Dental.Staff;

/// <summary>
/// Ограничение данных филиалами текущего пользователя (бывший Membership.HasBranch).
/// Использовать во всех app-сервисах модулей, где данные привязаны к BranchId.
/// Результат кэшируется в рамках scope (HTTP-запроса).
/// </summary>
public interface IBranchScope
{
    /// <summary>Профиль сотрудника текущего пользователя в текущем арендаторе (null — нет профиля).</summary>
    Task<Employee?> GetCurrentEmployeeAsync();

    Task<BranchScopeInfo> GetAsync();

    Task<bool> CanAccessAsync(Guid branchId);

    /// <summary>Бросает BusinessException Dental:BranchAccessDenied (403), если филиал недоступен.</summary>
    Task EnsureCanAccessAsync(Guid branchId);

    /// <summary>Фильтрует запрос по доступным филиалам: query.Where(x => allowed.Contains(branchSelector(x))).</summary>
    Task<IQueryable<T>> ApplyAsync<T>(IQueryable<T> query, Expression<Func<T, Guid>> branchSelector);
}
