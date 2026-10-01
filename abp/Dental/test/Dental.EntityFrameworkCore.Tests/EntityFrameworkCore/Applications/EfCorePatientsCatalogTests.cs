using Dental.PatientsCatalog;
using Xunit;

namespace Dental.EntityFrameworkCore.Applications;

[Collection(DentalTestConsts.CollectionDefinitionName)]
public class EfCorePatientsApprovalsAppTests : PatientsApprovalsAppTests<DentalEntityFrameworkCoreTestModule>
{
}

[Collection(DentalTestConsts.CollectionDefinitionName)]
public class EfCorePatientsCatalogDomainTests : PatientsCatalogDomainTests<DentalEntityFrameworkCoreTestModule>
{
}
