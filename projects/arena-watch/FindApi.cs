using Microsoft.Lumina.Client.ApiProxy;
using Microsoft.Lumina.Client.Models.Sonicberry;

namespace MinimalApiCall;

/// <summary>
/// Minimal Find API implementation.
/// Endpoint: POST /api/sonicberry/find
/// The Find API searches within an opened webpage session using pattern matching.
/// Accepts a shared LuminaServiceApiProxy for centralized proxy management.
/// </summary>
public class FindApi
{
    private readonly LuminaServiceApiProxy _proxy;

    /// <summary>
    /// Create FindApi with a shared proxy (recommended for centralized management).
    /// </summary>
    public FindApi(LuminaServiceApiProxy proxy)
    {
        _proxy = proxy;
    }

    /// <summary>
    /// Create FindApi standalone (for independent use).
    /// </summary>
    public FindApi(string endpoint, Func<Task<string>> tokenProvider)
    {
        var options = new LuminaApiOptions
        {
            Endpoint = endpoint,
            LuminaApiTokenProvider = async () => await tokenProvider()
        };
        _proxy = new LuminaServiceApiProxy(options, new DefaultHttpClientFactory());
    }

    /// <summary>
    /// Find specific content within a previously opened webpage using pattern matching.
    /// Use with Open API: first open a URL, then use the sessionId to search within the page.
    /// </summary>
    public async Task<FindResponse?> FindInPageAsync(string sessionId, string pattern)
    {
        var request = new FindRequest
        {
            Requests = new List<FindRequestItem>
            {
                new FindRequestItem { Pattern = pattern }
            },
            ToolState = new ToolState { SessionId = sessionId }
        };

        return await _proxy.FindAsync(request);
    }
}
