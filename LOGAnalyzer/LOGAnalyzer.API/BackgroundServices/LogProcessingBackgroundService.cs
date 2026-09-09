using System.Collections.Concurrent;
using System.Threading.Channels;
using LOGAnalyzer.Core.DTOs;
using LOGAnalyzer.Core.Interfaces;

namespace LOGAnalyzer.API.BackgroundServices;

/// <summary>
/// Hosted background service that drains the <see cref="Channel{LogJob}"/> queue,
/// runs the parser and analyzer, and stores results for polling.
/// </summary>
public class LogProcessingBackgroundService : BackgroundService
{
    private readonly Channel<LogJob> _queue;
    private readonly ILogParserService _parser;
    private readonly ILogAnalysisService _analyzer;
    private readonly ConcurrentDictionary<string, LogAnalysisResponse> _jobStore;
    private readonly ILogger<LogProcessingBackgroundService> _logger;

    public LogProcessingBackgroundService(
        Channel<LogJob> queue,
        ILogParserService parser,
        ILogAnalysisService analyzer,
        ConcurrentDictionary<string, LogAnalysisResponse> jobStore,
        ILogger<LogProcessingBackgroundService> logger)
    {
        _queue     = queue;
        _parser    = parser;
        _analyzer  = analyzer;
        _jobStore  = jobStore;
        _logger    = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LogProcessingBackgroundService started.");

        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            _logger.LogInformation("Processing job {JobId} for file '{FileName}'.", job.JobId, job.FileName);
            try
            {
                using var stream = new MemoryStream(job.FileBytes);
                var lines    = await _parser.ParseLinesAsync(stream);
                var response = await _analyzer.AnalyzeAsync(job.FileName, lines);
                _jobStore[job.JobId] = response;
                _logger.LogInformation("Job {JobId} completed — {Count} errors found.", job.JobId, response.TotalErrors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed.", job.JobId);
                // Store a failed response so the polling endpoint stops waiting
                _jobStore[job.JobId] = new LogAnalysisResponse
                {
                    FileName   = job.FileName,
                    AnalyzedAt = DateTime.UtcNow,
                    Errors     = []
                };
            }
        }
    }
}
