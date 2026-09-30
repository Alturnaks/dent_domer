using System;
using System.Threading.Tasks;

namespace Dental.Roles;

/// <summary>
/// Лимиты текущего пользователя (объединение по его ролям — берётся самый мягкий лимит).
/// Встроенная ABP-роль "admin" — без ограничений. Нет ролей/записей — всё запрещено.
/// </summary>
public interface IRoleLimitsProvider
{
    Task<RoleLimitsData> GetForCurrentUserAsync();

    Task<RoleLimitsData> GetForUserAsync(Guid userId);

    Task<RoleLimitsData> GetForRoleAsync(Guid roleId);

    /// <summary>Бросает Dental:RoleLimitExceeded, если скидка выше лимита (для запроса подтверждения ловить заранее через AllowsDiscount).</summary>
    Task EnsureDiscountAllowedAsync(decimal pct);
    Task EnsureWriteoffAllowedAsync(long amount);
    Task EnsureRefundAllowedAsync(long amount);
}
