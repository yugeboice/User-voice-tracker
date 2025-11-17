using Microsoft.Lumina.Client.ApiProxy;
using Microsoft.Lumina.Client.Models.Sonicberry;
using Newtonsoft.Json;
using System.Text.Json.Serialization;
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

        #region Content Extraction (Open API)

        /// <summary>
        /// Open and retrieve full content from a URL using Lumina Open API
        /// 
        /// Open API Use Case:
        /// When search returns a result URL, use Open API to extract the full page content.
        /// This is useful for content analysis, summarization, or detailed viewing.
        /// Returns both content and session ID for follow-up Find operations.
        /// </summary>
        /// <param name="url">Full URL to open and extract content from</param>
        /// <returns>Tuple with extracted page content and session ID</returns>
        public async Task<(string content, string sessionId)> OpenContentWithSessionAsync(string url)
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

                Console.WriteLine($"📤 Sending Open API request...");
                var response = await _proxy.OpenAsync(openRequest);
                Console.WriteLine($"📥 Received Open API response");
                
                // Debug: Log response structure
                Console.WriteLine($"📋 Open API Response Debug:");
                Console.WriteLine($"  Response is null: {response == null}");
                if (response != null)
                {
                    Console.WriteLine($"  Pages is null: {response.Pages == null}");
                    Console.WriteLine($"  Pages count: {response.Pages?.Count ?? 0}");
                    Console.WriteLine($"  ToolState is null: {response.ToolState == null}");
                    Console.WriteLine($"  SessionId: {response.ToolState?.SessionId ?? "null"}");
                }
                
                if (response != null && response.Pages != null && response.Pages.Count > 0)
                {
                    var page = response.Pages[0];
                    string content = page.Content ?? "";
                    string sessionId = response.ToolState?.SessionId ?? "";
                    
                    // Debug: Log page details
                    Console.WriteLine($"📄 Page Details:");
                    Console.WriteLine($"  URL: {page.Url ?? "null"}");
                    Console.WriteLine($"  Title: {page.Title ?? "null"}");
                    Console.WriteLine($"  Content length: {content.Length}");
                    Console.WriteLine($"  Content is empty: {string.IsNullOrWhiteSpace(content)}");
                    if (content.Length > 0 && content.Length < 500)
                    {
                        Console.WriteLine($"  Content preview: {content}");
                    }
                    else if (content.Length > 0)
                    {
                        Console.WriteLine($"  Content preview (first 200 chars): {content.Substring(0, Math.Min(200, content.Length))}");
                    }
                    
                    // Validate content quality
                    var isContentFiltered = content.Contains("filtered content") || 
                                          content.Contains("Failed to open") ||
                                          string.IsNullOrWhiteSpace(content);
                    
                    if (isContentFiltered)
                    {
                        Console.WriteLine($"⚠️ Content validation failed:");
                        Console.WriteLine($"  Contains 'filtered content': {content.Contains("filtered content")}");
                        Console.WriteLine($"  Contains 'Failed to open': {content.Contains("Failed to open")}");
                        Console.WriteLine($"  Is whitespace: {string.IsNullOrWhiteSpace(content)}");
                        throw new Exception($"No content available from the URL: {url}");
                    }
                    
                    Console.WriteLine($"✅ Successfully retrieved content. Length: {content.Length} characters, SessionId: {sessionId}");
                    return (content, sessionId);
                }
                else
                {
                    Console.WriteLine($"⚠️ Response structure validation failed");
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

        /// <summary>
        /// Open and retrieve full content from a URL (backward compatible version)
        /// </summary>
        /// <param name="url">Full URL to open and extract content from</param>
        /// <returns>Extracted page content as text</returns>
        public async Task<string> OpenContentAsync(string url)
        {
            var (content, _) = await OpenContentWithSessionAsync(url);
            return content;
        }

        #endregion

        #region Content Finding (Find API)

        /// <summary>
        /// Find specific patterns within opened page content using Lumina Find API
        /// 
        /// Find API Use Case:
        /// After opening a page with Open API, use Find to search for specific patterns (like stock price)
        /// within the page content. Similar to Ctrl+F in a browser.
        /// </summary>
        /// <param name="pattern">The pattern/text to search for (e.g., "price", "stock")</param>
        /// <param name="url">URL that was previously opened</param>
        /// <param name="sessionId">Session ID from previous Open operation</param>
        /// <returns>List of matching results with line numbers and content</returns>
        public async Task<FindResponse> FindContentAsync(string pattern, string url, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                throw new ArgumentException("Pattern cannot be empty", nameof(pattern));
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("Session ID is required for Find operation", nameof(sessionId));
            }

            try
            {
                Console.WriteLine($"🔍 Finding pattern '{pattern}' in content");

                // Create Find API request
                var findRequest = new FindRequest
                {
                    Requests = new List<FindRequestItem>
                    {
                        new FindRequestItem
                        {
                            Pattern = pattern,
                            PageContext = new PageContextInfo
                            {
                                Turn = 0,
                                Action = "view",
                                Id = 0
                            }
                        }
                    },
                    ToolState = new ToolState
                    {
                        SessionId = sessionId
                    }
                };

                var response = await _proxy.FindAsync(findRequest);
                
                if (response != null && response.Results != null && response.Results.Count > 0)
                {
                    Console.WriteLine($"✅ Found {response.Results.Count} matches for pattern '{pattern}'");
                    return response;
                }
                else
                {
                    Console.WriteLine($"⚠️ No matches found for pattern '{pattern}'");
                    return response!;
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"❌ Network error while finding content: {ex.Message}");
                throw new Exception($"Network error occurred during Find operation. Please check your internet connection.", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error finding content: {ex.Message}");
                throw new Exception($"Failed to find pattern '{pattern}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Extract company information from Wikipedia using Find API
        /// 
        /// Use Case:
        /// 1. Open the Wikipedia URL with Open API to get page content and session ID
        /// 2. Use Find API to search for company information keywords in the content
        /// 3. Extract and return company information like founded date, headquarters, revenue, etc.
        /// </summary>
        /// <param name="url">Wikipedia URL for the company</param>
        /// <returns>Extracted company information or null if not found</returns>
        public async Task<CompanyInfo?> ExtractCompanyInfoAsync(string url)
        {
            try
            {
                Console.WriteLine($"🏢 Extracting company information from: {url}");
                
                // Step 1: Open the URL to get content and session
                var (content, sessionId) = await OpenContentWithSessionAsync(url);
                
                if (string.IsNullOrEmpty(sessionId))
                {
                    Console.WriteLine("⚠️ No session ID returned from Open API");
                    return null;
                }
                
                // Step 2: Use Find API to search for Wikipedia infobox patterns
                // Search for key-value pairs commonly found in Wikipedia company infoboxes
                string[] patterns = { "Founded", "Headquarters", "Revenue", "Industry", "Type" };
                
                var companyInfo = new CompanyInfo
                {
                    Url = url,
                    Fields = new List<InfoField>()
                };
                
                foreach (var pattern in patterns)
                {
                    try
                    {
                        var findResponse = await FindContentAsync(pattern, url, sessionId);
                        
                        if (findResponse?.Results != null && findResponse.Results.Count > 0)
                        {
                            // Extract only the first match for each pattern (likely from infobox)
                            var firstMatch = findResponse.Results.FirstOrDefault();
                            if (firstMatch != null)
                            {
                                var matchContent = firstMatch.Template ?? "";
                                
                                // Try to extract just the value part (after the field name)
                                var cleanContent = ExtractInfoboxValue(matchContent, pattern);
                                Console.WriteLine($"✅ {pattern}: {cleanContent}");
                                
                                companyInfo.Fields.Add(new InfoField
                                {
                                    LineNumber = firstMatch.LineIdx ?? 0,
                                    Content = cleanContent,
                                    FieldName = pattern
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ {pattern}: {ex.Message}");
                    }
                }
                
                // Return results if we found any infobox data
                if (companyInfo.Fields.Any())
                {
                    return companyInfo;
                }
                
                Console.WriteLine($"⚠️ No company information patterns found");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error extracting company info: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Extract clean value from Wikipedia infobox content
        /// </summary>
        private string ExtractInfoboxValue(string rawContent, string fieldName)
        {
            // Remove link markers like [[[link_0]]]
            var content = System.Text.RegularExpressions.Regex.Replace(rawContent, @"\[\[\[link_\d+\]\]\]", "");
            
            // Strategy 1: Look for "FieldName:" pattern (most common in Wikipedia infoboxes)
            var colonPattern = $@"{fieldName}\s*:\s*([^\n\|]+)";
            var colonMatch = System.Text.RegularExpressions.Regex.Match(content, colonPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (colonMatch.Success && colonMatch.Groups.Count > 1)
            {
                var value = colonMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(value) && value.Length > 3)
                {
                    return CleanAndLimit(value, 150);
                }
            }
            
            // Strategy 2: Look for pipe-delimited table format "| FieldName | Value"
            var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                
                // Check if this line is the field name
                if (line.StartsWith("|") && line.Contains(fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    // Extract content between pipes
                    var cells = line.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    if (cells.Length >= 2)
                    {
                        // First cell might be the field name, second is the value
                        var fieldCell = cells[0].Trim();
                        if (fieldCell.Equals(fieldName, StringComparison.OrdinalIgnoreCase) && cells.Length > 1)
                        {
                            var valueCell = cells[1].Trim();
                            if (!string.IsNullOrWhiteSpace(valueCell) && valueCell.Length > 3)
                            {
                                return CleanAndLimit(valueCell, 150);
                            }
                        }
                    }
                }
            }
            
            // Strategy 3: Simple extraction - find field name and take the next meaningful text
            var fieldIndex = content.IndexOf(fieldName, StringComparison.OrdinalIgnoreCase);
            if (fieldIndex >= 0)
            {
                var afterField = content.Substring(fieldIndex + fieldName.Length);
                // Skip common separators
                afterField = System.Text.RegularExpressions.Regex.Replace(afterField, @"^[\s\|:\-]+", "");
                
                // Take text until newline or pipe
                var match = System.Text.RegularExpressions.Regex.Match(afterField, @"^([^\n\|]{5,150})");
                if (match.Success)
                {
                    return CleanAndLimit(match.Groups[1].Value, 150);
                }
            }
            
            return "N/A";
        }
        
        /// <summary>
        /// Clean and limit the length of extracted value
        /// </summary>
        private string CleanAndLimit(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "N/A";
            
            // Remove multiple spaces
            value = System.Text.RegularExpressions.Regex.Replace(value, @"\s+", " ").Trim();
            
            // Remove markdown/wiki formatting
            value = value.Replace("**", "").Replace("__", "");
            
            // Limit length
            if (value.Length > maxLength)
            {
                value = value.Substring(0, maxLength).Trim() + "...";
            }
            
            return value;
        }

        #endregion

        #region Computer Use Agent (CUA) Methods

        /// <summary>
        /// Initialize a virtual computer for CUA operations
        /// </summary>
        public async Task<string> InitializeComputerAsync(string computerId, string userId, string tenantId)
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync());

            var body = new
            {
                computerId = computerId,
                userId = userId,
                tenantId = tenantId
            };

            var response = await httpClient.PostAsJsonAsync(
                $"{_luminaEndpoint}/api/agent/computer/initialize", 
                body);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                var statusCode = (int)response.StatusCode;
                var errorMessage = statusCode switch
                {
                    507 => "CUA service is at capacity. Virtual computers are currently unavailable. Please try again later.",
                    401 => "Authentication failed. Please login again.",
                    403 => "Access denied. You may not have permission to use CUA service.",
                    _ => $"CUA initialization failed with status {statusCode}: {response.ReasonPhrase}"
                };
                throw new HttpRequestException($"{errorMessage}\n\nDetails: {errorContent}", null, response.StatusCode);
            }

            Console.WriteLine($"✅ Computer initialized: {computerId}");
            return computerId;
        }

        /// <summary>
        /// Get screenshot from virtual computer
        /// </summary>
        public async Task<CuaScreenshotResponse> GetComputerScreenshotAsync(string computerId)
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync());

            var body = new { computerId = computerId };

            var response = await httpClient.PostAsJsonAsync(
                $"{_luminaEndpoint}/api/agent/computer/get", 
                body);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Failed to get screenshot: {response.ReasonPhrase}\n{errorContent}", null, response.StatusCode);
            }

            var result = await response.Content.ReadFromJsonAsync<CuaScreenshotResponse>();
            Console.WriteLine($"✅ Screenshot retrieved: {result?.Content?.Width}x{result?.Content?.Height}");
            return result!;
        }

        /// <summary>
        /// Perform actions on virtual computer (navigate, click, type, etc.)
        /// </summary>
        public async Task<CuaActionResponse> PerformComputerActionsAsync(string computerId, List<CuaAction> actions, int actionDelayMs = 800)
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync());

            var body = new
            {
                computerId = computerId,
                actions = actions,
                actionDelayMs = actionDelayMs.ToString()
            };

            var response = await httpClient.PostAsJsonAsync(
                $"{_luminaEndpoint}/api/agent/computer/do", 
                body);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Failed to perform actions: {response.ReasonPhrase}\n{errorContent}", null, response.StatusCode);
            }

            var result = await response.Content.ReadFromJsonAsync<CuaActionResponse>();
            Console.WriteLine($"✅ Actions performed successfully");
            return result!;
        }

        /// <summary>
        /// Navigate to a URL in the virtual computer
        /// </summary>
        public async Task NavigateToUrlAsync(string computerId, string url)
        {
            Console.WriteLine($"🌐 Navigating to: {url}");

            var actions = new List<CuaAction>
            {
                new CuaAction { Action = "keypress", Keys = new[] { "ctrl", "l" } },
                new CuaAction { Action = "type", Text = url },
                new CuaAction { Action = "keypress", Keys = new[] { "enter" } },
                new CuaAction { Action = "wait" }
            };

            await PerformComputerActionsAsync(computerId, actions);
            Console.WriteLine($"✅ Navigation completed");
        }

        /// <summary>
        /// Release virtual computer resources
        /// </summary>
        public async Task ReleaseComputerAsync(string computerId)
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync());

            var body = new { computerId = computerId };

            var response = await httpClient.PostAsJsonAsync(
                $"{_luminaEndpoint}/api/agent/computer/release", 
                body);
            response.EnsureSuccessStatusCode();

            Console.WriteLine($"✅ Computer released: {computerId}");
        }

        /// <summary>
        /// Helper method to get access token from proxy
        /// </summary>
        private async Task<string> GetAccessTokenAsync()
        {
            // Use reflection to access the token provider from the proxy
            var optionsField = _proxy.GetType().GetField("_options", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var options = optionsField?.GetValue(_proxy) as LuminaApiOptions;
            return await options?.LuminaApiTokenProvider?.Invoke()! ?? string.Empty;
        }

        #endregion
    }

    #region Company Information Models

    /// <summary>
    /// Company information extracted from Wikipedia
    /// </summary>
    public class CompanyInfo
    {
        public string Url { get; set; } = string.Empty;
        public List<InfoField> Fields { get; set; } = new();
    }

    /// <summary>
    /// Individual information field extracted from company page
    /// </summary>
    public class InfoField
    {
        public long LineNumber { get; set; }
        public string Content { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
    }

    #endregion

    #region CUA Model Classes

    public class CuaAction
    {
        [JsonProperty("action")]
        public string Action { get; set; } = string.Empty;

        [JsonProperty("keys", NullValueHandling = NullValueHandling.Ignore)]
        public string[]? Keys { get; set; }

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string? Text { get; set; }

        [JsonProperty("x", NullValueHandling = NullValueHandling.Ignore)]
        public int? X { get; set; }

        [JsonProperty("y", NullValueHandling = NullValueHandling.Ignore)]
        public int? Y { get; set; }

        [JsonProperty("button", NullValueHandling = NullValueHandling.Ignore)]
        public int? Button { get; set; }
    }

    public class CuaScreenshotResponse
    {
        [JsonPropertyName("messageId")]
        public string? MessageId { get; set; }

        [JsonPropertyName("createTime")]
        public double CreateTime { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("content")]
        public CuaScreenshotContent? Content { get; set; }
    }

    public class CuaScreenshotContent
    {
        [JsonPropertyName("screenshot")]
        public string? Screenshot { get; set; }

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }

        [JsonPropertyName("success")]
        public bool Success { get; set; }
    }

    public class CuaActionResponse
    {
        [JsonPropertyName("messageId")]
        public string? MessageId { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    #endregion


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