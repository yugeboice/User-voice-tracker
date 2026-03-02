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

    /// <summary>
    /// Web search with time filter (recency in days).
    /// </summary>
    public async Task<List<SearchResultItem>> SearchWithRecencyAsync(string query, int topN = 10, int recencyDays = 7)
    {
        var request = new SearchRequest
        {
            Requests = new List<SearchRequestItem>
            {
                new SearchRequestItem
                {
                    Q = query,
                    TopN = topN,
                    Source = SearchProviders.WebWithBing,
                    Recency = recencyDays
                }
            }
        };

        var response = await _proxy.SearchAsync(request);
        return response?.Results?.Take(topN).ToList() ?? new List<SearchResultItem>();
    }

    /// <summary>
    /// Batch search multiple queries at once.
    /// Returns a dictionary mapping each query to its results.
    /// </summary>
    public async Task<Dictionary<string, List<SearchResultItem>>> BatchSearchAsync(
        List<string> queries, 
        int topN = 5, 
        int recencyDays = 7)
    {
        var results = new Dictionary<string, List<SearchResultItem>>();
        
        // Execute searches in parallel for better performance
        var tasks = queries.Select(async query =>
        {
            var searchResults = await SearchWithRecencyAsync(query, topN, recencyDays);
            return new { Query = query, Results = searchResults };
        });

        var completedTasks = await Task.WhenAll(tasks);
        
        foreach (var task in completedTasks)
        {
            results[task.Query] = task.Results;
        }

        return results;
    }
}

/// <summary>
/// Simple HttpClientFactory implementation for Lumina SDK.
/// </summary>
public class DefaultHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new HttpClient();
}
