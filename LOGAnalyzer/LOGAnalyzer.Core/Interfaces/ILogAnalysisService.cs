using LOGAnalyzer.Core.DTOs;

namespace LOGAnalyzer.Core.Interfaces;

/// <summary>
/// Analyzes parsed log lines and produces structured error entries with
/// root cause, solution, severity, category, and recommended action.
/// </summary>
public interface ILogAnalysisService
{
    /// <summary>
    /// Analyzes the given log lines and returns a full <see cref="LogAnalysisResponse"/>.
    /// </summary>
    /// <param name="fileName">Name of the source log file.</param>
    /// <param name="lines">All non-empty lines parsed from the file.</param>
    Task<LogAnalysisResponse> AnalyzeAsync(string fileName, IEnumerable<string> lines);
}
