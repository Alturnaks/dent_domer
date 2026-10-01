using System.Threading.Tasks;
namespace Dental.Audit;
public interface IAuditStore { Task<AuditPage> GetAsync(AuditQuery query); }
