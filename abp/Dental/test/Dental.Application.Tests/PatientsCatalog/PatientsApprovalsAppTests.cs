using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dental.Approvals;
using Dental.Patients;
using Shouldly;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Dental.PatientsCatalog;

/// <summary>Тестовый обработчик подтверждений (проверка реестра IApprovalHandler).</summary>
public class TestApprovalHandler : IApprovalHandler, ISingletonDependency
{
    public const string TypeName = "Test.Custom";
    public List<(Guid Id, bool Approved)> Calls { get; } = [];
    public string Type => TypeName;

    public Task OnApprovedAsync(ApprovalRequest request)
    {
        Calls.Add((request.Id, true));
        return Task.CompletedTask;
    }

    public Task OnRejectedAsync(ApprovalRequest request)
    {
        Calls.Add((request.Id, false));
        return Task.CompletedTask;
    }
}

public abstract class PatientsApprovalsAppTests<TStartupModule> : DentalApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    [Fact]
    public async Task Create_Patient_Detects_Phone_And_Iin_Duplicates()
    {
        var service = GetRequiredService<IPatientAppService>();
        var first = await service.CreateAsync(new CreateUpdatePatientDto { LastName = "Омаров", FirstName = "Арман", Phone = "+7 705 111 22 33", Iin = "990913467293" });
        first.Phone.ShouldBe("77051112233");

        var dup = await Should.ThrowAsync<BusinessException>(() =>
            service.CreateAsync(new CreateUpdatePatientDto { LastName = "Омаров", FirstName = "А.", Phone = "8 705 111 22 33" }));
        dup.Code.ShouldBe(DentalDomainErrorCodes.PatientDuplicate);

        var iin = await Should.ThrowAsync<BusinessException>(() =>
            service.CreateAsync(new CreateUpdatePatientDto { LastName = "Другой", FirstName = "Человек", Iin = "990913467293", IgnoreDuplicates = true }));
        iin.Code.ShouldBe(DentalDomainErrorCodes.IinTaken);

        var second = await service.CreateAsync(new CreateUpdatePatientDto { LastName = "Омаров", FirstName = "А.", Phone = "8 705 111 22 33", IgnoreDuplicates = true });
        (await service.GetLookupAsync("77051112")).Items.Count.ShouldBe(2);
        (await service.GetBalanceAsync(second.Id)).Balance.ShouldBe(0);
    }

    [Fact]
    public async Task Limit_Exceeded_Creates_Single_Pending_Request()
    {
        var manager = GetRequiredService<ApprovalManager>();
        var entityId = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            // У тестового пользователя нет ролей → лимиты «всё запрещено».
            var r1 = await manager.CheckOrRequestAsync(ApprovalTypes.Discount, "Visit", entityId, 10, "Скидка 10%", new { pct = 10 }, null);
            r1.Allowed.ShouldBeFalse();
            var r2 = await manager.CheckOrRequestAsync(ApprovalTypes.Discount, "Visit", entityId, 12, "Скидка 12%", new { pct = 12 }, null);
            r2.Request!.Id.ShouldBe(r1.Request!.Id);
            r2.Request.Amount.ShouldBe(12);
            (await manager.CheckOrRequestAsync(ApprovalTypes.PurchaseOrder, "Order", Guid.NewGuid(), 1, "", new { }, null)).Allowed.ShouldBeTrue();
        });
        await WithUnitOfWorkAsync(async () =>
        {
            (await GetRequiredService<IRepository<ApprovalRequest, Guid>>().CountAsync(a => a.EntityId == entityId)).ShouldBe(1);
        });
    }

    [Fact]
    public async Task Decide_Calls_Handler_And_Forbids_Own_Request()
    {
        var manager = GetRequiredService<ApprovalManager>();
        var repo = GetRequiredService<IRepository<ApprovalRequest, Guid>>();
        var handler = GetRequiredService<TestApprovalHandler>();
        var currentUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");
        var foreign = Guid.NewGuid();
        var own = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            var a = new ApprovalRequest(foreign, null, TestApprovalHandler.TypeName, "Thing", Guid.NewGuid(), null, Guid.NewGuid());
            a.SetDetails(1, "чужой", "{}");
            var b = new ApprovalRequest(own, null, TestApprovalHandler.TypeName, "Thing", Guid.NewGuid(), null, currentUserId);
            b.SetDetails(1, "свой", "{}");
            await repo.InsertManyAsync([a, b], autoSave: true);
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var decided = await manager.DecideAsync(foreign, true, "ок");
            decided.Status.ShouldBe(ApprovalStatus.Approved);
            handler.Calls.ShouldContain((foreign, true));
            (await Should.ThrowAsync<BusinessException>(() => manager.DecideAsync(own, false, null))).Code.ShouldBe(DentalDomainErrorCodes.ApprovalOwnRequest);
        });
        await WithUnitOfWorkAsync(async () =>
        {
            (await Should.ThrowAsync<BusinessException>(() => manager.DecideAsync(foreign, false, null))).Code.ShouldBe(DentalDomainErrorCodes.ApprovalNotPending);
        });
    }
}
