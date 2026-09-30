using Dental.Localization;
using Dental.Roles;
using Dental.Staff;
using Volo.Abp.Application.Services;

namespace Dental;

/// <summary>
/// Базовый класс всех app-сервисов приложения.
/// BranchScope — доступные текущему пользователю филиалы; RoleLimits — лимиты его ролей.
/// </summary>
public abstract class DentalAppService : ApplicationService
{
    protected DentalAppService()
    {
        LocalizationResource = typeof(DentalResource);
    }

    protected IBranchScope BranchScope => LazyServiceProvider.LazyGetRequiredService<IBranchScope>();

    protected IRoleLimitsProvider RoleLimits => LazyServiceProvider.LazyGetRequiredService<IRoleLimitsProvider>();
}
