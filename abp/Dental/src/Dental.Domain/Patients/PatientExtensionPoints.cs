using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Dental.Patients;

/// <summary>
/// Перенос связанных данных при слиянии карточек. Каждый модуль, хранящий PatientId
/// (записи, визиты, платежи, лист ожидания, сообщения, движения склада), регистрирует свою реализацию
/// (ITransientDependency + ExposeServices). Возвращает число перенесённых строк для аудита.
/// </summary>
public interface IPatientMergeContributor
{
    /// <summary>Ключ для журнала (appointments, visits, payments...).</summary>
    string Name { get; }

    Task<int> MoveAsync(Guid fromPatientId, Guid toPatientId);
}

/// <summary>Активность пациента для списка и карточки. Реализуют модули расписания/визитов/кассы.</summary>
public interface IPatientActivityProvider
{
    /// <summary>
    /// Id пациентов с закрытым визитом после даты (для фильтра «не был N месяцев»).
    /// null — данных нет (модуль визитов не подключён), фильтр не сужает список.
    /// Возвращайте IQueryable из того же DbContext, чтобы фильтр выполнился одним SQL.
    /// </summary>
    Task<IQueryable<Guid>?> GetVisitedSinceQueryAsync(DateTime sinceUtc);

    Task<Dictionary<Guid, PatientActivity>> GetActivityAsync(IReadOnlyCollection<Guid> patientIds);
}

public class PatientActivity
{
    public DateTime? LastVisitAt { get; set; }
    public DateTime? NextAppointmentAt { get; set; }
    public int VisitsCount { get; set; }
    /// <summary>Оплачено всего (тиыны, без оплат с баланса, за вычетом возвратов).</summary>
    public long TotalPaid { get; set; }
    /// <summary>Начислено по закрытым визитам (тиыны).</summary>
    public long TotalBilled { get; set; }
}

/// <summary>Реализация по умолчанию до переноса модулей визитов/расписания/кассы. Заменяется через [Dependency(ReplaceServices = true)].</summary>
[Dependency(TryRegister = true)]
public class NullPatientActivityProvider : IPatientActivityProvider, ITransientDependency
{
    public Task<IQueryable<Guid>?> GetVisitedSinceQueryAsync(DateTime sinceUtc) => Task.FromResult<IQueryable<Guid>?>(null);

    public Task<Dictionary<Guid, PatientActivity>> GetActivityAsync(IReadOnlyCollection<Guid> patientIds) =>
        Task.FromResult(new Dictionary<Guid, PatientActivity>());
}
