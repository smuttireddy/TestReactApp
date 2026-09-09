namespace LOGAnalyzer.Core.Interfaces;

/// <summary>
/// Parses raw log file stream into individual non-blank log lines.
/// </summary>
public interface ILogParserService
{
    /// <summary>
    /// Reads a log file stream and returns all non-empty lines.
    /// </summary>
    Task<IEnumerable<string>> ParseLinesAsync(Stream stream);
}
