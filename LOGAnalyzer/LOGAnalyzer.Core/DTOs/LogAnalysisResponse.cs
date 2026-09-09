using LOGAnalyzer.Core.Models;

namespace LOGAnalyzer.Core.DTOs;

/// <summary>
/// Full analysis response returned as JSON and used to generate the Excel export.
/// </summary>
public class LogAnalysisResponse
{
    /// <summary>Name of the analyzed log file.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Date and time the analysis was performed.</summary>
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Total number of lines parsed from the file.</summary>
    public int TotalLines { get; set; }

    /// <summary>Total number of error/warning entries found.</summary>
    public int TotalErrors { get; set; }

    /// <summary>Breakdown of errors by severity.</summary>
    public Dictionary<string, int> SeveritySummary { get; set; } = new();

    /// <summary>Breakdown of errors by category.</summary>
    public Dictionary<string, int> CategorySummary { get; set; } = new();

    /// <summary>The detailed list of analyzed error entries.</summary>
    public List<LogErrorEntry> Errors { get; set; } = new();
}
