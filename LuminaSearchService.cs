using Microsoft.Lumina.Client.ApiProxy;
using Microsoft.Lumina.Client.Models.Sonicberry;
using Newtonsoft.Json;
using System.Text.Json.Serialization;
using static Microsoft.Lumina.Common.Constants.ConstantStrings;
using LuminaSearchConsole.Models;
using LuminaSearchConsole.Services;

namespace LuminaSearchConsole
{
    /// <summary>
    /// Lumina Search API Service
    /// 
    /// Demonstrates integration with Lumina Search, Open, and Find APIs:
    /// - Web search using Bing provider
    /// - Batch search (multiple queries in one request)
    /// - Content extraction from URLs (Open API)
    /// - Pattern matching within opened content (Find API)
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
        /// <param name="apiLogService">Optional API log service for tracking</param>
        /// <param name="feature">Feature name for log categorization</param>
        /// <returns>Structured results with stock and news separated</returns>
        public async Task<BatchSearchResult> ExecuteBatchCompanySearchAsync(string companyName, int topN = 5, ApiLogService? apiLogService = null, string? feature = null)
        {
            if (string.IsNullOrWhiteSpace(companyName))
            {
                throw new ArgumentException("Company name cannot be empty", nameof(companyName));
            }

            apiLogService?.AddLog("Lumina Search API", "POST /api/sonicberry/search", 
                $"Parameters\n" +
                $"{{\n" +
                $"  \"requests\": [\n" +
                $"    {{ \"q\": \"{companyName} stock price\", \"topN\": {topN}, \"source\": \"WebWithBing\", \"recency\": 7 }},\n" +
                $"    {{ \"q\": \"{companyName} latest news\", \"topN\": {topN}, \"source\": \"WebWithBing\", \"recency\": 7 }}\n" +
                $"  ]\n" +
                $"}}", true, feature);

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
                var startTime = DateTime.Now;
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

                var stockPreview = stockResults.Count > 0 && stockResults[0].Title != null ? 
                    stockResults[0].Title!.Substring(0, Math.Min(50, stockResults[0].Title.Length)) + "..." : "(no results)";
                var newsPreview = newsResults.Count > 0 && newsResults[0].Title != null ? 
                    newsResults[0].Title!.Substring(0, Math.Min(50, newsResults[0].Title.Length)) + "..." : "(no results)";
                
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
        /// Execute web search and return structured results
        /// 
        /// This is the main search method used by the web interface.
        /// Returns clean, structured data ready for display.
        /// </summary>
        /// <param name="query">Search query text</param>
        /// <param name="topN">Maximum number of results (1-50)</param>
        /// <param name="domains">Optional: Specific domains to restrict search results to (e.g., ["wikipedia.org"])</param>
        /// <param name="apiLogService">Optional API log service for tracking</param>
        /// <param name="feature">Feature name for log categorization</param>
        /// <returns>List of search results with title, URL, and summary</returns>
        public async Task<List<Models.SearchResult>> ExecuteWebSearchAsync(string query, int topN = 5, string[]? domains = null, ApiLogService? apiLogService = null, string? feature = null)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new ArgumentException("Search query cannot be empty", nameof(query));
            }

            if (topN <= 0 || topN > 50)
            {
                throw new ArgumentOutOfRangeException(nameof(topN), "TopN must be between 1 and 50");
            }

            apiLogService?.AddLog("Lumina Search API", "POST /api/sonicberry/search", 
                $"Parameters\n" +
                $"{{\n" +
                $"  \"q\": \"{query}\",\n" +
                $"  \"topN\": {topN},\n" +
                $"  \"source\": \"WebWithBing\",\n" +
                $"  \"market\": \"en-US\"\n" +
                $"}}", true, feature);

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
                        Domains = domains?.ToList(), // Restrict to specific domains if provided
                        AdditionalConfig = new SearchRequestAdditionalConfig
                        {
                            MaxSemanticDocumentLength = 200
                        }
                    }
                }
            };

            try
            {
                var domainInfo = domains != null && domains.Length > 0 
                    ? $", Domains=[{string.Join(", ", domains)}]" 
                    : "";
                var startTime = DateTime.Now;
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

                var preview = results.Count > 0 && results[0].Title != null ? 
                    results[0].Title!.Substring(0, Math.Min(50, results[0].Title.Length)) + "..." : "(no results)";
                
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

        #region Content Extraction (Open API)

        /// <summary>
        /// Open and retrieve full content from a URL using Lumina Open API
        /// 
        /// Open API Use Case:
        /// When search returns a result URL, use Open API to extract the full page content.
        /// This is useful for content analysis, summarization, or detailed viewing.
        /// Returns content, session ID, links, and page context for follow-up operations.
        /// </summary>
        /// <param name="url">Full URL to open and extract content from</param>
        /// <param name="sessionId">Optional session ID to maintain context</param>
        /// <returns>OpenContentResult with content, links, and navigation context</returns>
        public async Task<OpenContentResult> OpenContentWithLinksAsync(string url, string? sessionId = null)
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
                var sessionInfo = string.IsNullOrEmpty(sessionId) ? "New session" : $"Session: {sessionId}";
                
                // Create Open API request with URL reference
                var openRequest = new OpenRequest
                {
                    Requests = new List<OpenRequestItem>
                    {
                        new OpenRequestItem
                        {
                            RefId = url  // Direct URL reference
                        }
                    },
                    ToolState = string.IsNullOrEmpty(sessionId) ? null : new Microsoft.Lumina.Client.Models.Sonicberry.ToolState { SessionId = sessionId }
                };

                var response = await _proxy.OpenAsync(openRequest);
                
                if (response != null && response.Pages != null && response.Pages.Count > 0)
                {
                    var page = response.Pages[0];
                    string content = page.Content ?? "";
                    string newSessionId = response.ToolState?.SessionId ?? "";
                    
                    // Extract links - handle dynamic type
                    var linksList = new List<dynamic>();
                    if (page.Doc?.Links != null)
                    {
                        foreach (var link in page.Doc.Links)
                        {
                            linksList.Add(link);
                            if (linksList.Count >= 20) break;
                        }
                    }
                    
                    var pageContext = page.PageContext;
                    
                    // Validate content quality
                    var isContentFiltered = content.Contains("filtered content") || 
                                          content.Contains("Failed to open") ||
                                          string.IsNullOrWhiteSpace(content);
                    
                    if (isContentFiltered)
                    {
                                                throw new Exception($"No content available from the URL: {url}");
                    }
                    
                                        return new OpenContentResult
                    {
                        Content = content,
                        SessionId = newSessionId,
                        Links = linksList,
                        PageContext = pageContext,
                        Url = page.Url ?? url,
                        Title = page.Title ?? ""
                    };
                }
                else
                {
                                        throw new Exception($"No content available from the URL: {url}");
                }
            }
            catch (HttpRequestException ex)
            {
                                throw new Exception($"Network error occurred while accessing '{url}'. Please check your internet connection.", ex);
            }
            catch (ArgumentException)
            {
                throw; // Re-throw validation errors as-is
            }
            catch (Exception ex)
            {
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
            var result = await OpenContentWithLinksAsync(url);
            return result.Content;
        }

        /// <summary>
        /// Click a link within an opened page using Lumina Click API
        /// </summary>
        /// <param name="sessionId">Session ID from previous Open operation</param>
        /// <param name="linkId">Link identifier (e.g., "13" or "link_13")</param>
        /// <param name="pageContext">Page context from the page containing the link</param>
        /// <returns>OpenContentResult with the clicked page's content and links</returns>
        public async Task<OpenContentResult> ClickLinkAsync(string sessionId, string linkId, dynamic pageContext)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("Session ID cannot be empty", nameof(sessionId));
            }

            if (string.IsNullOrWhiteSpace(linkId))
            {
                throw new ArgumentException("Link ID cannot be empty", nameof(linkId));
            }

            try
            {
                
                // Create Click request
                var clickRequest = new ClickRequest
                {
                    Requests = new List<ClickRequestItem>
                    {
                        new ClickRequestItem
                        {
                            RefId = linkId,
                            PageContext = new PageContextInfo
                            {
                                Turn = (int)(pageContext.Turn ?? 0),
                                Action = pageContext.Action?.ToString() ?? "view",
                                Id = (int)(pageContext.Id ?? 0)
                            }
                        }
                    },
                    ToolState = new Microsoft.Lumina.Client.Models.Sonicberry.ToolState
                    {
                        SessionId = sessionId
                    }
                };

                var clickResponse = await _proxy.ClickAsync(clickRequest);
                
                if (clickResponse != null && clickResponse.Pages != null && clickResponse.Pages.Count > 0)
                {
                    var page = clickResponse.Pages[0];
                    string content = page.Content ?? "";
                    
                    // Extract links
                    var linksList = new List<dynamic>();
                    if (page.Doc?.Links != null)
                    {
                        foreach (var link in page.Doc.Links)
                        {
                            linksList.Add(link);
                            if (linksList.Count >= 20) break;
                        }
                    }
                    
                    var newPageContext = page.PageContext;
                    
                                        
                    return new OpenContentResult
                    {
                        Content = content,
                        SessionId = sessionId,
                        Links = linksList,
                        PageContext = newPageContext,
                        Url = page.Url ?? "",
                        Title = page.Title ?? ""
                    };
                }
                else
                {
                    throw new Exception($"No content available from clicked link");
                }
            }
            catch (HttpRequestException ex)
            {
                                throw new Exception($"Network error occurred while clicking link. Please check your internet connection.", ex);
            }
            catch (Exception ex)
            {
                                throw new Exception($"Failed to click link '{linkId}': {ex.Message}", ex);
            }
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
                    var firstMatch = response.Results[0];
                    var preview = (firstMatch.Template?.Length ?? 0) > 50 ? 
                        firstMatch.Template!.Substring(0, 50) + "..." : 
                        firstMatch.Template ?? "";
                                        return response;
                }
                else
                {
                                        return response!;
                }
            }
            catch (HttpRequestException ex)
            {
                                throw new Exception($"Network error occurred during Find operation. Please check your internet connection.", ex);
            }
            catch (Exception ex)
            {
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
                                
                // Step 1: Open the URL to get content and session
                var result = await OpenContentWithLinksAsync(url);
                string content = result.Content;
                string sessionId = result.SessionId;
                
                if (string.IsNullOrEmpty(sessionId))
                {
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
                                                                
                                companyInfo.Fields.Add(new InfoField
                                {
                                    LineNumber = firstMatch.LineIdx ?? 0,
                                    Content = cleanContent,
                                    FieldName = pattern
                                });
                            }
                        }
                    }
                    catch (Exception)
                    {
                                            }
                }
                
                // Return results if we found any infobox data
                if (companyInfo.Fields.Any())
                {
                    return companyInfo;
                }
                
                                return null;
            }
            catch
            {
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

    #region Content Extraction Models

    /// <summary>
    /// Result from Open API or Click API containing page content and navigation context
    /// </summary>
    public class OpenContentResult
    {
        public string Content { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public List<dynamic> Links { get; set; } = new();
        public dynamic? PageContext { get; set; }
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    /// <summary>
    /// Simplified link information for UI display
    /// </summary>
    public class PageLink
    {
        public int LinkId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
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