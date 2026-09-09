namespace LOGAnalyzer.Core.Models;

/// <summary>
/// Represents a single analyzed error entry from a log file.
/// This is the central JSON output model.
/// </summary>
public class LogErrorEntry
{
    /// <summary>Name of the uploaded log file.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Timestamp extracted from the log line.</summary>
    public string Timestamp { get; set; } = string.Empty;

    /// <summary>The raw log line that contains the error.</summary>
    public string ErrorLine { get; set; } = string.Empty;

    /// <summary>Error code extracted from the log line (e.g. E1001, ERR-404). Defaults to UNKNOWN.</summary>
    public string ErrorCode { get; set; } = "UNKNOWN";

    /// <summary>AI-determined root cause of the error.</summary>
    public string RootCause { get; set; } = string.Empty;

    /// <summary>AI-suggested solution for the error.</summary>
    public string Solution { get; set; } = string.Empty;

    /// <summary>Severity level: Low, Medium, High, Critical.</summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>Error category: Database, Network, Authentication, Application, System, etc.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Recommended next action for the operator.</summary>
    public string RecommendedAction { get; set; } = string.Empty;
}
