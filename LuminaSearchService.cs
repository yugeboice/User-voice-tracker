using Microsoft.Lumina.Client.ApiProxy;
using Microsoft.Lumina.Client.Models.Sonicberry;
using Newtonsoft.Json;
using static Microsoft.Lumina.Common.Constants.ConstantStrings;
using LuminaSearchConsole.Models;

namespace LuminaSearchConsole
{
    /// <summary>
    /// Lumina Search Service - Implements various search functionalities
    /// </summary>
    public class LuminaSearchService
    {
        private static readonly string LuminaEndpoint = "https://luminaserviceapi-test-westus.copilotlumina.com";
        private readonly LuminaServiceApiProxy _proxy;

        public LuminaSearchService(string accessToken)
        {
            var options = new LuminaApiOptions
            {
                Endpoint = LuminaEndpoint,
                LuminaApiTokenProvider = async () => await Task.FromResult(accessToken)
            };

            var httpClientFactory = new DefaultHttpClientFactory();
            _proxy = new LuminaServiceApiProxy(options, httpClientFactory);
        }

        /// <summary>
        /// Execute basic search (following the Step 3 example from documentation)
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
        /// Execute batch search for company stock price and latest news
        /// </summary>
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
                
                // Parse results into two categories
                var stockResults = new List<Models.SearchResult>();
                var newsResults = new List<Models.SearchResult>();
                
                if (searchResult?.Results != null && searchResult.Results.Count > 0)
                {
                    // First half are stock results, second half are news results
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
        /// Execute web search and return structured results for web interface
        /// </summary>
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

        /// <summary>
        /// Display search results
        /// </summary>
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

        /// <summary>
        /// Open and retrieve full content from a specific URL using Lumina API
        /// </summary>
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

                var openRequest = new OpenRequest
                {
                    Requests = new List<OpenRequestItem>
                    {
                        new OpenRequestItem
                        {
                            RefId = url
                        }
                    }
                };

                var response = await _proxy.OpenAsync(openRequest);
                
                if (response != null && response.Pages != null && response.Pages.Count > 0)
                {
                    var page = response.Pages[0];
                    string content = page.Content ?? "";
                    
                    // Check if content was filtered or failed to load
                    var isContentFiltered = content.Contains("filtered content") || 
                                          content.Contains("Failed to open") ||
                                          string.IsNullOrWhiteSpace(content);
                    
                    if (isContentFiltered)
                    {
                        throw new Exception("Content was filtered or failed to load. This may be due to content restrictions.");
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
    }

    /// <summary>
    /// Simple HTTP client factory implementation
    /// </summary>
    public class DefaultHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }
}