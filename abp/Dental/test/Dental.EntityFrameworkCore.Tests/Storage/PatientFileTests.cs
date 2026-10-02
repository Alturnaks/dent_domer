using System;
using System.Threading.Tasks;
using Dental.Patients;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Dental.EntityFrameworkCore.Storage;
[Collection(DentalTestConsts.CollectionDefinitionName)]
public class PatientFileTests : DentalEntityFrameworkCoreTestBase
{
    [Fact]
    public async Task FilesAreTenantIsolatedAndSoftDeleted()
    {
        var tenant = GetRequiredService<ICurrentTenant>();
        var patients = GetRequiredService<IRepository<Patient, Guid>>();
        var files = GetRequiredService<IRepository<PatientFile, Guid>>();
        var clinic = Guid.NewGuid(); var patientId = Guid.NewGuid(); var fileId = Guid.NewGuid();
        using (tenant.Change(clinic))
        {
            await WithUnitOfWorkAsync(async () => {
                await patients.InsertAsync(new Patient(patientId, clinic, "File", "Test"), autoSave: true);
                var file = new PatientFile(fileId, clinic, patientId, "test.pdf", "application/pdf", 10, DateTime.UtcNow.AddMinutes(10));
                file.Complete(); await files.InsertAsync(file, autoSave: true);
            });
        }
        using (tenant.Change(Guid.NewGuid()))
            await WithUnitOfWorkAsync(async () => Assert.Null(await files.FindAsync(fileId)));
        using (tenant.Change(clinic))
        {
            await WithUnitOfWorkAsync(async () => {
                Assert.NotNull(await files.FindAsync(fileId)); await files.DeleteAsync(fileId, autoSave: true);
            });
            await WithUnitOfWorkAsync(async () => Assert.Null(await files.FindAsync(fileId)));
        }
    }
    [Fact]
    public async Task MergeMovesFilesWithoutChangingStorageKeys()
    {
        var clinic = Guid.NewGuid(); using var tenant = GetRequiredService<ICurrentTenant>().Change(clinic);
        var patients = GetRequiredService<IRepository<Patient, Guid>>();
        var files = GetRequiredService<IRepository<PatientFile, Guid>>();
        var from = Guid.NewGuid(); var to = Guid.NewGuid(); var id = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () => {
            await patients.InsertAsync(new Patient(from, clinic, "From", "Test"), autoSave: true);
            await patients.InsertAsync(new Patient(to, clinic, "To", "Test"), autoSave: true);
            var file = new PatientFile(id, clinic, from, "scan.pdf", "application/pdf", 10, DateTime.UtcNow.AddMinutes(10));
            file.Complete(); await files.InsertAsync(file, autoSave: true);
        });
        await WithUnitOfWorkAsync(async () => Assert.Equal(1, await GetRequiredService<PatientFileMergeContributor>().MoveAsync(from, to)));
        await WithUnitOfWorkAsync(async () => {
            var file = await files.GetAsync(id); Assert.Equal(to, file.PatientId);
            Assert.Equal($"files/{clinic:N}/{id:N}", file.ObjectKey); Assert.True(file.IsReady);
        });
    }
}
