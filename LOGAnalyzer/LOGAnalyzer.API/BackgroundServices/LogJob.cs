namespace LOGAnalyzer.API.BackgroundServices;

/// <summary>
/// Represents a queued log processing job.
/// </summary>
public record LogJob(string JobId, string FileName, byte[] FileBytes);
