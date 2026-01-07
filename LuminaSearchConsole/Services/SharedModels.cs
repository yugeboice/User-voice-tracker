namespace LuminaSearchConsole.Services
{
    // ========================================
    // Internal Helper Classes for Services
    // ========================================
    // These are internal classes used within service layer only.
    // View models for UI are in Models/ViewModels.cs
    // ========================================

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
}
