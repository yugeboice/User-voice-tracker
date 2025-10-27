using Microsoft.Lumina.Client.ApiProxy;
using Microsoft.Lumina.Client.Models.Sonicberry;
using Newtonsoft.Json;
using static Microsoft.Lumina.Common.Constants.ConstantStrings;
using LuminaSearchConsole.Models;

namespace LuminaSearchConsole
{
    /// <summary>
    /// Lumina Search API Service
    /// 
    /// Demonstrates integration with Lumina Search and Open APIs:
    /// - Web search using Bing provider
    /// - Batch search (multiple queries in one request)
    /// - Content extraction from URLs (Open API)
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

        #region Public Search Methods

        #endregion

        #region Public Search Methods

        /// <summary>
        /// Execute basic web search (Demo example)
        /// </summary>
        public async Task ExecuteBasicSearchAsync()
        {
            Console.WriteLine("Creating search request...");

            var searchRequest = new SearchRequest
            {
                Requests = new List<SearchRequestItem>
                {
                    new SearchRequestItem
                    {
                        Q = "Microsoft Azure Functions",
                        TopN = 3,
                        Source = SearchProviders.WebWithBing,
                        Language = "en",
                        Market = "en-US",
                        CountryCode = "us",
                        AdditionalConfig = new SearchRequestAdditionalConfig
                        {
                            MaxSemanticDocumentLength = 50
                        }
                    }
                }
            };

            await ExecuteSearchRequestAsync(searchRequest, "Basic Search");
        }

        /// <summary>
        /// Execute custom search
        /// </summary>
        public async Task ExecuteCustomSearchAsync(string query, int topN = 5, string language = "en", string market = "en-US")
        {
            Console.WriteLine($"Creating custom search request: {query}");

            var searchRequest = new SearchRequest
            {
                Requests = new List<SearchRequestItem>
                {
                    new SearchRequestItem
                    {
                        Q = query,
                        TopN = topN,
                        Source = SearchProviders.WebWithBing,
                        Language = language,
                        Market = market,
                        CountryCode = "us",
                        AdditionalConfig = new SearchRequestAdditionalConfig
                        {
                            MaxSemanticDocumentLength = 100
                        }
                    }
                }
            };

            await ExecuteSearchRequestAsync(searchRequest, "Custom Search");
        }

        /// <summary>
        /// Generic method to execute search requests
        /// </summary>
        private async Task ExecuteSearchRequestAsync(SearchRequest searchRequest, string searchType)
        {
            try
            {
                var request = searchRequest.Requests[0];
                Console.WriteLine($"Search Parameters ({searchType}):");
                Console.WriteLine($"  Query: {request.Q}");
                Console.WriteLine($"  Number of Results: {request.TopN}");
                Console.WriteLine($"  Language: {request.Language}");
                Console.WriteLine($"  Market: {request.Market}");
                Console.WriteLine($"  Search Source: {request.Source}");
                Console.WriteLine();

                Console.WriteLine("🔍 Executing search...");

                var searchResult = await _proxy.SearchAsync(searchRequest);

                Console.WriteLine($"✅ {searchType} completed!");
                Console.WriteLine();

                DisplaySearchResults(searchResult);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ {searchType} failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Execute batch search for company analysis
        /// 
        /// Batch Search Pattern:
        /// Single API call with multiple search queries reduces latency.
        /// Use case: Get both stock price and news for a company.
        /// </summary>
        /// <param name="companyName">Company name to search for</param>
        /// <param name="topN">Maximum results per query</param>
        /// <returns>Structured results with stock and news separated</returns>
        public async Task<BatchSearchResult> ExecuteBatchCompanySearchAsync(string companyName, int topN = 5)
        {
            if (string.IsNullOrWhiteSpace(companyName))
            {
                throw new ArgumentException("Company name cannot be empty", nameof(companyName));
            }

            var searchRequest = new SearchRequest
            {
                Requests = new List<SearchRequestItem>
                {
                    // First search: Company + stock price
                    new SearchRequestItem
                    {
                        Q = $"{companyName} stock price",
                        TopN = topN,
                        Source = SearchProviders.WebWithBing,
                        Language = "en",
                        Market = "en-US",
                        CountryCode = "us",
                        Recency = 7, // Recent stock price info
                        AdditionalConfig = new SearchRequestAdditionalConfig
                        {
                            MaxSemanticDocumentLength = 200
                        }
                    },
                    // Second search: Company + latest news
                    new SearchRequestItem
                    {
                        Q = $"{companyName} latest news",
                        TopN = topN,
                        Source = SearchProviders.WebWithBing,
                        Language = "en",
                        Market = "en-US",
                        CountryCode = "us",
                        Recency = 7, // Recent news
                        AdditionalConfig = new SearchRequestAdditionalConfig
                        {
                            MaxSemanticDocumentLength = 200
                        }
                    }
                }
            };

            try
            {
                Console.WriteLine($"Executing batch search for company: {companyName}");
                var searchResult = await _proxy.SearchAsync(searchRequest);
                Console.WriteLine($"✅ Batch search completed successfully");
                
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

                return new BatchSearchResult
                {
                    CompanyName = companyName,
                    StockResults = stockResults,
                    NewsResults = newsResults
                };
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"❌ Network error during batch search: {ex.Message}");
                throw new Exception($"Network error occurred while searching for '{companyName}'. Please check your internet connection.", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Batch search failed: {ex.Message}");
                throw new Exception($"Batch search failed for '{companyName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Execute web search and return structured results
        /// 
        /// This is the main search method used by the web interface.
        /// Returns clean, structured data ready for display.
        /// </summary>
        /// <param name="query">Search query text</param>
        /// <param name="topN">Maximum number of results (1-50)</param>
        /// <returns>List of search results with title, URL, and summary</returns>
        public async Task<List<Models.SearchResult>> ExecuteWebSearchAsync(string query, int topN = 5)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new ArgumentException("Search query cannot be empty", nameof(query));
            }

            if (topN <= 0 || topN > 50)
            {
                throw new ArgumentOutOfRangeException(nameof(topN), "TopN must be between 1 and 50");
            }

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
                        AdditionalConfig = new SearchRequestAdditionalConfig
                        {
                            MaxSemanticDocumentLength = 200
                        }
                    }
                }
            };

            try
            {
                Console.WriteLine($"Executing web search for query: {query}");
                var searchResult = await _proxy.SearchAsync(searchRequest);
                Console.WriteLine($"✅ Web search completed, found {searchResult?.Results?.Count ?? 0} results");
                
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

                return results;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"❌ Network error during web search: {ex.Message}");
                throw new Exception($"Network error occurred while searching for '{query}'. Please check your internet connection.", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Web search failed: {ex.Message}");
                throw new Exception($"Search failed for query '{query}': {ex.Message}", ex);
            }
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Display search results to console for debugging purposes.
        /// Shows result count, title/URL pairs, and full JSON response.
        /// </summary>
        /// <param name="searchResult">Dynamic search result object from Lumina API</param>
        private void DisplaySearchResults(dynamic searchResult)
        {
            Console.WriteLine("📊 Search Results:");
            Console.WriteLine("=================");

            if (searchResult?.Results != null && searchResult.Results.Count > 0)
            {
                Console.WriteLine($"Found {searchResult.Results.Count} search results:");
                for (int i = 0; i < searchResult.Results.Count; i++)
                {
                    var result = searchResult.Results[i];
                    Console.WriteLine($"{i + 1}. Title: {result.Title ?? "No Title"}");
                    Console.WriteLine($"   URL: {result.Url ?? "No URL"}");
                    Console.WriteLine();
                }
            }
            else
            {
                Console.WriteLine("No search results found");
            }

            // Display full JSON response (for debugging)
            Console.WriteLine("🔧 Full Response (JSON):");
            Console.WriteLine("========================");
            var jsonResponse = JsonConvert.SerializeObject(searchResult, Formatting.Indented);
            Console.WriteLine(jsonResponse);
        }

        #endregion

        #region Content Extraction (Open API)

        /// <summary>
        /// Open and retrieve full content from a URL using Lumina Open API
        /// 
        /// Open API Use Case:
        /// When search returns a result URL, use Open API to extract the full page content.
        /// This is useful for content analysis, summarization, or detailed viewing.
        /// </summary>
        /// <param name="url">Full URL to open and extract content from</param>
        /// <returns>Extracted page content as text</returns>
        public async Task<string> OpenContentAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("URL cannot be empty", nameof(url));
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                throw new ArgumentException($"Invalid URL format: {url}", nameof(url));
            }

            try
            {
                Console.WriteLine($"📄 Opening content from URL: {url}");

                // Create Open API request with URL reference
                var openRequest = new OpenRequest
                {
                    Requests = new List<OpenRequestItem>
                    {
                        new OpenRequestItem
                        {
                            RefId = url  // Direct URL reference
                        }
                    }
                };

                var response = await _proxy.OpenAsync(openRequest);
                
                if (response != null && response.Pages != null && response.Pages.Count > 0)
                {
                    var page = response.Pages[0];
                    string content = page.Content ?? "";
                    
                    // Validate content quality
                    var isContentFiltered = content.Contains("filtered content") || 
                                          content.Contains("Failed to open") ||
                                          string.IsNullOrWhiteSpace(content);
                    
                    if (isContentFiltered)
                    {
                        Console.WriteLine($"⚠️ No content retrieved from URL: {url}");
                        throw new Exception($"No content available from the URL: {url}");
                    }
                    
                    Console.WriteLine($"✅ Successfully retrieved content. Length: {content.Length} characters");
                    return content;
                }
                else
                {
                    Console.WriteLine($"⚠️ No content retrieved from URL: {url}");
                    throw new Exception($"No content available from the URL: {url}");
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"❌ Network error while opening content: {ex.Message}");
                throw new Exception($"Network error occurred while accessing '{url}'. Please check your internet connection.", ex);
            }
            catch (ArgumentException)
            {
                throw; // Re-throw validation errors as-is
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error opening content: {ex.Message}");
                throw new Exception($"Failed to open content from '{url}': {ex.Message}", ex);
            }
        }

        #endregion
    }

    #region Helper Classes

    /// <summary>
    /// Simple HTTP client factory for creating HttpClient instances.
    /// Used by LuminaServiceApiProxy for making API requests.
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