using System.Text.RegularExpressions;
using LOGAnalyzer.Core.DTOs;
using LOGAnalyzer.Core.Interfaces;
using LOGAnalyzer.Core.Models;

namespace LOGAnalyzer.Infrastructure.Services;

/// <summary>
/// Analyzes parsed log lines using keyword/pattern detection to simulate AI analysis.
/// Populates all 9 fields of <see cref="LogErrorEntry"/> per error line found.
/// </summary>
public class LogAnalysisService : ILogAnalysisService
{
    // Regex patterns for timestamp extraction
    private static readonly Regex TimestampBracket =
        new(@"\[(\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2})\]", RegexOptions.Compiled);

    private static readonly Regex TimestampIso =
        new(@"(\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2})", RegexOptions.Compiled);

    // Regex for error codes
    private static readonly Regex ErrorCodePattern =
        new(@"\b(E\d{3,6}|ERR[-_]?\d+|0x[0-9A-Fa-f]{4,8}|HTTP[/ ]\d{3}|\b[45]\d{2}\b)\b", RegexOptions.Compiled);

    // Keywords that flag a line as an error/warning worth analyzing
    private static readonly string[] ErrorKeywords =
    [
        "ERROR", "FATAL", "CRITICAL", "EXCEPTION", "UNHANDLED",
        "WARN", "WARNING", "FAILED", "FAILURE", "TIMEOUT",
        "UNAUTHORIZED", "FORBIDDEN", "NULL", "NULLREFERENCE",
        "STACKOVERFLOW", "OUTOFMEMORY", "DEADLOCK", "CONNECTIONREFUSED",
        "SQLEXCEPTION", "DBNULL", "SOCKET", "NETWORK", "IOEXCEPTION",
        "ACCESSDENIED", "NOTFOUND", "BADREQUEST", "SERVERERROR"
    ];

    // -----------------------------------------------------------------------
    // Rule table: keyword → (RootCause, Solution, Severity, Category, RecommendedAction)
    // -----------------------------------------------------------------------
    private static readonly Dictionary<string, (string RootCause, string Solution, string Severity, string Category, string RecommendedAction)> Rules
        = new(StringComparer.OrdinalIgnoreCase)
        {
            ["NULLREFERENCE"]     = ("A null object reference was accessed before initialization.",
                                     "Add null checks or use null-conditional operators (?.) before accessing the object.",
                                     "High", "Application",
                                     "Review the stack trace, add defensive null checks, and redeploy."),

            ["NULL"]              = ("A null value was encountered where a valid object was expected.",
                                     "Validate inputs and ensure all required fields are initialized before use.",
                                     "Medium", "Application",
                                     "Inspect the surrounding context for uninitialized variables."),

            ["OUTOFMEMORY"]       = ("The application exceeded available heap memory.",
                                     "Increase JVM/CLR heap size, fix memory leaks, or optimize large data operations.",
                                     "Critical", "System",
                                     "Restart the service immediately and profile memory usage."),

            ["STACKOVERFLOW"]     = ("Infinite or deeply nested recursion exhausted the call stack.",
                                     "Identify the recursive method, add a proper base case or convert to iteration.",
                                     "Critical", "Application",
                                     "Restart the process and refactor the recursive logic."),

            ["TIMEOUT"]           = ("An operation exceeded its allowed time limit.",
                                     "Increase timeout settings, optimize the slow query/call, or add circuit-breaker logic.",
                                     "High", "Network",
                                     "Check downstream service latency and add retry with exponential back-off."),

            ["DEADLOCK"]          = ("Two or more threads are waiting on each other, causing a circular lock.",
                                     "Review locking order, use lock-free data structures, or add deadlock detection.",
                                     "Critical", "Database",
                                     "Kill the blocked transactions, then refactor transaction scope and lock ordering."),

            ["SQLEXCEPTION"]      = ("A database error occurred during query execution.",
                                     "Verify SQL syntax, check database connectivity, and review query parameters.",
                                     "High", "Database",
                                     "Inspect the SQL error code, fix the query, and monitor DB connection pool."),

            ["DBNULL"]            = ("A database column returned NULL unexpectedly.",
                                     "Add IS NOT NULL constraints or handle DBNull in the data-access layer.",
                                     "Medium", "Database",
                                     "Review schema defaults and add null-handling in the query result mapping."),

            ["CONNECTIONREFUSED"] = ("The target server actively refused the TCP connection.",
                                     "Verify the server is running, firewall rules allow the port, and the endpoint is correct.",
                                     "High", "Network",
                                     "Check service health, port availability, and update connection strings if needed."),

            ["SOCKET"]            = ("A socket-level network error occurred.",
                                     "Check network stability, firewall settings, and socket timeout configuration.",
                                     "High", "Network",
                                     "Retry the connection and review network infrastructure."),

            ["IOEXCEPTION"]       = ("A file system read/write operation failed.",
                                     "Verify file permissions, disk space, and that the target path exists.",
                                     "Medium", "System",
                                     "Check disk health, file locks, and application file-access permissions."),

            ["UNAUTHORIZED"]      = ("The request lacked valid authentication credentials.",
                                     "Ensure the client sends a valid token/API key and that it has not expired.",
                                     "High", "Authentication",
                                     "Rotate credentials, verify token expiry, and review auth middleware."),

            ["FORBIDDEN"]         = ("The authenticated user does not have permission for this resource.",
                                     "Review role/permission assignments and update access control policies.",
                                     "High", "Authentication",
                                     "Audit role assignments and update authorization rules as needed."),

            ["ACCESSDENIED"]      = ("Access to the resource was denied by the operating system or security layer.",
                                     "Grant the required permissions to the service account or application user.",
                                     "High", "Authentication",
                                     "Check OS-level ACLs and service account permissions."),

            ["NOTFOUND"]          = ("A requested resource (file, endpoint, or record) was not found.",
                                     "Verify the resource path, check if it was deleted, and update any stale references.",
                                     "Medium", "Application",
                                     "Review routing configuration and confirm the resource still exists."),

            ["BADREQUEST"]        = ("The server received a malformed or invalid request.",
                                     "Validate all request parameters and payloads before sending.",
                                     "Medium", "Application",
                                     "Review API contract, add input validation, and check client request construction."),

            ["SERVERERROR"]       = ("The server encountered an unexpected internal error.",
                                     "Inspect server logs for the root exception and fix the underlying issue.",
                                     "High", "Application",
                                     "Review server error logs, hotfix the bug, and add alerting."),

            ["FATAL"]             = ("A fatal error caused the application to terminate unexpectedly.",
                                     "Investigate the crash dump or log context, fix the root cause, and add health monitoring.",
                                     "Critical", "Application",
                                     "Immediately restart the service, capture diagnostics, and escalate to engineering."),

            ["EXCEPTION"]         = ("An unhandled exception was thrown during application execution.",
                                     "Add proper try-catch handling and log the full stack trace for diagnosis.",
                                     "High", "Application",
                                     "Review the stack trace and add exception handling around the failing code path."),

            ["WARN"]              = ("A non-critical warning condition was detected.",
                                     "Investigate the warning context; it may indicate a future failure.",
                                     "Low", "Application",
                                     "Monitor the warning frequency; act if it becomes persistent."),

            ["WARNING"]           = ("A non-critical warning condition was detected.",
                                     "Investigate the warning context; it may indicate a future failure.",
                                     "Low", "Application",
                                     "Monitor the warning frequency; act if it becomes persistent."),

            ["FAILED"]            = ("An operation or process failed to complete successfully.",
                                     "Check the specific operation context, review dependencies, and retry.",
                                     "High", "Application",
                                     "Identify the failed step, fix the root cause, and add retry logic."),

            ["NETWORK"]           = ("A network-level error occurred.",
                                     "Verify network connectivity, DNS resolution, and remote endpoint availability.",
                                     "High", "Network",
                                     "Run connectivity diagnostics and review network configuration."),

            ["ERROR"]             = ("A general application error occurred.",
                                     "Review the error message and stack trace for specific failure details.",
                                     "Medium", "Application",
                                     "Investigate the error context and apply appropriate fix."),
        };

    public Task<LogAnalysisResponse> AnalyzeAsync(string fileName, IEnumerable<string> lines)
    {
        var lineList = lines.ToList();
        var errors = new List<LogErrorEntry>();

        foreach (var line in lineList)
        {
            var upperLine = line.ToUpperInvariant();
            var matchedKeyword = ErrorKeywords.FirstOrDefault(k => upperLine.Contains(k));
            if (matchedKeyword is null) continue;

            // Find the best (most specific) rule key that matches the line
            var ruleKey = Rules.Keys
                .Where(k => upperLine.Contains(k.ToUpperInvariant()))
                .OrderByDescending(k => k.Length)
                .FirstOrDefault() ?? matchedKeyword;

            var (rootCause, solution, severity, category, recommendedAction) =
                Rules.TryGetValue(ruleKey, out var rule)
                    ? rule
                    : ("An error was detected in the log.", "Review the log line for details.", "Medium", "Application", "Investigate the log context.");

            errors.Add(new LogErrorEntry
            {
                FileName          = fileName,
                Timestamp         = ExtractTimestamp(line),
                ErrorLine         = line.Length > 500 ? line[..500] : line,
                ErrorCode         = ExtractErrorCode(line),
                RootCause         = rootCause,
                Solution          = solution,
                Severity          = severity,
                Category          = category,
                RecommendedAction = recommendedAction
            });
        }

        var response = new LogAnalysisResponse
        {
            FileName    = fileName,
            AnalyzedAt  = DateTime.UtcNow,
            TotalLines  = lineList.Count,
            TotalErrors = errors.Count,
            SeveritySummary = errors
                .GroupBy(e => e.Severity)
                .ToDictionary(g => g.Key, g => g.Count()),
            CategorySummary = errors
                .GroupBy(e => e.Category)
                .ToDictionary(g => g.Key, g => g.Count()),
            Errors = errors
        };

        return Task.FromResult(response);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string ExtractTimestamp(string line)
    {
        var m = TimestampBracket.Match(line);
        if (m.Success) return m.Groups[1].Value;
        m = TimestampIso.Match(line);
        return m.Success ? m.Groups[1].Value : string.Empty;
    }

    private static string ExtractErrorCode(string line)
    {
        var m = ErrorCodePattern.Match(line);
        return m.Success ? m.Value : "UNKNOWN";
    }
}
