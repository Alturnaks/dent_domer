using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Dental.Data;

/* This is used if database provider does't define
 * IDentalDbSchemaMigrator implementation.
 */
public class NullDentalDbSchemaMigrator : IDentalDbSchemaMigrator, ITransientDependency
{
    public Task MigrateAsync()
    {
        return Task.CompletedTask;
    }
}
