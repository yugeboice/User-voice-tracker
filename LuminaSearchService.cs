using Microsoft.Lumina.Client.ApiProxy;
using Microsoft.Lumina.Client.Models.Sonicberry;
using Newtonsoft.Json;
using static Microsoft.Lumina.Common.Constants.ConstantStrings;

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