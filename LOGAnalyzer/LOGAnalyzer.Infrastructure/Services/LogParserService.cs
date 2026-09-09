using LOGAnalyzer.Core.Interfaces;

namespace LOGAnalyzer.Infrastructure.Services;

/// <summary>
/// Reads a log file stream line-by-line and returns all non-blank lines.
/// </summary>
public class LogParserService : ILogParserService
{
    public async Task<IEnumerable<string>> ParseLinesAsync(Stream stream)
    {
        var lines = new List<string>();
        using var reader = new StreamReader(stream, leaveOpen: true);
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line.Trim());
        }
        return lines;
    }
}
