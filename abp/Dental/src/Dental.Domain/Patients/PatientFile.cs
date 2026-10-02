using System;
using System.Threading.Tasks;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Dental.Patients;

public class PatientFile : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid PatientId { get; private set; }
    public string FileName { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public long Size { get; private set; }
    public string ObjectKey { get; private set; } = "";
    public DateTime UploadExpiresAt { get; private set; }
    public bool IsReady { get; private set; }
    protected PatientFile() { }
    public PatientFile(Guid id, Guid tenant, Guid patient, string name, string mime, long size, DateTime expires) : base(id)
    {
        TenantId = tenant; PatientId = patient; FileName = name; ContentType = mime; Size = size;
        ObjectKey = $"files/{tenant:N}/{id:N}"; UploadExpiresAt = expires;
    }
    public void Complete() => IsReady = true;
    internal void MoveTo(Guid patient) => PatientId = patient;
}

public class PatientFileMergeContributor(IRepository<PatientFile, Guid> files) : IPatientMergeContributor, ITransientDependency
{
    public string Name => "files";
    public async Task<int> MoveAsync(Guid fromPatientId, Guid toPatientId)
    {
        var rows = await files.GetListAsync(x => x.PatientId == fromPatientId);
        foreach (var row in rows) { row.MoveTo(toPatientId); await files.UpdateAsync(row); }
        return rows.Count;
    }
}
