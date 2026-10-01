using Localization.Resources.AbpUi;
using Dental.Localization;
using Volo.Abp.Account;
using Volo.Abp.SettingManagement;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement.HttpApi;
using Volo.Abp.Localization;
using Volo.Abp.TenantManagement;

namespace Dental;

 [DependsOn(
    typeof(DentalApplicationContractsModule),
    typeof(AbpPermissionManagementHttpApiModule),
    typeof(AbpSettingManagementHttpApiModule),
    typeof(AbpAccountHttpApiModule),
    typeof(AbpIdentityHttpApiModule),
    typeof(AbpTenantManagementHttpApiModule),
    typeof(AbpFeatureManagementHttpApiModule)
    )]
public class DentalHttpApiModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ConfigureLocalization();
        ConfigureErrorStatusCodes();
    }

    /// <summary>HTTP-коды для бизнес-ошибок модулей (по умолчанию ABP отдаёт 403). Модули дописывают свои коды.</summary>
    private void ConfigureErrorStatusCodes()
    {
        Configure<Volo.Abp.AspNetCore.ExceptionHandling.AbpExceptionHttpStatusCodeOptions>(options =>
        {
            options.Map(DentalDomainErrorCodes.AppointmentSlotConflict, System.Net.HttpStatusCode.Conflict);
            options.Map(DentalDomainErrorCodes.AppointmentDoctorNotWorking, System.Net.HttpStatusCode.UnprocessableEntity);
            options.Map(DentalDomainErrorCodes.AppointmentInvalidStatus, System.Net.HttpStatusCode.Conflict);
            options.Map(DentalDomainErrorCodes.AppointmentReasonRequired, System.Net.HttpStatusCode.BadRequest);
            options.Map(DentalDomainErrorCodes.ScheduleHasAppointments, System.Net.HttpStatusCode.Conflict);
            options.Map(DentalDomainErrorCodes.ScheduleInvalidRange, System.Net.HttpStatusCode.BadRequest);
            options.Map(DentalDomainErrorCodes.ScheduleDoctorInvalid, System.Net.HttpStatusCode.BadRequest);
            options.Map(DentalDomainErrorCodes.ScheduleChairInvalid, System.Net.HttpStatusCode.BadRequest);
            options.Map(DentalDomainErrorCodes.ScheduleOverlappingShifts, System.Net.HttpStatusCode.Conflict);
            options.Map(DentalDomainErrorCodes.PatientDuplicate, System.Net.HttpStatusCode.Conflict);
            options.Map(DentalDomainErrorCodes.IinTaken, System.Net.HttpStatusCode.Conflict);
            options.Map(DentalDomainErrorCodes.InvalidIin, System.Net.HttpStatusCode.BadRequest);
            options.Map(DentalDomainErrorCodes.InvalidPhone, System.Net.HttpStatusCode.BadRequest);
            options.Map(DentalDomainErrorCodes.InvalidBirthDate, System.Net.HttpStatusCode.BadRequest);
            options.Map(DentalDomainErrorCodes.PatientMergeInvalid, System.Net.HttpStatusCode.BadRequest);
            options.Map(DentalDomainErrorCodes.CategoryParentInvalid, System.Net.HttpStatusCode.BadRequest);
            options.Map(DentalDomainErrorCodes.ServiceCodeAlreadyExists, System.Net.HttpStatusCode.Conflict);
            options.Map(DentalDomainErrorCodes.PriceInvalid, System.Net.HttpStatusCode.BadRequest);
            options.Map(DentalDomainErrorCodes.TechCardItemInvalid, System.Net.HttpStatusCode.BadRequest);
            options.Map(DentalDomainErrorCodes.ApprovalNotPending, System.Net.HttpStatusCode.Conflict);
            // Склад: справочники (документы бросают StockException с собственным кодом HTTP).
            options.Map(DentalDomainErrorCodes.WarehouseNotEmpty, System.Net.HttpStatusCode.Conflict);
            options.Map(DentalDomainErrorCodes.ItemSkuAlreadyExists, System.Net.HttpStatusCode.Conflict);
            options.Map(DentalDomainErrorCodes.ItemBaseUnitLocked, System.Net.HttpStatusCode.Conflict);
            options.Map(DentalDomainErrorCodes.WarehouseBranchRequired, System.Net.HttpStatusCode.BadRequest);
            options.Map(DentalDomainErrorCodes.CategoryCycle, System.Net.HttpStatusCode.BadRequest);
            options.Map(DentalDomainErrorCodes.ItemSerialRequiresPcs, System.Net.HttpStatusCode.BadRequest);
            options.Map(DentalDomainErrorCodes.ItemUnitNotFound, System.Net.HttpStatusCode.BadRequest);
            options.Map(DentalDomainErrorCodes.StockLevelInvalid, System.Net.HttpStatusCode.BadRequest);
            options.Map(DentalDomainErrorCodes.SupplierBinInvalid, System.Net.HttpStatusCode.BadRequest);
        });
    }

    private void ConfigureLocalization()
    {
        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Get<DentalResource>()
                .AddBaseTypes(
                    typeof(AbpUiResource)
                );
        });
    }
}
