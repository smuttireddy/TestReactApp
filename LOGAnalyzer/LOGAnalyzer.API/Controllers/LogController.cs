using System.Collections.Concurrent;
using System.Threading.Channels;
using LOGAnalyzer.API.BackgroundServices;
using Microsoft.AspNetCore.Mvc;

namespace LOGAnalyzer.API.Controllers;

/// <summary>
/// MVC controller — serves the Upload and Results pages.
/// </summary>
public class LogController : Controller
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".log", ".txt"];

    private readonly Channel<LogJob> _queue;
    private readonly ConcurrentDictionary<string, Core.DTOs.LogAnalysisResponse> _jobStore;

    public LogController(
        Channel<LogJob> queue,
        ConcurrentDictionary<string, Core.DTOs.LogAnalysisResponse> jobStore)
    {
        _queue    = queue;
        _jobStore = jobStore;
    }

    // GET /Log/Upload
    [HttpGet]
    public IActionResult Upload() => View();

    // POST /Log/Upload
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError("", "Please select a .log or .txt file.");
            return View();
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
        {
            ModelState.AddModelError("", $"Only .log and .txt files are accepted.");
            return View();
        }

        if (file.Length > MaxFileSizeBytes)
        {
            ModelState.AddModelError("", "File must be 10 MB or smaller.");
            return View();
        }

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            bytes = ms.ToArray();
        }

        var jobId = Guid.NewGuid().ToString("N");
        await _queue.Writer.WriteAsync(new LogJob(jobId, file.FileName, bytes));

        return RedirectToAction(nameof(Results), new { jobId });
    }

    // GET /Log/Results/{jobId}
    [HttpGet]
    public IActionResult Results(string jobId)
    {
        ViewBag.JobId = jobId;
        return View();
    }
}
