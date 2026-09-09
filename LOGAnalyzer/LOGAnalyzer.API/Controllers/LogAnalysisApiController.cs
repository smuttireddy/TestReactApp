using System.Collections.Concurrent;
using System.Threading.Channels;
using LOGAnalyzer.API.BackgroundServices;
using LOGAnalyzer.Core.DTOs;
using LOGAnalyzer.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LOGAnalyzer.API.Controllers;

/// <summary>
/// REST API controller for log file upload, analysis polling, and Excel export.
/// </summary>
[ApiController]
[Route("api/loganalysis")]
public class LogAnalysisApiController : ControllerBase
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    private static readonly string[] AllowedExtensions = [".log", ".txt"];

    private readonly Channel<LogJob> _queue;
    private readonly ConcurrentDictionary<string, LogAnalysisResponse> _jobStore;
    private readonly IExcelExportService _excelExport;
    private readonly ILogger<LogAnalysisApiController> _logger;

    public LogAnalysisApiController(
        Channel<LogJob> queue,
        ConcurrentDictionary<string, LogAnalysisResponse> jobStore,
        IExcelExportService excelExport,
        ILogger<LogAnalysisApiController> logger)
    {
        _queue       = queue;
        _jobStore    = jobStore;
        _excelExport = excelExport;
        _logger      = logger;
    }

    // -----------------------------------------------------------------------
    // POST /api/loganalysis/upload
    // -----------------------------------------------------------------------

    /// <summary>
    /// Validates and queues a log file for background analysis.
    /// Returns 202 Accepted with a jobId to poll for results.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<IActionResult> Upload(IFormFile? file)
    {
        // --- Validation ---
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file provided or file is empty." });

        if (file.Length > MaxFileSizeBytes)
            return BadRequest(new { error = $"File size exceeds the 10 MB limit." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return BadRequest(new { error = $"Only .log and .txt files are accepted. Received: '{ext}'" });

        // --- Read file bytes (must happen inside the request) ---
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            bytes = ms.ToArray();
        }

        // --- Enqueue ---
        var jobId = Guid.NewGuid().ToString("N");
        var job   = new LogJob(jobId, file.FileName, bytes);
        await _queue.Writer.WriteAsync(job);

        _logger.LogInformation("Queued job {JobId} for file '{FileName}'.", jobId, file.FileName);

        return Accepted(new { jobId, message = "File accepted. Poll /api/loganalysis/result/{jobId} for results." });
    }

    // -----------------------------------------------------------------------
    // GET /api/loganalysis/result/{jobId}
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the analysis result for a completed job.
    /// Returns 202 while the job is still processing.
    /// </summary>
    [HttpGet("result/{jobId}")]
    public IActionResult GetResult(string jobId)
    {
        if (_jobStore.TryGetValue(jobId, out var result))
            return Ok(result);

        return Accepted(new { jobId, message = "Processing — please try again in a moment." });
    }

    // -----------------------------------------------------------------------
    // GET /api/loganalysis/export/{jobId}
    // -----------------------------------------------------------------------

    /// <summary>
    /// Exports the analysis result for a completed job as an Excel (.xlsx) file.
    /// </summary>
    [HttpGet("export/{jobId}")]
    public IActionResult Export(string jobId)
    {
        if (!_jobStore.TryGetValue(jobId, out var result))
            return NotFound(new { error = "Job not found or still processing." });

        var bytes = _excelExport.Export(result.Errors, result.FileName);
        var downloadName = $"LogAnalysis_{Path.GetFileNameWithoutExtension(result.FileName)}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", downloadName);
    }
}
