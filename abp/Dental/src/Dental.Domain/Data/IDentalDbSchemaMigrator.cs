using System.Threading.Tasks;

namespace Dental.Data;

public interface IDentalDbSchemaMigrator
{
    Task MigrateAsync();
}
