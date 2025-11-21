using System.Collections.Generic;
using System.Text;

namespace LuminaSearchConsole.Services
{
    /// <summary>
    /// Service for collecting and managing API call logs.
    /// Helps partners understand API usage patterns by showing request/response details.
    /// </summary>
    public class ApiLogService
    {
        private readonly List<ApiLogEntry> _logs = new List<ApiLogEntry>();
        private readonly object _lockObject = new object();

        /// <summary>
        /// Add a new log entry for an API call
        /// </summary>
        /// <param name="apiName">API name (e.g., "Lumina Search API")</param>
        /// <param name="operation">Operation name (e.g., "POST /api/sonicberry/search")</param>
        /// <param name="details">Detailed information (Parameters/Result)</param>
        /// <param name="success">Whether the operation succeeded</param>
        /// <param name="feature">User feature that triggered this API call (e.g., "Company Overview", "Search Results")</param>
        public void AddLog(string apiName, string operation, string details, bool success = true, string? feature = null)
        {
            lock (_lockObject)
            {
                _logs.Add(new ApiLogEntry
                {
                    Timestamp = DateTime.Now,
                    ApiName = apiName,
                    Operation = operation,
                    Details = details,
                    Success = success,
                    Feature = feature
                });

                // Keep only last 50 logs to avoid memory issues
                if (_logs.Count > 50)
                {
                    _logs.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// Get all logs as a formatted list
        /// </summary>
        public List<ApiLogEntry> GetLogs()
        {
            lock (_lockObject)
            {
                return new List<ApiLogEntry>(_logs);
            }
        }

        /// <summary>
        /// Clear all logs
        /// </summary>
        public void ClearLogs()
        {
            lock (_lockObject)
            {
                _logs.Clear();
            }
        }

        /// <summary>
        /// Get logs formatted as HTML for display
        /// </summary>
        public string GetLogsAsHtml()
        {
            lock (_lockObject)
            {
                if (_logs.Count == 0)
                {
                    return "<p class='text-muted'>No API calls logged yet. Perform authentication or search to see logs.</p>";
                }

                var sb = new StringBuilder();
                sb.AppendLine("<div class='api-logs'>");

                foreach (var log in _logs)
                {
                    var statusClass = log.Success ? "success" : "danger";
                    var statusIcon = log.Success ? "✅" : "❌";
                    
                    // Add feature badge if available
                    var featureBadge = !string.IsNullOrEmpty(log.Feature) 
                        ? $"<span class='log-feature badge bg-primary'>{log.Feature}</span>" 
                        : "";

                    sb.AppendLine($@"
                    <div class='log-entry log-{statusClass}'>
                        <div class='log-header'>
                            <span class='log-icon'>{statusIcon}</span>
                            <span class='log-time'>{log.Timestamp:HH:mm:ss.fff}</span>
                            {featureBadge}
                            <span class='log-api badge bg-{statusClass}'>{log.ApiName}</span>
                            <span class='log-operation'>{log.Operation}</span>
                        </div>
                        <div class='log-details'>{System.Web.HttpUtility.HtmlEncode(log.Details)}</div>
                    </div>");
                }

                sb.AppendLine("</div>");
                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// Represents a single API call log entry
    /// </summary>
    public class ApiLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string ApiName { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? Feature { get; set; }  // User feature that triggered this API call
    }
}
