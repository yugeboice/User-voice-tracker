namespace LuminaSearchConsole.Services
{
    // ========================================
    // Internal Helper Classes for Services
    // ========================================
    // These are internal classes used within service layer only.
    // View models for UI are in Models/ViewModels.cs
    // ========================================

    #region Service Layer Internal Models

    /// <summary>
    /// Internal result model from Open/Click API - used within service layer.
    /// For UI responses, use OpenContentResponse in Models/ViewModels.cs
    /// </summary>
    public class OpenContentInternal
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
