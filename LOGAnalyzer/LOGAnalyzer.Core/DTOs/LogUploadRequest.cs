namespace LOGAnalyzer.Core.DTOs;

/// <summary>
/// Request DTO for uploading a log file.
/// FileName and FileStream are populated by the API layer before passing to services.
/// </summary>
public class LogUploadRequest
{
    /// <summary>Original file name from the upload.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Raw file content stream.</summary>
    public Stream? FileStream { get; set; }
}
