using LOGAnalyzer.Core.Models;

namespace LOGAnalyzer.Core.Interfaces;

/// <summary>
/// Exports a collection of <see cref="LogErrorEntry"/> records to an Excel (.xlsx) file.
/// </summary>
public interface IExcelExportService
{
    /// <summary>
    /// Generates an Excel workbook from the given entries and returns the raw bytes.
    /// </summary>
    byte[] Export(IEnumerable<LogErrorEntry> entries, string fileName);
}
