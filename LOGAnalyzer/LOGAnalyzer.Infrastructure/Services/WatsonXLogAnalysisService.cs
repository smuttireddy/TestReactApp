using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LOGAnalyzer.Core.DTOs;
using LOGAnalyzer.Core.Interfaces;
using LOGAnalyzer.Core.Models;
using LOGAnalyzer.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LOGAnalyzer.Infrastructure.Services;

/// <summary>
/// IBM watsonx.ai powered log analysis service.
/// Calls the Granite model via REST API to analyze each error line.
/// Falls back to rule-based analysis if watsonx is unavailable.
/// </summary>
public class WatsonXLogAnalysisService : ILogAnalysisService
{
    private readonly WatsonXSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogAnalysisService _fallback;
    private readonly ILogger<WatsonXLogAnalysisService> _logger;

    private static readonly Regex TimestampRegex =
        new(@"(\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2})", RegexOptions.Compiled);

    private static readonly Regex ErrorCodeRegex =
        new(@"\b(E\d{3,6}|ERR[-_]?\d+|0x[0-9A-Fa-f]{4,8}|HTTP[/ ]\d{3}|\b[45]\d{2}\b)\b", RegexOptions.Compiled);

    private static readonly string[] ErrorKeywords =
    [
        "ERROR", "FATAL", "CRITICAL", "EXCEPTION", "UNHANDLED",
        "WARN", "WARNING", "FAILED", "FAILURE", "TIMEOUT",
        "UNAUTHORIZED", "FORBIDDEN", "NULL", "NULLREFERENCE",
        "STACKOVERFLOW", "OUTOFMEMORY", "DEADLOCK", "CONNECTIONREFUSED",
        "SQLEXCEPTION", "SOCKET", "NETWORK", "IOEXCEPTION",
        "ACCESSDENIED", "NOTFOUND", "BADREQUEST", "SERVERERROR"
    ];

    public WatsonXLogAnalysisService(
        IOptions<WatsonXSettings> settings,
        IHttpClientFactory httpClientFactory,
        ILogAnalysisService fallback,
        ILogger<WatsonXLogAnalysisService> logger)
    {
        _settings          = settings.Value;
        _httpClientFactory = httpClientFactory;
        _fallback          = fallback;
        _logger            = logger;
    }

    // -----------------------------------------------------------------------
    // Main entry point
    // -----------------------------------------------------------------------
    public async Task<LogAnalysisResponse> AnalyzeAsync(string fileName, IEnumerable<string> lines)
    {
        var lineList = lines.ToList();

        // Validate settings before trying watsonx
        if (string.IsNullOrWhiteSpace(_settings.ApiKey) ||
            _settings.ApiKey == "YOUR_IBM_CLOUD_API_KEY")
        {
            _logger.LogWarning("WatsonX ApiKey is not configured — using rule-based fallback.");
            return await _fallback.AnalyzeAsync(fileName, lineList);
        }

        _logger.LogInformation("WatsonX: Starting analysis of '{FileName}' with {Count} lines.", fileName, lineList.Count);

        // Step 1 — Get IAM token
        var iamToken = await GetIamTokenAsync();
        if (string.IsNullOrEmpty(iamToken))
        {
            _logger.LogWarning("WatsonX: Failed to get IAM token — using rule-based fallback.");
            return await _fallback.AnalyzeAsync(fileName, lineList);
        }

        _logger.LogInformation("WatsonX: IAM token obtained successfully.");

        // Step 2 — Analyze each error line
        var errors = new List<LogErrorEntry>();
        foreach (var line in lineList)
        {
            var upper = line.ToUpperInvariant();
            if (!ErrorKeywords.Any(k => upper.Contains(k))) continue;

            _logger.LogInformation("WatsonX: Analyzing line — {Line}", line[..Math.Min(80, line.Length)]);

            var entry = await AnalyzeLineAsync(line, fileName, iamToken)
                        ?? BuildFallbackEntry(line, fileName);
            errors.Add(entry);
        }

        _logger.LogInformation("WatsonX: Analysis complete — {Count} errors found.", errors.Count);

        return new LogAnalysisResponse
        {
            FileName        = fileName,
            AnalyzedAt      = DateTime.UtcNow,
            TotalLines      = lineList.Count,
            TotalErrors     = errors.Count,
            SeveritySummary = errors.GroupBy(e => e.Severity).ToDictionary(g => g.Key, g => g.Count()),
            CategorySummary = errors.GroupBy(e => e.Category).ToDictionary(g => g.Key, g => g.Count()),
            Errors          = errors
        };
    }

    // -----------------------------------------------------------------------
    // Call watsonx.ai text generation endpoint for one line
    // -----------------------------------------------------------------------
    private async Task<LogErrorEntry?> AnalyzeLineAsync(string line, string fileName, string iamToken)
    {
        try
        {
            var requestBody = new
            {
                model_id   = _settings.ModelId,
                project_id = _settings.ProjectId,
                input      = BuildPrompt(line),
                parameters = new
                {
                    decoding_method = "greedy",
                    max_new_tokens  = 300
                }
            };

            var json    = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var client = _httpClientFactory.CreateClient("watsonx");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", iamToken);

            var url      = $"{_settings.ServiceUrl.TrimEnd('/')}/ml/v1/text/generation?version=2023-05-29";
            _logger.LogInformation("WatsonX: POST {Url}", url);

            var response = await client.PostAsync(url, content);
            var body     = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("WatsonX: Response status {Status}", response.StatusCode);
            _logger.LogDebug("WatsonX: Response body {Body}", body);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("WatsonX: Non-success response — {Status} — {Body}", response.StatusCode, body);
                return null;
            }

            return ParseResponse(body, line, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WatsonX: Exception during line analysis.");
            return null;
        }
    }

    // -----------------------------------------------------------------------
    // Get IBM IAM Bearer token using API key
    // -----------------------------------------------------------------------
    private async Task<string> GetIamTokenAsync()
    {
        try
        {
            _logger.LogInformation("WatsonX: Requesting IAM token from https://iam.cloud.ibm.com/identity/token");

            var client  = _httpClientFactory.CreateClient();
            var payload = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ibm:params:oauth:grant-type:apikey",
                ["apikey"]     = _settings.ApiKey
            });

            var response = await client.PostAsync("https://iam.cloud.ibm.com/identity/token", payload);
            var body     = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("WatsonX: IAM response status {Status}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("WatsonX: IAM token request failed — {Status} — {Body}", response.StatusCode, body);
                return string.Empty;
            }

            using var doc   = JsonDocument.Parse(body);
            var accessToken = doc.RootElement.GetProperty("access_token").GetString() ?? string.Empty;

            _logger.LogInformation("WatsonX: IAM token received (length={Len}).", accessToken.Length);
            return accessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WatsonX: Exception getting IAM token.");
            return string.Empty;
        }
    }

    // -----------------------------------------------------------------------
    // Build structured prompt for Granite model
    // -----------------------------------------------------------------------
    private static string BuildPrompt(string logLine)
    {
        return "You are a log analysis expert. Analyze the following log line and respond ONLY with a valid JSON object.\n\n" +
               "Log line:\n" + logLine + "\n\n" +
               "Respond with this exact JSON structure (no markdown, no explanation):\n" +
               "{\n" +
               "  \"RootCause\": \"brief root cause\",\n" +
               "  \"Solution\": \"step to fix it\",\n" +
               "  \"Severity\": \"Low or Medium or High or Critical\",\n" +
               "  \"Category\": \"Application or Database or Network or Authentication or System\",\n" +
               "  \"RecommendedAction\": \"next action for the operator\"\n" +
               "}";
    }

    // -----------------------------------------------------------------------
    // Parse watsonx response JSON into LogErrorEntry
    // -----------------------------------------------------------------------
    private LogErrorEntry? ParseResponse(string responseJson, string line, string fileName)
    {
        try
        {
            using var doc     = JsonDocument.Parse(responseJson);
            var generatedText = doc.RootElement
                                   .GetProperty("results")[0]
                                   .GetProperty("generated_text")
                                   .GetString() ?? string.Empty;

            _logger.LogInformation("WatsonX: Generated text — {Text}", generatedText[..Math.Min(200, generatedText.Length)]);

            var start = generatedText.IndexOf('{');
            var end   = generatedText.LastIndexOf('}');
            if (start < 0 || end < 0)
            {
                _logger.LogWarning("WatsonX: No JSON object found in generated text.");
                return null;
            }

            var jsonSnippet  = generatedText[start..(end + 1)];
            using var parsed = JsonDocument.Parse(jsonSnippet);
            var root         = parsed.RootElement;

            return new LogErrorEntry
            {
                FileName          = fileName,
                Timestamp         = ExtractTimestamp(line),
                ErrorLine         = line.Length > 500 ? line[..500] : line,
                ErrorCode         = ExtractErrorCode(line),
                RootCause         = GetString(root, "RootCause"),
                Solution          = GetString(root, "Solution"),
                Severity          = GetString(root, "Severity"),
                Category          = GetString(root, "Category"),
                RecommendedAction = GetString(root, "RecommendedAction")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WatsonX: Failed to parse response JSON.");
            return null;
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    private static string GetString(JsonElement root, string key) =>
        root.TryGetProperty(key, out var val) ? val.GetString() ?? string.Empty : string.Empty;

    private static string ExtractTimestamp(string line)
    {
        var m = TimestampRegex.Match(line);
        return m.Success ? m.Groups[1].Value : string.Empty;
    }

    private static string ExtractErrorCode(string line)
    {
        var m = ErrorCodeRegex.Match(line);
        return m.Success ? m.Value : "UNKNOWN";
    }

    private static LogErrorEntry BuildFallbackEntry(string line, string fileName) => new()
    {
        FileName          = fileName,
        Timestamp         = ExtractTimestamp(line),
        ErrorLine         = line.Length > 500 ? line[..500] : line,
        ErrorCode         = ExtractErrorCode(line),
        RootCause         = "Error detected — AI analysis unavailable.",
        Solution          = "Review the log line manually.",
        Severity          = "Medium",
        Category          = "Application",
        RecommendedAction = "Inspect the log context and escalate if needed."
    };
}
