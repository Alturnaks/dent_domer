using System;
using System.Threading.Tasks;
using Dental.Patients;
using Dental.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dental.Web.Pages.Patients;

/// <summary>
/// Карточка пациента. Вкладки «Записи», «Визиты», «Платежи» — частичные представления в Pages/Patients/Tabs,
/// модули расписания/визитов/кассы заменяют их своим содержимым (получают PatientDto как модель).
/// </summary>
[Authorize(DentalPermissions.Patients.View)]
public class DetailModel : DentalPageModel
{
    private readonly IPatientAppService _patients;

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public PatientDto Patient { get; set; } = null!;

    public DetailModel(IPatientAppService patients) => _patients = patients;

    public async Task OnGetAsync()
    {
        Patient = await _patients.GetAsync(Id);
    }
}
