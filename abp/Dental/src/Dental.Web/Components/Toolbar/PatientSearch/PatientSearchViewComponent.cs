using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;

namespace Dental.Web.Components.Toolbar.PatientSearch;

/// <summary>Глобальный поиск пациента в шапке (логика — wwwroot/js/dental-common.js).</summary>
public class PatientSearchViewComponent : AbpViewComponent
{
    public virtual IViewComponentResult Invoke() => View("~/Components/Toolbar/PatientSearch/Default.cshtml");
}
