using Microsoft.Lumina.Client.ApiProxy;
using Microsoft.Lumina.Client.Models.Sonicberry;
using static Microsoft.Lumina.Common.Constants.ConstantStrings;

namespace MinimalApiCall;

/// <summary>
/// Minimal Search API implementation.
/// Accepts a shared LuminaServiceApiProxy for centralized proxy management.
/// </summary>
public class SearchApi
{
    private readonly LuminaServiceApiProxy _proxy;

    /// <summary>
    /// Create SearchApi with a shared proxy (recommended for centralized management).
    /// </summary>
    public SearchApi(LuminaServiceApiProxy proxy)
    {
        _proxy = proxy;
    }

    /// <summary>
    /// Create SearchApi standalone (for independent use).
    /// </summary>
    public SearchApi(string endpoint, Func<Task<string>> tokenProvider)
    {
        var options = new LuminaApiOptions
        {
            Endpoint = endpoint,
            LuminaApiTokenProvider = async () => await tokenProvider()
        };
        _proxy = new LuminaServiceApiProxy(options, new DefaultHttpClientFactory());
    }

    /// <summary>
    /// Simple web search.
    /// </summary>
    public async Task<List<SearchResultItem>> SearchAsync(string query, int topN = 10)
    {
        var request = new SearchRequest
        {
            Requests = new List<SearchRequestItem>
            {
                new SearchRequestItem
                {
                    Q = query,
                    TopN = topN,
                    Source = SearchProviders.WebWithBing
                }
            }
        };

        var response = await _proxy.SearchAsync(request);
        return response?.Results?.Take(topN).ToList() ?? new List<SearchResultItem>();
    }
}

/// <summary>
/// Simple HttpClientFactory implementation for Lumina SDK.
/// </summary>
public class DefaultHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new HttpClient();
}
