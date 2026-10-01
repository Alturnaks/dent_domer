using System.Threading.Tasks;
using Dental.Roles;

namespace Dental.Approvals;

/// <summary>
/// Исполнитель решения по запросу конкретного типа. Реализации — в модулях (скидки, списания, возвраты,
/// правки закрытых визитов, заказы поставщику, зарплата): класс + ITransientDependency, имя оканчивается на
/// "ApprovalHandler" (конвенция ABP экспонирует IApprovalHandler) или [ExposeServices(typeof(IApprovalHandler))].
/// Вызывается в той же UoW, что и решение.
/// </summary>
public interface IApprovalHandler
{
    string Type { get; }

    Task OnApprovedAsync(ApprovalRequest request);

    Task OnRejectedAsync(ApprovalRequest request);
}

/// <summary>
/// Необязательное расширение: собственная проверка лимита подтверждающего для нестандартного типа
/// (стандартные типы проверяются в ApprovalManager.AllowsByLimits).
/// </summary>
public interface IApprovalLimitCheck
{
    bool AllowsByLimits(ApprovalRequest request, RoleLimitsData limits);
}
