using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Dental.Patients;

/// <summary>Пациенты: список с поиском/фильтрами, карточка, дубли и слияние, согласия, баланс.</summary>
public interface IPatientAppService : IApplicationService
{
    Task<PagedResultDto<PatientListItemDto>> GetListAsync(GetPatientListInput input);
    Task<PatientDto> GetAsync(Guid id);

    /// <summary>Совпадение ИИН → Dental:IinTaken; телефона → Dental:PatientDuplicate (data.duplicates), если не IgnoreDuplicates.</summary>
    Task<PatientDto> CreateAsync(CreateUpdatePatientDto input);
    Task<PatientDto> UpdateAsync(Guid id, CreateUpdatePatientDto input);

    /// <summary>Быстрый поиск (шапка, выбор пациента в записи/визите/кассе).</summary>
    Task<ListResultDto<PatientLookupDto>> GetLookupAsync(string? filter, int maxResultCount = 10);

    Task<ListResultDto<DuplicateCandidateDto>> CheckDuplicatesAsync(CheckDuplicatesInput input);
    Task<ListResultDto<DuplicatePairDto>> GetDuplicatesAsync();
    Task<PatientDto> MergeAsync(MergePatientsInput input);

    Task<PatientBalanceDto> GetBalanceAsync(Guid id);
    Task<ListResultDto<PatientConsentDto>> GetConsentsAsync(Guid id);
    Task<PatientConsentDto> AddConsentAsync(Guid id, CreatePatientConsentDto input);

    /// <summary>Все используемые теги (для фильтра).</summary>
    Task<List<string>> GetTagsAsync();
}

public interface ILeadSourceAppService : IApplicationService
{
    Task<ListResultDto<LeadSourceDto>> GetListAsync();
    Task<LeadSourceDto> CreateAsync(CreateUpdateLeadSourceDto input);
    Task<LeadSourceDto> UpdateAsync(Guid id, CreateUpdateLeadSourceDto input);
    Task DeleteAsync(Guid id);
}
