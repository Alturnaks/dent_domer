using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dental.Permissions;
using Dental.Storage;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace Dental.Patients;
[Authorize(DentalPermissions.Patients.View)]
public class PatientFileAppService(IRepository<PatientFile, Guid> files, IRepository<Patient, Guid> patients, IFileStorage storage)
    : DentalAppService, IPatientFileAppService
{
    public async Task<List<PatientFileDto>> GetListAsync(Guid patientId)
    {
        await patients.GetAsync(patientId);
        return (await files.GetListAsync(x => x.PatientId == patientId && x.IsReady))
            .OrderByDescending(x => x.CreationTime)
            .Select(x => new PatientFileDto(x.Id, x.FileName, x.ContentType, x.Size, x.CreationTime)).ToList();
    }
    [Authorize(DentalPermissions.Patients.Edit)]
    public async Task<PatientFileUploadDto> CreateUploadAsync(PatientFileInput input)
    {
        var patient = await patients.GetAsync(input.PatientId);
        if (patient.MergedIntoId != null) throw new UserFriendlyException("Откройте основную карточку пациента.");
        if (CurrentTenant.Id == null || !FileRules.IsAllowed(input.FileName, input.ContentType) || input.Size < 1 || input.Size > FileRules.MaxSize
            || input.FileName.Any(char.IsControl) || input.FileName.Contains('/') || input.FileName.Contains('\\'))
            throw new UserFriendlyException("Допустимы PDF, PNG и JPEG до 20 МБ.");
        var file = new PatientFile(GuidGenerator.Create(), CurrentTenant.Id.Value, input.PatientId,
            input.FileName, input.ContentType, input.Size, Clock.Now.AddMinutes(10));
        var form = storage.CreateUpload("pending/" + file.ObjectKey, file.ContentType, file.Size);
        await files.InsertAsync(file);
        return new(file.Id, form);
    }
    [Authorize(DentalPermissions.Patients.Edit)]
    public async Task CompleteAsync(Guid id)
    {
        var file = await files.GetAsync(id);
        if (file.IsReady) return;
        if (file.CreatorId != CurrentUser.Id || file.UploadExpiresAt < Clock.Now)
            throw new UserFriendlyException("Срок загрузки истёк. Загрузите файл повторно.");
        await storage.PromoteAsync("pending/" + file.ObjectKey, file.ObjectKey, file.ContentType, file.Size);
        file.Complete(); await files.UpdateAsync(file);
    }
    public async Task<string> GetDownloadAsync(Guid id)
    {
        var file = await files.GetAsync(id);
        if (!file.IsReady) throw new UserFriendlyException("Файл ещё не загружен.");
        return storage.Download(file.ObjectKey, file.FileName);
    }
    [Authorize(DentalPermissions.Patients.Edit)]
    public async Task DeleteAsync(Guid id)
    {
        // Soft deletion retains the private object for recovery; no further links are issued.
        await files.DeleteAsync(id);
    }
}
