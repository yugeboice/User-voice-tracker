using Microsoft.Lumina.Client.ApiProxy;
using Microsoft.Lumina.Client.Models.Sonicberry;
using static Microsoft.Lumina.Common.Constants.ConstantStrings;
using LuminaSearchConsole.Models;
// Models like DefaultHttpClientFactory are in SharedModels.cs (same namespace)

namespace LuminaSearchConsole.Services
{
    /// <summary>
    /// Search API Demo - Shows how to use POST /api/sonicberry/search
    /// 
    /// WHAT THIS API DOES:
    /// Web search with Bing provider, supports batch queries for better performance.
    /// Can filter by domain (e.g., Wikipedia only) and recency (recent results).
    /// 
    /// HOW TO TEST IN THIS DEMO:
    /// - Home page: Enter search query and click "Search" button
    /// - Batch Search: Enter company name and click "Batch Search" (searches stock prices + news)
    /// - Check "API Calls Log" section to see request/response details
    /// 
    /// CODE EXAMPLE:
    /// <code>
    /// var service = new LuminaSearchService(accessToken, luminaConfig);
    /// var results = await service.ExecuteWebSearchAsync("Microsoft", domains: new[] { "wikipedia.org" });
    /// </code>
    /// 
    /// API Endpoint configured in appsettings.json (LuminaConfiguration:ApiEndpoint)
    /// </summary>
    public class LuminaSearchService
    {
        #region Configuration
        
        private readonly string _luminaEndpoint;
        private readonly LuminaServiceApiProxy _proxy;
        
        #endregion

        #region Constructor

        public LuminaSearchService(string accessToken, LuminaConfiguration luminaConfig)
        {
            _luminaEndpoint = luminaConfig.ApiEndpoint;
            
            // Configure Lumina API proxy with authentication
            var options = new LuminaApiOptions
            {
                Endpoint = _luminaEndpoint,
                LuminaApiTokenProvider = async () => await Task.FromResult(accessToken)
            };

            var httpClientFactory = new DefaultHttpClientFactory();
            _proxy = new LuminaServiceApiProxy(options, httpClientFactory);
        }
        
        #endregion

        #region Search Methods

        /// <summary>
        /// Execute batch search for company analysis
        /// 
        /// Batch Search Pattern:
        /// Single API call with multiple search queries reduces latency.
        /// Use case: Get both stock price and news for a company.
        /// 
        /// Example:
        /// Input: "Microsoft", topN=5
        /// Output: 5 stock-related results + 5 news results
        /// 
        /// API Request:
        /// POST /api/sonicberry/search
        /// {
        ///   "Requests": [
        ///     { "Q": "Microsoft stock price", "TopN": 5, "Source": "WebWithBing", "Recency": 7 },
        ///     { "Q": "Microsoft latest news", "TopN": 5, "Source": "WebWithBing", "Recency": 7 }
        ///   ]
        /// }
        /// </summary>
        /// <param name="companyName">Company name to search for</param>
        /// <param name="topN">Maximum results per query (default: 5)</param>
        /// <param name="apiLogService">Optional logging service for tracking API calls</param>
        /// <param name="feature">Feature name for log categorization (e.g., "Stock Search")</param>
        /// <returns>Structured results with stock and news separated</returns>
        public async Task<BatchSearchResult> ExecuteBatchCompanySearchAsync(
            string companyName, 
            int topN = 5, 
            ApiLogService? apiLogService = null, 
            string? feature = null)
        {
            if (string.IsNullOrWhiteSpace(companyName))
            {
                throw new ArgumentException("Company name cannot be empty", nameof(companyName));
            }

            // Log request parameters (optional, for debugging/learning)
            apiLogService?.AddLog("Lumina Search API", "POST /api/sonicberry/search", 
                $"Parameters\n" +
                $"{{\n" +
                $"  \"requests\": [\n" +
                $"    {{ \"q\": \"{companyName} stock price\", \"topN\": {topN}, \"source\": \"WebWithBing\", \"recency\": 7 }},\n" +
                $"    {{ \"q\": \"{companyName} latest news\", \"topN\": {topN}, \"source\": \"WebWithBing\", \"recency\": 7 }}\n" +
                $"  ]\n" +
                $"}}", true, feature);

            // Build search request with 2 queries
            var searchRequest = new SearchRequest
            {
                Requests = new List<SearchRequestItem>
                {
                    // Query 1: Stock price information
                    new SearchRequestItem
                    {
                        Q = $"{companyName} stock price",
                        TopN = topN,
                        Source = SearchProviders.WebWithBing,
                        Language = "en",
                        Market = "en-US",
                        CountryCode = "us",
                        Recency = 7, // Get results from last 7 days
                        AdditionalConfig = new SearchRequestAdditionalConfig
                        {
                            MaxSemanticDocumentLength = 200
                        }
                    },
                    // Query 2: Latest news
                    new SearchRequestItem
                    {
                        Q = $"{companyName} latest news",
                        TopN = topN,
                        Source = SearchProviders.WebWithBing,
                        Language = "en",
                        Market = "en-US",
                        CountryCode = "us",
                        Recency = 7, // Get results from last 7 days
                        AdditionalConfig = new SearchRequestAdditionalConfig
                        {
                            MaxSemanticDocumentLength = 200
                        }
                    }
                }
            };

            try
            {
                var startTime = DateTime.Now;
                
                // Call Lumina Search API
                var searchResult = await _proxy.SearchAsync(searchRequest);
                
                var duration = (DateTime.Now - startTime).TotalMilliseconds;
                
                // Parse and separate results
                // Results are returned in order: first query results, then second query results
                var stockResults = new List<Models.SearchResult>();
                var newsResults = new List<Models.SearchResult>();
                
                if (searchResult?.Results != null && searchResult.Results.Count > 0)
                {
                    // Split results: first half = stock, second half = news
                    int midPoint = searchResult.Results.Count / 2;
                    
                    // Process stock price results (first request)
                    for (int i = 0; i < Math.Min(midPoint, topN); i++)
                    {
                        if (i < searchResult.Results.Count)
                        {
                            var result = searchResult.Results[i];
                            stockResults.Add(new Models.SearchResult
                            {
                                Title = result.Title ?? "No Title",
                                Url = result.Url ?? "",
                                Summary = result.SemanticDocument ?? "",
                                SemanticDocument = result.SemanticDocument ?? ""
                            });
                        }
                    }
                    
                    // Process news results (second request)
                    for (int i = midPoint; i < searchResult.Results.Count && i < midPoint + topN; i++)
                    {
                        var result = searchResult.Results[i];
                        newsResults.Add(new Models.SearchResult
                        {
                            Title = result.Title ?? "No Title",
                            Url = result.Url ?? "",
                            Summary = result.SemanticDocument ?? "",
                            SemanticDocument = result.SemanticDocument ?? ""
                        });
                    }
                }

                // Log response (optional, for debugging/learning)
                string stockPreview = "(no results)";
                if (stockResults.Count > 0)
                {
                    var title = stockResults[0].Title;
                    if (!string.IsNullOrEmpty(title))
                    {
                        stockPreview = title.Substring(0, Math.Min(50, title.Length)) + "...";
                    }
                }
                
                string newsPreview = "(no results)";
                if (newsResults.Count > 0)
                {
                    var title = newsResults[0].Title;
                    if (!string.IsNullOrEmpty(title))
                    {
                        newsPreview = title.Substring(0, Math.Min(50, title.Length)) + "...";
                    }
                }
                
                apiLogService?.AddLog("Lumina Search API", "POST /api/sonicberry/search", 
                    $"Result\n" +
                    $"{{\n" +
                    $"  \"results_count\": {stockResults.Count + newsResults.Count},\n" +
                    $"  \"stock_results\": {stockResults.Count},\n" +
                    $"  \"news_results\": {newsResults.Count},\n" +
                    $"  \"stock_preview\": \"{stockPreview}\",\n" +
                    $"  \"news_preview\": \"{newsPreview}\",\n" +
                    $"  \"response_time_ms\": {duration:F0}\n" +
                    $"}}", true, feature);

                return new BatchSearchResult
                {
                    CompanyName = companyName,
                    StockResults = stockResults,
                    NewsResults = newsResults
                };
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Network error occurred while searching for '{companyName}'. Please check your internet connection.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Batch search failed for '{companyName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Execute simple web search and return structured results
        /// 
        /// This is the basic search method for general-purpose web search.
        /// 
        /// Example:
        /// Input: "Azure cloud services", topN=10
        /// Output: 10 search results with titles, URLs, and summaries
        /// 
        /// API Request:
        /// POST /api/sonicberry/search
        /// {
        ///   "Requests": [
        ///     { "Q": "Azure cloud services", "TopN": 10, "Source": "WebWithBing" }
        ///   ]
        /// }
        /// </summary>
        /// <param name="query">Search query text</param>
        /// <param name="topN">Maximum number of results (1-50, default: 5)</param>
        /// <param name="domains">Optional: Restrict results to specific domains (e.g., ["wikipedia.org"])</param>
        /// <param name="apiLogService">Optional logging service for tracking API calls</param>
        /// <param name="feature">Feature name for log categorization</param>
        /// <returns>List of search results with title, URL, and summary</returns>
        public async Task<List<Models.SearchResult>> ExecuteWebSearchAsync(
            string query, 
            int topN = 5, 
            string[]? domains = null, 
            ApiLogService? apiLogService = null, 
            string? feature = null)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new ArgumentException("Search query cannot be empty", nameof(query));
            }

            if (topN <= 0 || topN > 50)
            {
                throw new ArgumentOutOfRangeException(nameof(topN), "TopN must be between 1 and 50");
            }

            // Log request parameters (optional)
            apiLogService?.AddLog("Lumina Search API", "POST /api/sonicberry/search", 
                $"Parameters\n" +
                $"{{\n" +
                $"  \"q\": \"{query}\",\n" +
                $"  \"topN\": {topN},\n" +
                $"  \"source\": \"WebWithBing\",\n" +
                $"  \"market\": \"en-US\"\n" +
                $"}}", true, feature);

            // Build search request
            var searchRequest = new SearchRequest
            {
                Requests = new List<SearchRequestItem>
                {
                    new SearchRequestItem
                    {
                        Q = query,
                        TopN = topN,
                        Source = SearchProviders.WebWithBing,
                        Language = "en",
                        Market = "en-US",
                        CountryCode = "us",
                        Domains = domains?.ToList()!, // Optional domain filter
                        AdditionalConfig = new SearchRequestAdditionalConfig
                        {
                            MaxSemanticDocumentLength = 200
                        }
                    }
                }
            };

            try
            {
                var startTime = DateTime.Now;
                
                // Call Lumina Search API
                var searchResult = await _proxy.SearchAsync(searchRequest);
                
                var duration = (DateTime.Now - startTime).TotalMilliseconds;
                                
                // Convert API results to view models
                var results = new List<Models.SearchResult>();

                if (searchResult?.Results != null && searchResult.Results.Count > 0)
                {
                    foreach (var result in searchResult.Results)
                    {
                        results.Add(new Models.SearchResult
                        {
                            Title = result.Title ?? "No Title",
                            Url = result.Url ?? "",
                            Summary = result.SemanticDocument ?? "",
                            SemanticDocument = result.SemanticDocument ?? ""
                        });
                    }
                }

                // Log response (optional)
                string preview = "(no results)";
                if (results.Count > 0)
                {
                    var title = results[0].Title;
                    if (!string.IsNullOrEmpty(title))
                    {
                        preview = title.Substring(0, Math.Min(50, title.Length)) + "...";
                    }
                }
                
                apiLogService?.AddLog("Lumina Search API", "POST /api/sonicberry/search", 
                    $"Result\n" +
                    $"{{\n" +
                    $"  \"results_count\": {results.Count},\n" +
                    $"  \"first_result\": \"{preview}\",\n" +
                    $"  \"response_time_ms\": {duration:F0}\n" +
                    $"}}", true, feature);

                return results;
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Network error occurred while searching for '{query}'. Please check your internet connection.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Search failed for query '{query}': {ex.Message}", ex);
            }
        }

        #endregion
    }
}
