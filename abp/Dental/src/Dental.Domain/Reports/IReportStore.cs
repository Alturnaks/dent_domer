using System.Threading.Tasks;
namespace Dental.Reports;
public interface IReportStore { Task<ReportTable> ExecuteAsync(ReportQuery query); }
public interface IReportDocumentRenderer { byte[] Render(ReportTable table, string title, string format); }
