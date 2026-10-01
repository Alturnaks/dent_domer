using System;
using System.Threading;
using System.Threading.Tasks;
namespace Dental.Reports;
public interface IOperationsRunner { Task RunTenantAsync(Guid tenant,CancellationToken cancellationToken=default); }
