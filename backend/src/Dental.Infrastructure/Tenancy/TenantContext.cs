using Dental.Application.Common;

namespace Dental.Infrastructure.Tenancy;

/// <summary>Scoped-контекст арендатора. Заполняется из JWT (middleware) или явно в задачах/seed.</summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? OrganizationId { get; private set; }

    public Guid RequiredOrganizationId =>
        OrganizationId ?? throw new InvalidOperationException("Tenant is not set for this operation");

    public void Set(Guid? organizationId) => OrganizationId = organizationId;
}
