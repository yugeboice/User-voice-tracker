using Microsoft.Lumina.Client.ApiProxy;
using Microsoft.Lumina.Client.Models.Sonicberry;

namespace MinimalApiCall;

/// <summary>
/// Minimal Open API implementation.
/// Accepts a shared LuminaServiceApiProxy for centralized proxy management.
/// </summary>
public class OpenApi
{
    private readonly LuminaServiceApiProxy _proxy;

    /// <summary>
    /// Create OpenApi with a shared proxy (recommended for centralized management).
    /// </summary>
    public OpenApi(LuminaServiceApiProxy proxy)
    {
        _proxy = proxy;
    }

    /// <summary>
    /// Create OpenApi standalone (for independent use).
    /// </summary>
    public OpenApi(string endpoint, Func<Task<string>> tokenProvider)
    {
        var options = new LuminaApiOptions
        {
            Endpoint = endpoint,
            LuminaApiTokenProvider = async () => await tokenProvider()
        };
        _proxy = new LuminaServiceApiProxy(options, new DefaultHttpClientFactory());
    }

    /// <summary>
    /// Result from Open operation.
    /// </summary>
    public class OpenResult
    {
        public string SessionId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public string Url { get; set; } = "";
    }

    /// <summary>
    /// Open a URL to get its content.
    /// </summary>
    public async Task<OpenResult?> OpenUrlAsync(string url)
    {
        var request = new OpenRequest
        {
            Requests = new List<OpenRequestItem>
            {
                new OpenRequestItem { RefId = url }
            }
        };

        var response = await _proxy.OpenAsync(request);

        if (response?.Pages != null && response.Pages.Count > 0)
        {
            var page = response.Pages[0];
            return new OpenResult
            {
                SessionId = response.ToolState?.SessionId ?? "",
                Title = page.Title ?? "",
                Content = page.Content ?? "",
                Url = page.Url ?? url
            };
        }
        return null;
    }
}
