namespace LuminaSearchConsole.Services
{
    // ========================================
    // Shared Model Classes for Demo Application
    // ========================================
    // These are NOT official Lumina API models.
    // They are custom classes created for demonstration purposes only.
    // ========================================

    #region Company Information Models

    /// <summary>
    /// Company information extracted from Wikipedia
    /// </summary>
    public class CompanyInfo
    {
        public string Url { get; set; } = string.Empty;
        public List<InfoField> Fields { get; set; } = new();
    }

    /// <summary>
    /// Individual information field extracted from company page
    /// </summary>
    public class InfoField
    {
        public long LineNumber { get; set; }
        public string Content { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
    }

    #endregion

    #region Content Extraction Models

    /// <summary>
    /// Result from Open API or Click API containing page content and navigation context
    /// </summary>
    public class OpenContentResult
    {
        public string Content { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public List<dynamic> Links { get; set; } = new();
        public dynamic? PageContext { get; set; }
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    /// <summary>
    /// Simplified link information for UI display
    /// </summary>
    public class PageLink
    {
        public int LinkId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    #endregion

    #region Helper Classes

    /// <summary>
    /// Simple HTTP client factory for creating HttpClient instances.
    /// Required by LuminaServiceApiProxy for making API requests.
    /// </summary>
    public class DefaultHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }

    #endregion
}
