using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Lumina.Client.ApiProxy;
using Microsoft.Lumina.Client.Models.Sonicberry;
using static Microsoft.Lumina.Common.Constants.ConstantStrings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using System.Reflection;

public class ExpandQueryRequest
{
    public string Query { get; set; } = "";
}

public class BatchSearchRequest
{
    public string Query { get; set; } = "";
    public bool UseAiExpansion { get; set; } = true;
}

public class CompanySearchRequest
{
    [JsonPropertyName("companyName")]
    public string CompanyName { get; set; } = "";
}

namespace BasicApiCall
{
    class Program
    {
        private static OAuthService _oauthService = new OAuthService();
        private static IHttpClientFactory? _httpClientFactory;
    
    static async Task Main(string[] args)
    {
        Console.WriteLine("🌐 Lumina Search Service Starting...");
        
        // Start web server first, then handle OAuth in callbacks
        await StartWebServerWithAuth();
    }
    
    static async Task StartWebServerWithAuth()
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        
        // Main page - display authentication status and search interface
        app.MapGet("/", async context =>
        {
            // Check if this is an OAuth callback (contains code or error parameters)
            var query = context.Request.Query;
            if (query.ContainsKey("code") || query.ContainsKey("error"))
            {
                // This is an OAuth callback, handle it
                await HandleOAuthCallback(context);
                return;
            }
            
            if (!_oauthService.IsAuthenticated)
            {
                await context.Response.WriteAsync(GetAuthHtml());
            }
            else
            {
                await context.Response.WriteAsync(GetSearchHtml());
            }
        });
        
        // OAuth callback handler
        app.MapGet("/auth/callback", async context =>
        {
            // Redirect to main page after OAuth authentication completion
            await context.Response.WriteAsync("""
<html>
<head>
    <meta charset="UTF-8">
    <title>Authentication Successful</title>
    <style>
        body { 
            font-family: Arial; 
            text-align: center; 
            margin-top: 100px; 
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            height: 100vh;
            margin: 0;
            display: flex;
            align-items: center;
            justify-content: center;
        }
        .container {
            background: rgba(255,255,255,0.1);
            padding: 40px;
            border-radius: 15px;
            backdrop-filter: blur(10px);
        }
        h1 { font-size: 2.5em; margin-bottom: 20px; }
        p { font-size: 1.2em; }
    </style>
</head>
<body>
    <div class="container">
        <h1>✅ Authentication Successful!</h1>
        <p>Returning to search page...</p>
        <script>
            // Notify parent window that authentication is complete
            if (window.opener) {
                window.opener.postMessage('auth_complete', '*');
                setTimeout(() => {
                    window.opener.location.reload();
                    window.close();
                }, 1500);
            } else {
                // If no parent window, redirect directly
                setTimeout(() => {
                    window.location.href = '/';
                }, 1500);
            }
        </script>
    </div>
</body>
</html>
""");
        });
        
        // Start authentication flow
        app.MapPost("/auth/start", async context =>
        {
            try
            {
                var success = await _oauthService.InitializeAuthenticationAsync();
                if (success)
                {
                    // HTTP client initialization
                    var services = new ServiceCollection();
                    services.AddHttpClient();
                    _httpClientFactory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
                }
                await context.Response.WriteAsJsonAsync(new { success });
            }
            catch (Exception ex)
            {
                await context.Response.WriteAsJsonAsync(new { success = false, error = ex.Message });
            }
        });
        
        // Handle search requests
        app.MapPost("/search", async context =>
        {
            if (!_oauthService.IsAuthenticated)
            {
                await context.Response.WriteAsJsonAsync(new { success = false, error = "Please complete authentication first" });
                return;
            }
            
            var json = await new StreamReader(context.Request.Body).ReadToEndAsync();
            var data = JsonSerializer.Deserialize<SearchData>(json);
            
            Console.WriteLine($"\n🔍 Search: {data?.query}");
            
            var result = await PerformSearchWithOpen(data?.query ?? "");
            
            Console.WriteLine($"Found search results and opened first page");
            
            await context.Response.WriteAsJsonAsync(new { 
                success = true, 
                result = result
            });
        });

        // Handle company search requests (dual search for stock price and news)
        app.MapPost("/company-search", async context =>
        {
            var json = await new StreamReader(context.Request.Body).ReadToEndAsync();
            Console.WriteLine($"📨 Received company search request: {json}");
            
            if (!_oauthService.IsAuthenticated)
            {
                Console.WriteLine("❌ Not authenticated");
                await context.Response.WriteAsJsonAsync(new { success = false, error = "Please complete authentication first" });
                return;
            }
            
            var data = JsonSerializer.Deserialize<CompanySearchRequest>(json);
            Console.WriteLine($"🔍 Parsed company data: CompanyName = '{data?.CompanyName}'");
            
            if (string.IsNullOrEmpty(data?.CompanyName))
            {
                Console.WriteLine("❌ Company name is null or empty");
                await context.Response.WriteAsJsonAsync(new { success = false, error = "Company name is required" });
                return;
            }
            
            Console.WriteLine($"\n🏢 Company Search: {data.CompanyName}");
            
            var result = await PerformDualCompanySearch(data.CompanyName);
            
            Console.WriteLine($"Completed dual search for {data.CompanyName}");
            
            await context.Response.WriteAsJsonAsync(new { 
                success = true, 
                result = result
            });
        });

        // AI-powered query expansion endpoint
        app.MapPost("/ai/expand-query", async context =>
        {
            try
            {
                var json = await new StreamReader(context.Request.Body).ReadToEndAsync();
                var data = JsonSerializer.Deserialize<ExpandQueryRequest>(json);
                
                if (string.IsNullOrEmpty(data?.Query))
                {
                    await context.Response.WriteAsJsonAsync(new { success = false, error = "Query is required" });
                    return;
                }

                Console.WriteLine($"\n🤖 AI Expanding query: {data.Query}");
                
                var expandedQueries = await CallCopilotBridgeService(data.Query);
                
                await context.Response.WriteAsJsonAsync(new { 
                    success = true, 
                    originalQuery = data.Query,
                    expandedQueries = expandedQueries,
                    source = "github-copilot-bridge"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in AI expand query: {ex.Message}");
                await context.Response.WriteAsJsonAsync(new { 
                    success = false, 
                    error = ex.Message 
                });
            }
        });

        // AI-powered batch search endpoint
        app.MapPost("/ai/batch-search", async context =>
        {
            try
            {
                var json = await new StreamReader(context.Request.Body).ReadToEndAsync();
                var data = JsonSerializer.Deserialize<BatchSearchRequest>(json);
                
                if (string.IsNullOrEmpty(data?.Query))
                {
                    await context.Response.WriteAsJsonAsync(new { success = false, error = "Query is required" });
                    return;
                }

                Console.WriteLine($"\n🔥 AI Batch Search: {data.Query}");
                
                List<string> expandedQueries;
                if (data.UseAiExpansion)
                {
                    expandedQueries = await CallCopilotBridgeService(data.Query);
                }
                else
                {
                    expandedQueries = GenerateFallbackQueries(data.Query);
                }
                
                var result = await PerformBatchSearchWithOpen(data.Query, expandedQueries);
                
                await context.Response.WriteAsJsonAsync(new { 
                    success = true, 
                    result = result
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in AI batch search: {ex.Message}");
                await context.Response.WriteAsJsonAsync(new { 
                    success = false, 
                    error = ex.Message 
                });
            }
        });
        
        Console.WriteLine("🌐 Server started: http://localhost:8400");
        Console.WriteLine("📱 Please open in browser: http://localhost:8400");
        await app.RunAsync("http://localhost:8400");
    }
    
    static async Task HandleOAuthCallback(HttpContext context)
    {
        // OAuth callback handling - display success page
        await context.Response.WriteAsync("""
<html>
<head>
    <meta charset="UTF-8">
    <title>Authentication Successful</title>
    <style>
        body { 
            font-family: 'Segoe UI', Arial, sans-serif; 
            text-align: center; 
            margin: 0;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            height: 100vh;
            display: flex;
            align-items: center;
            justify-content: center;
        }
        .container {
            background: rgba(255,255,255,0.1);
            padding: 60px 40px;
            border-radius: 20px;
            backdrop-filter: blur(20px);
            box-shadow: 0 8px 32px rgba(0,0,0,0.3);
            max-width: 500px;
        }
        .success-icon {
            font-size: 4em;
            margin-bottom: 20px;
            animation: bounce 0.6s ease-in-out;
        }
        h1 { 
            font-size: 2.5em; 
            margin: 0 0 20px 0;
            font-weight: 300;
        }
        p { 
            font-size: 1.3em; 
            margin-bottom: 30px;
            opacity: 0.9;
        }
        .progress {
            width: 100%;
            height: 4px;
            background: rgba(255,255,255,0.3);
            border-radius: 2px;
            overflow: hidden;
            margin-top: 20px;
        }
        .progress-bar {
            width: 0%;
            height: 100%;
            background: linear-gradient(90deg, #4CAF50, #45a049);
            animation: progress 2s ease-in-out forwards;
        }
        @keyframes bounce {
            0%, 20%, 50%, 80%, 100% { transform: translateY(0); }
            40% { transform: translateY(-10px); }
            60% { transform: translateY(-5px); }
        }
        @keyframes progress {
            from { width: 0%; }
            to { width: 100%; }
        }
    </style>
</head>
<body>
    <div class="container">
        <div class="success-icon">✅</div>
        <h1>Authentication Successful!</h1>
        <p>You have successfully authenticated with Microsoft Azure AD.</p>
        <p>Redirecting to search page...</p>
        <div class="progress">
            <div class="progress-bar"></div>
        </div>
        <script>
            // Redirect to main page after showing success message
            setTimeout(() => {
                window.location.href = '/';
            }, 3000);
        </script>
    </div>
</body>
</html>
""");
    }
    
    static async Task<object> PerformSearchWithOpen(string query)
    {
        var options = new LuminaApiOptions
        {
            Endpoint = "https://luminaserviceapi-test-westus.copilotlumina.com",
            LuminaApiTokenProvider = async () => await Task.FromResult(_oauthService.AccessToken!),
            Scenario = "BasicSearch",
            TrafficType = "TEST"
        };

        var proxy = new LuminaServiceApiProxy(options, _httpClientFactory!, _ => { });

        // Step 1: Create search query
        var searchRequests = GenerateSearchQueries(query);
        
        var searchRequest = new SearchRequest
        {
            Requests = searchRequests
        };

        var searchResponse = await proxy.SearchAsync(searchRequest);
        
        if (searchResponse?.Results == null || searchResponse.Results.Count == 0)
        {
            return new { searchResults = new List<object>(), openedPage = (object?)null };
        }

        // Step 2: Auto-open the first result
        object? openedPageContent = null;
        try
        {
            var sessionId = searchResponse.ToolState?.SessionId;
            if (!string.IsNullOrEmpty(sessionId) && searchResponse.Results.Count > 0)
            {
                // Create open request using the first search result URL
                var openRequest = new OpenRequest
                {
                    Requests = new List<OpenRequestItem>
                    {
                        new OpenRequestItem
                        {
                            RefId = searchResponse.Results[0].Url,
                            PageContext = new PageContextInfo
                            {
                                Action = searchResponse.PageContext.Action,
                                Turn = searchResponse.PageContext.Turn,
                                Id = 0
                            }
                        }
                    },
                    ToolState = searchResponse.ToolState
                };
                
                var openResponse = await proxy.OpenAsync(openRequest);
                if (openResponse?.Pages != null && openResponse.Pages.Count > 0)
                {
                    var page = openResponse.Pages[0];
                    
                    // Check if content was filtered or failed to load
                    var content = page.Content ?? "";
                    var isContentFiltered = content.Contains("filtered content") || 
                                          content.Contains("Failed to open") ||
                                          string.IsNullOrWhiteSpace(content);
                    
                    if (isContentFiltered)
                    {
                        // Try opening the second result if available
                        if (searchResponse.Results.Count > 1)
                        {
                            Console.WriteLine("⚠️ First result was filtered, trying second result...");
                            var secondOpenRequest = new OpenRequest
                            {
                                Requests = new List<OpenRequestItem>
                                {
                                    new OpenRequestItem
                                    {
                                        RefId = searchResponse.Results[1].Url,
                                        PageContext = new PageContextInfo
                                        {
                                            Action = searchResponse.PageContext.Action,
                                            Turn = searchResponse.PageContext.Turn,
                                            Id = 1
                                        }
                                    }
                                },
                                ToolState = searchResponse.ToolState
                            };
                            
                            var secondOpenResponse = await proxy.OpenAsync(secondOpenRequest);
                            if (secondOpenResponse?.Pages != null && secondOpenResponse.Pages.Count > 0)
                            {
                                var secondPage = secondOpenResponse.Pages[0];
                                var secondContent = secondPage.Content ?? "";
                                
                                if (!secondContent.Contains("filtered content") && 
                                    !secondContent.Contains("Failed to open") &&
                                    !string.IsNullOrWhiteSpace(secondContent))
                                {
                                    page = secondPage;
                                    content = secondContent;
                                    isContentFiltered = false;
                                }
                            }
                        }
                    }
                    
                    openedPageContent = new
                    {
                        url = page.Url,
                        title = page.Title ?? (isContentFiltered ? "Content Filtered" : "No title"),
                        content = isContentFiltered ? 
                            "Content was filtered or failed to load. This may be due to content restrictions." :
                            (content.Length > 1000 ? content.Substring(0, 1000) + "..." : content),
                        isFiltered = isContentFiltered,
                        pageContext = new
                        {
                            action = page.PageContext?.Action,
                            turn = page.PageContext?.Turn,
                            id = page.PageContext?.Id
                        }
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to open first result: {ex.Message}");
        }

        // Build search results for display - limit to 3 results
        var searchResults = searchResponse.Results.Take(3).Select((item, index) => {
            // Get semantic document content as snippet
            string snippet = "";
            try 
            {
                // According to the API documentation, search results contain 'semanticDocument' field
                var type = item.GetType();
                var semanticDocProp = type.GetProperty("SemanticDocument");
                
                snippet = (semanticDocProp?.GetValue(item) as string) ?? "";
                
                // Log what we found for debugging
                if (!string.IsNullOrEmpty(snippet))
                {
                    Console.WriteLine($"   Result {index}: found semanticDocument with {snippet.Length} characters");
                }
                else 
                {
                    Console.WriteLine($"   Result {index}: no semanticDocument found");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   Error getting semanticDocument for result {index}: {ex.Message}");
            }
            
            return new {
                index = index,
                title = item.Title ?? "",
                url = item.Url ?? "",
                snippet = snippet
            };
        }).ToList();

        return new { searchResults, openedPage = openedPageContent };
    }
    
    static async Task<object> PerformDualCompanySearch(string companyName)
    {
        var options = new LuminaApiOptions
        {
            Endpoint = "https://luminaserviceapi-test-westus.copilotlumina.com",
            LuminaApiTokenProvider = async () => await Task.FromResult(_oauthService.AccessToken!),
            Scenario = "CompanyResearch",
            TrafficType = "TEST"
        };

        var proxy = new LuminaServiceApiProxy(options, _httpClientFactory!, _ => { });

        // Create two search queries: stock price and latest news
        var stockQuery = $"{companyName} stock price share price market value";
        var newsQuery = $"{companyName} latest news recent updates";
        
        Console.WriteLine($"📈 Stock search: {stockQuery}");
        Console.WriteLine($"📰 News search: {newsQuery}");

        // Perform both searches in parallel
        var stockTask = PerformSingleSearch(proxy, stockQuery, "stock");
        var newsTask = PerformSingleSearch(proxy, newsQuery, "news");
        
        await Task.WhenAll(stockTask, newsTask);
        
        var stockResults = await stockTask;
        var newsResults = await newsTask;

        return new { 
            stockResults = stockResults.Take(3).ToList(),
            newsResults = newsResults.Take(3).ToList(),
            companyName = companyName
        };
    }
    
    static async Task<List<object>> PerformSingleSearch(LuminaServiceApiProxy proxy, string query, string searchType)
    {
        try
        {
            var searchRequests = new List<SearchRequestItem>
            {
                new SearchRequestItem
                {
                    Q = query,
                    TopN = 3,
                    Source = SearchProviders.WebWithBing,
                    Language = "en",
                    Market = "en-US",
                    AdditionalConfig = new SearchRequestAdditionalConfig
                    {
                        MaxSemanticDocumentLength = 500,
                        MaxGroundingResults = 3,
                        MaxNonWebAnswers = 0,
                        MaxNewsItems = searchType == "news" ? 2 : 0,
                        MaxWeatherItems = 0,
                        MaxFinanceItems = searchType == "stock" ? 2 : 0
                    }
                }
            };

            var searchRequest = new SearchRequest
            {
                Requests = searchRequests
            };

            var searchResponse = await proxy.SearchAsync(searchRequest);
            
            if (searchResponse?.Results == null || searchResponse.Results.Count == 0)
            {
                Console.WriteLine($"⚠️ No results found for {searchType} search: {query}");
                return new List<object>();
            }

            Console.WriteLine($"✅ Found {searchResponse.Results.Count} results for {searchType} search");

            // Build search results
            var results = searchResponse.Results.Select((item, index) => {
                string snippet = "";
                try 
                {
                    var type = item.GetType();
                    var semanticDocProp = type.GetProperty("SemanticDocument");
                    snippet = (semanticDocProp?.GetValue(item) as string) ?? "";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Error getting semanticDocument for {searchType} result {index}: {ex.Message}");
                }
                
                return new {
                    index = index,
                    title = item.Title ?? "No title",
                    url = item.Url ?? "",
                    snippet = snippet.Length > 300 ? snippet.Substring(0, 300) + "..." : snippet,
                    searchType = searchType
                };
            }).ToList();

            return results.Cast<object>().ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error in {searchType} search: {ex.Message}");
            return new List<object>();
        }
    }
    
    static List<SearchRequestItem> GenerateSearchQueries(string originalQuery)
    {
        var queries = new List<SearchRequestItem>();
        
        // Always include the original query
        queries.Add(new SearchRequestItem
        {
            Q = originalQuery,
            TopN = 2,
            Source = SearchProviders.WebWithBing,
            Language = "en",
            Market = "en-US",
            AdditionalConfig = new SearchRequestAdditionalConfig
            {
                MaxSemanticDocumentLength = 500,
                MaxGroundingResults = 2,
                MaxNonWebAnswers = 0,
                MaxNewsItems = 0,
                MaxWeatherItems = 0,
                MaxFinanceItems = 0
            }
        });
        
        return queries;
    }

    // Call the Copilot Bridge service to expand queries using GitHub Copilot
    static async Task<List<string>> CallCopilotBridgeService(string query)
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            
            var requestData = new { query = query };
            var json = JsonSerializer.Serialize(requestData);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            Console.WriteLine("🌉 Calling Copilot Bridge service...");
            var response = await client.PostAsync("http://localhost:8500/ai/expand-query", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<CopilotBridgeResponse>(responseJson);
                
                Console.WriteLine($"✅ Received {result?.expandedQueries?.Count ?? 0} expanded queries from GitHub Copilot");
                return result?.expandedQueries ?? GenerateFallbackQueries(query);
            }
            else
            {
                Console.WriteLine($"⚠️ Copilot Bridge service returned {response.StatusCode}, using fallback");
                return GenerateFallbackQueries(query);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Failed to call Copilot Bridge service: {ex.Message}, using fallback");
            return GenerateFallbackQueries(query);
        }
    }

    static List<string> GenerateFallbackQueries(string query)
    {
        var lowerQuery = query.ToLower();
        var fallbackQueries = new List<string>();
        
        if (lowerQuery.Contains("ai") || lowerQuery.Contains("artificial intelligence"))
        {
            fallbackQueries.AddRange(new[]
            {
                $"{query} latest breakthrough 2025",
                $"{query} funding investment news recent",
                $"{query} regulation policy updates",
                $"OpenAI ChatGPT {query.Replace("ai", "").Replace("artificial intelligence", "").Trim()} news",
                $"Google Gemini {query.Replace("ai", "").Replace("artificial intelligence", "").Trim()} updates",
                $"Microsoft Copilot {query.Replace("ai", "").Replace("artificial intelligence", "").Trim()}",
                $"{query} enterprise adoption trends",
                $"{query} safety ethics discussions"
            });
        }
        else if (lowerQuery.Contains("tech") || lowerQuery.Contains("technology"))
        {
            fallbackQueries.AddRange(new[]
            {
                $"{query} startup funding 2025",
                $"{query} innovation trends",
                $"{query} market analysis report",
                $"{query} company acquisitions merger",
                $"{query} latest developments",
                $"{query} industry impact",
                $"{query} future predictions",
                $"{query} research breakthrough"
            });
        }
        else
        {
            fallbackQueries.AddRange(new[]
            {
                $"{query} latest news 2025",
                $"{query} recent developments",
                $"{query} industry analysis",
                $"{query} market trends",
                $"{query} expert opinions",
                $"{query} research report",
                $"{query} company updates",
                $"{query} future outlook"
            });
        }
        
        return fallbackQueries.Take(8).ToList();
    }

    static async Task<object> PerformBatchSearchWithOpen(string originalQuery, List<string> expandedQueries)
    {
        var options = new LuminaApiOptions
        {
            Endpoint = "https://luminaserviceapi-test-westus.copilotlumina.com",
            LuminaApiTokenProvider = async () => await Task.FromResult(_oauthService.AccessToken!),
            Scenario = "AIBatchSearch",
            TrafficType = "TEST"
        };

        var proxy = new LuminaServiceApiProxy(options, _httpClientFactory!, _ => { });

        // Create batch search with original + expanded queries
        var allQueries = new List<string> { originalQuery };
        allQueries.AddRange(expandedQueries);
        
        var searchRequests = allQueries.Select((query, index) => new SearchRequestItem
        {
            Q = query,
            TopN = index == 0 ? 3 : 2, // More results for original query
            Source = SearchProviders.WebWithBing,
            Language = "en",
            Market = "en-US",
            Recency = DetermineQueryRecency(query),
            AdditionalConfig = new SearchRequestAdditionalConfig
            {
                MaxSemanticDocumentLength = 500,
                MaxGroundingResults = index == 0 ? 3 : 2,
                MaxNonWebAnswers = 0,
                MaxNewsItems = 0,
                MaxWeatherItems = 0,
                MaxFinanceItems = 0
            }
        }).ToList();

        var searchRequest = new SearchRequest
        {
            Requests = searchRequests
        };

        var searchResponse = await proxy.SearchAsync(searchRequest);
        
        if (searchResponse?.Results == null || searchResponse.Results.Count == 0)
        {
            return new { searchResults = new List<object>(), openedPage = (object?)null, queriesUsed = allQueries };
        }

        // Auto-open the first result
        object? openedPageContent = await TryOpenFirstResult(proxy, searchResponse);

        // Build comprehensive search results from all queries
        var searchResults = searchResponse.Results.Take(15).Select((item, index) => {
            string snippet = "";
            try 
            {
                var type = item.GetType();
                var semanticDocProp = type.GetProperty("SemanticDocument");
                snippet = (semanticDocProp?.GetValue(item) as string) ?? "";
                
                if (!string.IsNullOrEmpty(snippet))
                {
                    Console.WriteLine($"   Batch Result {index}: found semanticDocument with {snippet.Length} characters");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   Error getting semanticDocument for batch result {index}: {ex.Message}");
            }
            
            return new {
                index = index,
                title = item.Title ?? "",
                url = item.Url ?? "",
                snippet = snippet,
                querySource = index < 3 ? "original" : "ai-expanded"
            };
        }).ToList();

        return new { 
            searchResults, 
            openedPage = openedPageContent,
            queriesUsed = allQueries,
            totalQueries = allQueries.Count,
            aiExpansionUsed = true
        };
    }

    static int DetermineQueryRecency(string query)
    {
        var lowerQuery = query.ToLower();
        if (lowerQuery.Contains("latest") || lowerQuery.Contains("recent") || lowerQuery.Contains("breaking") || lowerQuery.Contains("2025"))
            return 7; // Recent 7 days
        if (lowerQuery.Contains("news") || lowerQuery.Contains("update"))
            return 14; // Recent 2 weeks
        return 30; // Recent month
    }

    static async Task<object?> TryOpenFirstResult(LuminaServiceApiProxy proxy, SearchResponse searchResponse)
    {
        object? openedPageContent = null;
        try
        {
            var sessionId = searchResponse.ToolState?.SessionId;
            if (!string.IsNullOrEmpty(sessionId) && searchResponse.Results.Count > 0)
            {
                var openRequest = new OpenRequest
                {
                    Requests = new List<OpenRequestItem>
                    {
                        new OpenRequestItem
                        {
                            RefId = searchResponse.Results[0].Url,
                            PageContext = new PageContextInfo
                            {
                                Action = searchResponse.PageContext.Action,
                                Turn = searchResponse.PageContext.Turn,
                                Id = 0
                            }
                        }
                    },
                    ToolState = searchResponse.ToolState
                };
                
                var openResponse = await proxy.OpenAsync(openRequest);
                if (openResponse?.Pages != null && openResponse.Pages.Count > 0)
                {
                    var page = openResponse.Pages[0];
                    var content = page.Content ?? "";
                    var isContentFiltered = content.Contains("filtered content") || 
                                          content.Contains("Failed to open") ||
                                          string.IsNullOrWhiteSpace(content);
                    
                    if (isContentFiltered && searchResponse.Results.Count > 1)
                    {
                        Console.WriteLine("⚠️ First result was filtered, trying second result...");
                        var secondOpenRequest = new OpenRequest
                        {
                            Requests = new List<OpenRequestItem>
                            {
                                new OpenRequestItem
                                {
                                    RefId = searchResponse.Results[1].Url,
                                    PageContext = new PageContextInfo
                                    {
                                        Action = searchResponse.PageContext.Action,
                                        Turn = searchResponse.PageContext.Turn,
                                        Id = 1
                                    }
                                }
                            },
                            ToolState = searchResponse.ToolState
                        };
                        
                        var secondOpenResponse = await proxy.OpenAsync(secondOpenRequest);
                        if (secondOpenResponse?.Pages != null && secondOpenResponse.Pages.Count > 0)
                        {
                            var secondPage = secondOpenResponse.Pages[0];
                            var secondContent = secondPage.Content ?? "";
                            
                            if (!secondContent.Contains("filtered content") && 
                                !secondContent.Contains("Failed to open") &&
                                !string.IsNullOrWhiteSpace(secondContent))
                            {
                                page = secondPage;
                                content = secondContent;
                                isContentFiltered = false;
                            }
                        }
                    }
                    
                    openedPageContent = new
                    {
                        url = page.Url,
                        title = page.Title ?? (isContentFiltered ? "Content Filtered" : "No title"),
                        content = isContentFiltered ? 
                            "Content was filtered or failed to load. This may be due to content restrictions." :
                            (content.Length > 1000 ? content.Substring(0, 1000) + "..." : content),
                        isFiltered = isContentFiltered,
                        pageContext = new
                        {
                            action = page.PageContext?.Action,
                            turn = page.PageContext?.Turn,
                            id = page.PageContext?.Id
                        }
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to open first result: {ex.Message}");
        }
        
        return openedPageContent;
    }
    
    static string GetAuthHtml() => """
<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <title>Lumina Search - Authentication</title>
    <style>
        body { font-family: Arial; max-width: 600px; margin: 50px auto; text-align: center; }
        button { padding: 15px 25px; font-size: 16px; background: #007acc; color: white; border: none; border-radius: 8px; cursor: pointer; margin: 10px; }
        button:hover { background: #005999; }
        #status { margin: 20px 0; font-size: 18px; }
        .success { color: green; }
        .error { color: red; }
    </style>
</head>
<body>
    <h1>🔍 Lumina Search Service</h1>
    <p>Please complete OAuth authentication to use search functionality</p>
    <br>
    <button onclick="startAuth()">Start OAuth Authentication</button>
    <div id="status"></div>
    
    <script>
        function startAuth() {
            const status = document.getElementById('status');
            status.innerHTML = '🔄 Starting authentication...';
            
            fetch('/auth/start', { method: 'POST' })
            .then(r => r.json())
            .then(data => {
                if (data.success) {
                    status.innerHTML = '<span class="success">✅ Authentication successful! Redirecting to search page...</span>';
                    // Slight delay before refreshing page to let user see success message
                    setTimeout(() => location.reload(), 1000);
                } else {
                    status.innerHTML = `<span class="error">❌ Authentication failed: ${data.error}</span>`;
                }
            })
            .catch(error => {
                status.innerHTML = `<span class="error">❌ Request failed: ${error.message}</span>`;
            });
        }
        
        // Listen for messages from authentication window
        window.addEventListener('message', function(event) {
            if (event.data === 'auth_complete') {
                location.reload();
            }
        });
    </script>
</body>
</html>
""";

    static string GetSearchHtml() => """
<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <title>Lumina Company Research</title>
    <style>
        body { font-family: Arial; margin: 0; padding: 20px; background: #f5f5f5; }
        .container { max-width: 1400px; margin: 0 auto; }
        .header { text-align: center; margin-bottom: 30px; }
        .search-box { text-align: center; margin-bottom: 30px; background: white; padding: 30px; border-radius: 12px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
        .search-box h2 { margin-top: 0; color: #333; }
        input { width: 400px; padding: 15px; font-size: 16px; border: 2px solid #ddd; border-radius: 8px; }
        button { padding: 15px 25px; font-size: 16px; background: #007acc; color: white; border: none; border-radius: 8px; cursor: pointer; margin-left: 10px; }
        button:hover { background: #005999; }
        #status { margin: 20px 0; font-size: 18px; text-align: center; }
        .success { color: green; }
        .error { color: red; }
        .results-container { display: flex; gap: 20px; margin-top: 30px; }
        .results-column { flex: 1; background: white; border-radius: 12px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); overflow: hidden; }
        .column-header { background: #007acc; color: white; padding: 20px; text-align: center; font-size: 18px; font-weight: bold; }
        .stock-header { background: #28a745; }
        .news-header { background: #dc3545; }
        .column-content { padding: 20px; min-height: 400px; }
        .result-item { 
            border: 1px solid #eee; 
            border-radius: 8px; 
            padding: 15px; 
            margin: 15px 0; 
            background: #f9f9f9; 
            transition: all 0.3s ease;
        }
        .result-item:hover {
            box-shadow: 0 4px 12px rgba(0,0,0,0.1);
            transform: translateY(-2px);
        }
        .result-title { 
            font-size: 16px; 
            font-weight: bold; 
            color: #1a0dab; 
            margin-bottom: 8px; 
        }
        .result-url { 
            color: #006621; 
            font-size: 12px; 
            margin-bottom: 8px; 
            word-break: break-all;
        }
        .result-snippet { 
            color: #545454; 
            line-height: 1.4; 
            font-size: 14px;
        }
        .result-title a { 
            color: #1a0dab; 
            text-decoration: none; 
        }
        .result-title a:hover { 
            text-decoration: underline; 
        }
        .loading-spinner {
            text-align: center;
            padding: 40px;
            color: #666;
        }
        .no-results {
            text-align: center;
            padding: 40px;
            color: #999;
            font-style: italic;
        }
        .retry-button {
            background: #17a2b8;
            color: white;
            border: none;
            padding: 8px 16px;
            border-radius: 4px;
            cursor: pointer;
            margin-top: 10px;
        }
        .retry-button:hover {
            background: #138496;
        }
        @media (max-width: 768px) {
            .results-container {
                flex-direction: column;
            }
            input {
                width: 300px;
            }
        }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1>🏢 Lumina Company Research</h1>
            <p>✅ Search for stock prices and latest news</p>
        </div>
        
        <div class="search-box">
            <h2>Enter Company Name</h2>
            <input type="text" id="companyName" placeholder="Enter company name (e.g., Microsoft, Apple, Tesla)..." />
            <button onclick="searchCompany()">Search Company</button>
        </div>
        
        <div id="status"></div>
        
        <div class="results-container" id="resultsContainer" style="display: none;">
            <div class="results-column">
                <div class="column-header stock-header">
                    📈 Stock Price Information
                </div>
                <div class="column-content" id="stockResults">
                    <div class="loading-spinner">🔄 Loading stock information...</div>
                </div>
            </div>
            
            <div class="results-column">
                <div class="column-header news-header">
                    📰 Latest News
                </div>
                <div class="column-content" id="newsResults">
                    <div class="loading-spinner">🔄 Loading latest news...</div>
                </div>
            </div>
        </div>
    </div>
    
    <script>
        function searchCompany() {
            const companyName = document.getElementById('companyName').value.trim();
            const status = document.getElementById('status');
            const resultsContainer = document.getElementById('resultsContainer');
            const stockResults = document.getElementById('stockResults');
            const newsResults = document.getElementById('newsResults');
            
            if (!companyName) {
                status.innerHTML = '<span class="error">❌ Please enter a company name</span>';
                return;
            }
            
            status.innerHTML = '🔄 Searching for stock prices and news...';
            resultsContainer.style.display = 'flex';
            stockResults.innerHTML = '<div class="loading-spinner">🔄 Loading stock information...</div>';
            newsResults.innerHTML = '<div class="loading-spinner">🔄 Loading latest news...</div>';
            
            fetch('/company-search', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ companyName })
            })
            .then(r => r.json())
            .then(data => {
                if (data.success) {
                    status.innerHTML = `✅ Found results for ${companyName}`;
                    displayCompanyResults(data.result);
                } else {
                    status.innerHTML = `❌ Search failed: ${data.error}`;
                    stockResults.innerHTML = '<div class="no-results">❌ Failed to load stock information</div>';
                    newsResults.innerHTML = '<div class="no-results">❌ Failed to load news</div>';
                }
            })
            .catch(err => {
                status.innerHTML = '❌ Network error';
                stockResults.innerHTML = '<div class="no-results">❌ Network error loading stock information</div>';
                newsResults.innerHTML = '<div class="no-results">❌ Network error loading news</div>';
            });
        }
        
        function displayCompanyResults(result) {
            const stockResults = document.getElementById('stockResults');
            const newsResults = document.getElementById('newsResults');
            
            // Display stock results
            if (result.stockResults && result.stockResults.length > 0) {
                stockResults.innerHTML = result.stockResults.map(item => `
                    <div class="result-item">
                        <div class="result-title">
                            <a href="${item.url}" target="_blank">${item.title || 'No title'}</a>
                        </div>
                        <div class="result-url">${item.url}</div>
                        <div class="result-snippet">${item.snippet || 'No summary available'}</div>
                    </div>
                `).join('');
            } else {
                stockResults.innerHTML = '<div class="no-results">📈 No stock price information found</div>';
            }
            
            // Display news results
            if (result.newsResults && result.newsResults.length > 0) {
                newsResults.innerHTML = result.newsResults.map(item => `
                    <div class="result-item">
                        <div class="result-title">
                            <a href="${item.url}" target="_blank">${item.title || 'No title'}</a>
                        </div>
                        <div class="result-url">${item.url}</div>
                        <div class="result-snippet">${item.snippet || 'No summary available'}</div>
                    </div>
                `).join('');
            } else {
                newsResults.innerHTML = '<div class="no-results">📰 No news found</div>';
            }
        }
        
        document.getElementById('companyName').onkeypress = function(e) {
            if (e.key === 'Enter') searchCompany();
        }
    </script>
</body>
</html>
""";
    }

    public class SearchData
    {
        public string? query { get; set; }
    }
}

public class CopilotBridgeResponse
{
    public string? originalQuery { get; set; }
    public List<string>? expandedQueries { get; set; }
    public string? timestamp { get; set; }
    public string? source { get; set; }
}