using System.Threading.Tasks;

namespace Dental.Finance;

/// <summary>Serializes visit, patient and cash mutations in a tenant transaction.</summary>
public interface IFinanceLock
{
    Task AcquireAsync();
}
