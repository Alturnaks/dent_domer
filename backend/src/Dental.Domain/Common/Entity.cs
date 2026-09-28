namespace Dental.Domain.Common;

/// <summary>Базовая сущность: UUID v7, генерируется в приложении.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}

/// <summary>Сущность, принадлежащая организации (арендатору).</summary>
public interface ITenantEntity
{
    Guid OrganizationId { get; set; }
}

/// <summary>Сущность с полями created/updated.</summary>
public interface IHasTimestamps
{
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
    Guid? CreatedBy { get; set; }
    Guid? UpdatedBy { get; set; }
}

/// <summary>Мягкое удаление для справочников.</summary>
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Оптимистическая блокировка: version инкрементируется при каждом сохранении.</summary>
public interface IVersioned
{
    int Version { get; set; }
}

/// <summary>Изменения сущности пишутся в audit_log автоматически.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class AuditedAttribute : Attribute;

/// <summary>Поле с персональными данными: маскируется в логах и аудите.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SensitiveAttribute : Attribute;

/// <summary>Бизнес-сущность организации: id, organization_id, created/updated.</summary>
public abstract class TenantEntity : Entity, ITenantEntity, IHasTimestamps
{
    public Guid OrganizationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
