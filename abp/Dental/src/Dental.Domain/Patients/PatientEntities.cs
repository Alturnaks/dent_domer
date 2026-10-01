using System;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dental.Patients;

/// <summary>Подписанное согласие пациента (ИДС, обработка ПД и т.п.).</summary>
[Audited]
public class PatientConsent : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid PatientId { get; private set; }
    public string Type { get; private set; } = null!;
    public DateTime SignedAt { get; private set; }
    public string? FileUrl { get; set; }

    protected PatientConsent() { }

    public PatientConsent(Guid id, Guid? tenantId, Guid patientId, string type, DateTime signedAt, string? fileUrl) : base(id)
    {
        TenantId = tenantId;
        PatientId = patientId;
        Type = Check.NotNullOrWhiteSpace(type, nameof(type), PatientConsts.MaxConsentTypeLength).Trim();
        SignedAt = signedAt;
        FileUrl = fileUrl;
    }

    internal void MoveTo(Guid patientId) => PatientId = patientId;
}

/// <summary>
/// Кэш баланса пациента: + аванс / − долг (тиыны). Одна строка на пациента, меняется кассой/визитами
/// в той же транзакции, что и платёж (Add). Concurrency stamp защищает от гонок.
/// </summary>
public class PatientBalance : AggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid PatientId { get; private set; }
    public long Balance { get; private set; }

    protected PatientBalance() { }

    public PatientBalance(Guid id, Guid? tenantId, Guid patientId) : base(id)
    {
        TenantId = tenantId;
        PatientId = patientId;
    }

    /// <summary>Изменить баланс на delta тиынов (+ оплата/аванс, − начисление за визит).</summary>
    public void Add(long delta) => Balance += delta;

    internal long TakeAll()
    {
        var b = Balance;
        Balance = 0;
        return b;
    }
}

/// <summary>Источник привлечения пациента (справочник).</summary>
[Audited]
public class LeadSource : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public string Name { get; private set; } = null!;

    protected LeadSource() { }

    public LeadSource(Guid id, Guid? tenantId, string name) : base(id)
    {
        TenantId = tenantId;
        SetName(name);
    }

    public void SetName(string name) => Name = Check.NotNullOrWhiteSpace(name, nameof(name), PatientConsts.MaxLeadSourceNameLength).Trim();
}
