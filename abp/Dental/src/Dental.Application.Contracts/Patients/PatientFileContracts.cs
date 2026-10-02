using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Dental.Storage;
using Volo.Abp.Application.Services;

namespace Dental.Patients;
public class PatientFileInput
{
    public Guid PatientId { get; set; }
    [Required, StringLength(200)] public string FileName { get; set; } = "";
    [Required, StringLength(100)] public string ContentType { get; set; } = "";
    [Range(1, FileRules.MaxSize)] public long Size { get; set; }
}
public record PatientFileDto(Guid Id, string FileName, string ContentType, long Size, DateTime CreationTime);
public record PatientFileUploadDto(Guid Id, UploadForm Upload);
public interface IPatientFileAppService : IApplicationService
{
    Task<List<PatientFileDto>> GetListAsync(Guid patientId);
    Task<PatientFileUploadDto> CreateUploadAsync(PatientFileInput input);
    Task CompleteAsync(Guid id);
    Task<string> GetDownloadAsync(Guid id);
    Task DeleteAsync(Guid id);
}
