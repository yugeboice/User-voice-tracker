using Microsoft.AspNetCore.Mvc;
using LuminaSearchConsole.Models;
using System.Diagnostics;

namespace LuminaSearchConsole.Controllers
{
    /// <summary>
    /// Main MVC controller handling user authentication, search operations, and content viewing.
    /// Manages session-based authentication and coordinates between OboTokenService and LuminaSearchService.
    /// </summary>
    public class HomeController : Controller
    {
        #region Dependencies

        private readonly OboTokenService _oboTokenService;
        private readonly ILogger<HomeController> _logger;
        private readonly Services.ApiLogService _apiLogService;
        private readonly Services.CuaComputerPool _cuaComputerPool;
        private readonly AzureAdConfiguration _azureAdConfig;
        private readonly LuminaConfiguration _luminaConfig;

        public HomeController(
            OboTokenService oboTokenService, 
            ILogger<HomeController> logger, 
            Services.ApiLogService apiLogService,
            Services.CuaComputerPool cuaComputerPool,
            AzureAdConfiguration azureAdConfig,
            LuminaConfiguration luminaConfig)
        {
            _oboTokenService = oboTokenService;
            _logger = logger;
            _apiLogService = apiLogService;
            _cuaComputerPool = cuaComputerPool;
            _azureAdConfig = azureAdConfig;
            _luminaConfig = luminaConfig;
        }

        #endregion

        #region Page Actions

        /// <summary>
        /// Display the home page with authentication status.
        /// Checks session for existing access token.
        /// </summary>
        public IActionResult Index()
        {
            var token = HttpContext.Session.GetString("AccessToken");
            var model = new SearchViewModel
            {
                IsAuthenticated = !string.IsNullOrEmpty(token),
                ApiLogs = _apiLogService.GetLogs()
            };
            return View(model);
        }

        #endregion

        #region Authentication Operations

        /// <summary>
        /// Authenticate user via OAuth 2.0 and store access token in session.
        /// Triggers browser-based login flow if no cached token is available.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Login()
        {
            try
            {
                _apiLogService.AddLog("MSAL Auth", "GetUserToken", 
                    "📋 OAuth 2.0 Configuration:\n" +
                    $"  TenantId: {_azureAdConfig.TenantId}\n" +
                    $"  ClientId: {_azureAdConfig.ClientId}\n" +
                    $"  RedirectUri: {_azureAdConfig.RedirectUri}\n" +
                    $"  Scopes: {_luminaConfig.ApiScopes}\n" +
                    $"  Cache: %LocalAppData%\\LuminaSearchConsole\\{_azureAdConfig.CacheFileName}\n" +
                    "  Method: Silent token acquisition (with fallback to interactive)");
                
                var token = await _oboTokenService.GetUserTokenAsync();
                HttpContext.Session.SetString("AccessToken", token);
                
                var tokenPreview = token.Length > 50 ? token.Substring(0, 50) + "..." : token;
                _apiLogService.AddLog("MSAL Auth", "GetUserToken", 
                    $"✅ Token acquired successfully\n" +
                    $"  Token length: {token.Length} chars\n" +
                    $"  Token preview: {tokenPreview}\n" +
                    $"  Storage: Session (30min timeout)");
                
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Authentication failed");
                _apiLogService.AddLog("MSAL Auth", "GetUserToken", $"❌ Error: {ex.Message}", false);
                
                string userMessage = "Login failed. Please try again.";
                if (ex.Message.Contains("AADSTS"))
                {
                    userMessage = "Authentication service error. Please try again or contact support.";
                }
                else if (ex.Message.Contains("network") || ex.Message.Contains("connection"))
                {
                    userMessage = "Network error. Please check your internet connection and try again.";
                }
                
                TempData["Error"] = userMessage;
                return RedirectToAction("Index");
            }
        }

        #endregion

        #region Search Operations

        /// <summary>
        /// Execute batch search for a company (searches stock info + latest news in one request).
        /// Demonstrates batch search pattern: combines multiple queries for efficiency.
        /// </summary>
        /// <param name="model">Search parameters including company name and result count</param>
        [HttpPost]
        public async Task<IActionResult> Search(SearchViewModel model)
        {
            if (string.IsNullOrEmpty(model.Query))
            {
                TempData["Error"] = "Please enter a search query";
                return RedirectToAction("Index");
            }

            var token = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(token))
            {
                TempData["Error"] = "Please log in first";
                return RedirectToAction("Index");
            }

            try
            {
                var stockQuery = $"{model.Query} stock price";
                var newsQuery = $"{model.Query} latest news";
                _apiLogService.AddLog("Lumina Search", "BatchSearch", 
                    $"📋 API Configuration:\n" +
                    $"  Endpoint: {_luminaConfig.ApiEndpoint}\n" +
                    $"  Method: POST /search\n" +
                    $"  Auth: Bearer token (OAuth 2.0)\n\n" +
                    $"📝 Batch Search Request:\n" +
                    $"  Company: '{model.Query}'\n" +
                    $"  Query 1 (Stock): '{stockQuery}'\n" +
                    $"  Query 2 (News): '{newsQuery}'\n\n" +
                    $"⚙️ Parameters:\n" +
                    $"  TopN per query: {model.TopResults}\n" +
                    $"  Source: WebWithBing\n" +
                    $"  Market: en-US\n" +
                    $"  Language: en\n" +
                    $"  Recency: 7 days\n" +
                    $"  MaxSemanticDocumentLength: 200");
                
                var searchService = new LuminaSearchService(token, _luminaConfig);
                
                // Execute batch search for company
                var startTime = DateTime.Now;
                var batchResult = await searchService.ExecuteBatchCompanySearchAsync(model.Query, model.TopResults);
                var duration = (DateTime.Now - startTime).TotalMilliseconds;
                
                // Build result preview with first title from each category
                var stockPreview = batchResult.StockResults.Count > 0 ? 
                    $"\n  First stock result: {batchResult.StockResults[0].Title}" : "";
                var newsPreview = batchResult.NewsResults.Count > 0 ? 
                    $"\n  First news result: {batchResult.NewsResults[0].Title}" : "";
                
                _apiLogService.AddLog("Lumina Search", "BatchSearch", 
                    $"✅ Search completed\n" +
                    $"  Stock results: {batchResult.StockResults.Count}\n" +
                    $"  News results: {batchResult.NewsResults.Count}\n" +
                    $"  Response time: {duration:F0}ms{stockPreview}{newsPreview}");
                
                // Try to extract company information from Wikipedia using Find API
                try
                {
                    // Step 1: Search for company + Wikipedia to find the actual Wikipedia URL
                    _apiLogService.AddLog("Lumina Search", "WikipediaSearch", 
                        $"📋 Searching for Wikipedia page:\n" +
                        $"  Query: '{model.Query} Wikipedia'\n" +
                        $"  Purpose: Find company's Wikipedia URL");
                    
                    var wikiSearchStart = DateTime.Now;
                    var wikipediaSearchResults = await searchService.ExecuteWebSearchAsync($"{model.Query} Wikipedia", 3);
                    var wikiSearchDuration = (DateTime.Now - wikiSearchStart).TotalMilliseconds;
                    
                    // Find Wikipedia URL from search results
                    var wikipediaUrl = wikipediaSearchResults
                        .FirstOrDefault(r => r.Url?.Contains("wikipedia.org/wiki/") == true)?.Url;
                    
                    if (!string.IsNullOrEmpty(wikipediaUrl))
                    {
                        _apiLogService.AddLog("Lumina Search", "WikipediaSearch", 
                            $"✅ Found Wikipedia URL\n" +
                            $"  URL: {wikipediaUrl}\n" +
                            $"  Response time: {wikiSearchDuration:F0}ms");
                        
                        _apiLogService.AddLog("Lumina Find", "ExtractCompanyInfo", 
                            $"📋 Attempting to extract company information:\n" +
                            $"  URL: {wikipediaUrl}\n" +
                            $"  Company: {model.Query}\n" +
                            $"  Method: Open API + Find API\n" +
                            $"  Search patterns: 'founded', 'headquarters', 'revenue', 'employees'");
                        
                        var infoStartTime = DateTime.Now;
                        var companyInfo = await searchService.ExtractCompanyInfoAsync(wikipediaUrl);
                        var infoDuration = (DateTime.Now - infoStartTime).TotalMilliseconds;
                        
                        if (companyInfo != null && companyInfo.Fields.Any())
                        {
                            var fieldsSummary = string.Join("\n  ", 
                                companyInfo.Fields.Take(3).Select(f => $"{f.FieldName}: {f.Content.Substring(0, Math.Min(100, f.Content.Length))}"));
                            
                            _apiLogService.AddLog("Lumina Find", "ExtractCompanyInfo", 
                                $"✅ Company information extracted from Wikipedia\n" +
                                $"  Total fields: {companyInfo.Fields.Count}\n" +
                                $"  Response time: {infoDuration:F0}ms\n" +
                                $"  Sample fields:\n  {fieldsSummary}");
                            
                            model.CompanyInfo = companyInfo;
                        }
                        else
                        {
                            _apiLogService.AddLog("Lumina Find", "ExtractCompanyInfo", 
                                $"⚠️ No company information found\n" +
                                $"  Response time: {infoDuration:F0}ms");
                        }
                    }
                    else
                    {
                        _apiLogService.AddLog("Lumina Search", "WikipediaSearch", 
                            $"⚠️ No Wikipedia URL found in search results\n" +
                            $"  Response time: {wikiSearchDuration:F0}ms");
                    }
                }
                catch (Exception findEx)
                {
                    _logger.LogWarning(findEx, "Failed to extract company info using Find API");
                    _apiLogService.AddLog("Lumina Find", "ExtractCompanyInfo", 
                        $"⚠️ Could not extract company information: {findEx.Message}", false);
                    // Continue even if Find API fails
                }
                
                model.BatchSearchResult = batchResult;
                model.IsBatchSearch = true;
                model.IsAuthenticated = true;
                model.HasSearched = true;
                model.ApiLogs = _apiLogService.GetLogs();
                
                return View("Index", model);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid search parameter: {Message}", ex.Message);
                _apiLogService.AddLog("Lumina Search", "BatchSearch", $"❌ Validation error: {ex.Message}", false);
                TempData["Error"] = ex.Message;
                model.IsAuthenticated = true;
                model.ApiLogs = _apiLogService.GetLogs();
                return View("Index", model);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error during batch search for: {Query}", model.Query);
                _apiLogService.AddLog("Lumina Search", "BatchSearch", $"❌ Network error: {ex.Message}", false);
                TempData["Error"] = "Network error. Please check your internet connection and try again.";
                model.IsAuthenticated = true;
                model.ApiLogs = _apiLogService.GetLogs();
                return View("Index", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch search failed for query: {Query}", model.Query);
                _apiLogService.AddLog("Lumina Search", "BatchSearch", $"❌ Error: {ex.Message}", false);
                TempData["Error"] = $"Search failed: {ex.Message}";
                model.IsAuthenticated = true;
                model.ApiLogs = _apiLogService.GetLogs();
                return View("Index", model);
            }
        }

        /// <summary>
        /// Execute simple web search (general purpose search).
        /// Returns clean list of results ready for display.
        /// </summary>
        /// <param name="model">Search parameters including query text and result count</param>
        [HttpPost]
        public async Task<IActionResult> SimpleSearch(SearchViewModel model)
        {
            if (string.IsNullOrEmpty(model.Query))
            {
                TempData["Error"] = "Please enter a search query";
                return RedirectToAction("Index");
            }

            var token = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(token))
            {
                TempData["Error"] = "Please log in first";
                return RedirectToAction("Index");
            }

            try
            {
                _apiLogService.AddLog("Lumina Search", "WebSearch", 
                    $"📋 API Configuration:\n" +
                    $"  Endpoint: {_luminaConfig.ApiEndpoint}\n" +
                    $"  Method: POST /search\n" +
                    $"  Auth: Bearer token (OAuth 2.0)\n\n" +
                    $"📝 Web Search Request:\n" +
                    $"  Query: '{model.Query}'\n" +
                    $"  TopN: {model.TopResults}\n" +
                    $"  Source: WebWithBing\n" +
                    $"  Market: en-US\n" +
                    $"  Language: en");
                
                var searchService = new LuminaSearchService(token, _luminaConfig);
                var startTime = DateTime.Now;
                var results = await searchService.ExecuteWebSearchAsync(model.Query, model.TopResults);
                var duration = (DateTime.Now - startTime).TotalMilliseconds;
                
                var firstResult = results.Count > 0 ? $"\n  First result: {results[0].Title}" : "";
                
                _apiLogService.AddLog("Lumina Search", "WebSearch", 
                    $"✅ Search completed\n" +
                    $"  Results found: {results.Count}\n" +
                    $"  Response time: {duration:F0}ms{firstResult}");
                
                model.SearchResults = results;
                model.IsBatchSearch = false;
                model.IsAuthenticated = true;
                model.HasSearched = true;
                model.ApiLogs = _apiLogService.GetLogs();
                
                return View("Index", model);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid search parameter: {Message}", ex.Message);
                _apiLogService.AddLog("Lumina Search", "WebSearch", $"❌ Validation error: {ex.Message}", false);
                TempData["Error"] = ex.Message;
                model.IsAuthenticated = true;
                model.ApiLogs = _apiLogService.GetLogs();
                return View("Index", model);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error during simple search for: {Query}", model.Query);
                _apiLogService.AddLog("Lumina Search", "WebSearch", $"❌ Network error: {ex.Message}", false);
                TempData["Error"] = "Network error. Please check your internet connection and try again.";
                model.IsAuthenticated = true;
                model.ApiLogs = _apiLogService.GetLogs();
                return View("Index", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Simple search failed for query: {Query}", model.Query);
                _apiLogService.AddLog("Lumina Search", "WebSearch", $"❌ Error: {ex.Message}", false);
                TempData["Error"] = $"Search failed: {ex.Message}";
                model.IsAuthenticated = true;
                model.ApiLogs = _apiLogService.GetLogs();
                return View("Index", model);
            }
        }

        #endregion

        #region Content Operations

        /// <summary>
        /// Open and extract full content from a URL using Lumina Open API.
        /// Called when user clicks a search result to view full content.
        /// Returns JSON response for AJAX requests.
        /// </summary>
        /// <param name="request">Request containing URL to extract content from</param>
        [HttpPost]
        public async Task<IActionResult> OpenContent([FromBody] OpenContentRequest request)
        {
            if (string.IsNullOrEmpty(request.Url))
            {
                return Json(new { success = false, error = "URL is required" });
            }

            var token = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(token))
            {
                return Json(new { success = false, error = "Please log in first" });
            }

            try
            {
                _apiLogService.AddLog("Lumina Open", "OpenContent", 
                    $"📋 API Configuration:\n" +
                    $"  Endpoint: {_luminaConfig.ApiEndpoint}\n" +
                    $"  Method: POST /open\n" +
                    $"  Auth: Bearer token (OAuth 2.0)\n\n" +
                    $"📝 Open Request:\n" +
                    $"  RefId (URL): {request.Url}\n" +
                    $"  Purpose: Extract full page content");
                
                var searchService = new LuminaSearchService(token, _luminaConfig);
                var startTime = DateTime.Now;
                var content = await searchService.OpenContentAsync(request.Url);
                var duration = (DateTime.Now - startTime).TotalMilliseconds;
                
                var contentPreview = content.Length > 100 ? content.Substring(0, 100) + "..." : content;
                
                _apiLogService.AddLog("Lumina Open", "OpenContent", 
                    $"✅ Content retrieved\n" +
                    $"  Content length: {content.Length} chars\n" +
                    $"  Response time: {duration:F0}ms\n" +
                    $"  Preview: {contentPreview}");
                
                return Json(new { success = true, content = content, logsUpdated = true });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid URL parameter: {Message}", ex.Message);
                _apiLogService.AddLog("Lumina Open", "OpenContent", $"❌ Validation error: {ex.Message}", false);
                return Json(new { success = false, error = ex.Message, logsUpdated = true });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error while opening content from: {Url}", request.Url);
                _apiLogService.AddLog("Lumina Open", "OpenContent", $"❌ Network error: {ex.Message}", false);
                return Json(new { success = false, error = "Network error. Please check your internet connection and try again.", logsUpdated = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Open content failed for URL: {Url}", request.Url);
                _apiLogService.AddLog("Lumina Open", "OpenContent", $"❌ Error: {ex.Message}", false);
                return Json(new { success = false, error = $"Failed to open content: {ex.Message}", logsUpdated = true });
            }
        }

        /// <summary>
        /// Sign out user, clear MSAL cache, and destroy session.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            try
            {
                await _oboTokenService.SignOutAsync();
                HttpContext.Session.Clear();
                TempData["Message"] = "Successfully logged out!";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Logout failed");
                TempData["Error"] = $"Logout failed: {ex.Message}";
            }
            
            return RedirectToAction("Index");
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Get updated API logs as HTML for AJAX refresh
        /// </summary>
        [HttpGet]
        public IActionResult GetLogs()
        {
            var logs = _apiLogService.GetLogs();
            
            if (logs == null || !logs.Any())
            {
                return Json(new { success = true, logsHtml = "<p class='text-muted'>No API calls logged yet.</p>" });
            }

            var logsHtml = new System.Text.StringBuilder();
            logsHtml.AppendLine("<div class='api-logs'>");

            foreach (var log in logs)
            {
                var statusClass = log.Success ? "success" : "danger";
                var statusIcon = log.Success ? "✅" : "❌";
                
                logsHtml.AppendLine($@"
                <div class='log-entry log-{statusClass}'>
                    <div class='log-header'>
                        <span class='log-icon'>{statusIcon}</span>
                        <span class='log-time'>{log.Timestamp:HH:mm:ss.fff}</span>
                        <span class='log-api badge bg-{statusClass}'>{System.Web.HttpUtility.HtmlEncode(log.ApiName)}</span>
                        <span class='log-operation'>{System.Web.HttpUtility.HtmlEncode(log.Operation)}</span>
                    </div>
                    <div class='log-details'>{System.Web.HttpUtility.HtmlEncode(log.Details)}</div>
                </div>");
            }

            logsHtml.AppendLine("</div>");
            
            return Json(new { success = true, logsHtml = logsHtml.ToString(), logs = logs });
        }

        /// <summary>
        /// Error page for unhandled exceptions
        /// </summary>
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        #endregion

        #region Computer Use Agent (CUA) Operations

        /// <summary>
        /// Test CUA by navigating to Bing search and getting a screenshot
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> TestCua([FromBody] TestCuaRequest request)
        {
            var token = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(token))
            {
                return Json(new { success = false, error = "Please log in first" });
            }

            var cuaService = new LuminaCuaService(token, _luminaConfig);
            
            // Handle different steps
            string step = request?.Step?.ToLower() ?? string.Empty;

            try
            {
                // Step 1: Initialize
                if (step == "initialize")
                {
                    if (string.IsNullOrWhiteSpace(request?.CompanyName))
                    {
                        return Json(new { success = false, error = "Company name is required" });
                    }

                    var computerId = Guid.NewGuid().ToString("N");
                    
                    _apiLogService.AddLog("Lumina CUA", "Step 1: Initialize", 
                        $"📋 Initializing virtual computer\n" +
                        $"  ComputerId: {computerId}\n" +
                        $"  TenantId: {_azureAdConfig.TenantId}");

                    var startTime = DateTime.Now;
                    await cuaService.InitializeComputerAsync(computerId, "user-from-token", _azureAdConfig.TenantId);
                    var duration = (DateTime.Now - startTime).TotalMilliseconds;

                    _apiLogService.AddLog("Lumina CUA", "Step 1: Initialize", 
                        $"✅ Virtual computer initialized\n" +
                        $"  ComputerId: {computerId}\n" +
                        $"  Response time: {duration:F0}ms");

                    // Store computerId in session for next steps
                    HttpContext.Session.SetString($"CUA_Computer_{computerId}", computerId);

                    return Json(new { success = true, sessionId = computerId });
                }
                
                // Step 2: Navigate
                else if (step == "navigate")
                {
                    var sessionId = request?.SessionId;
                    if (string.IsNullOrWhiteSpace(sessionId))
                    {
                        return Json(new { success = false, error = "Session ID is required" });
                    }

                    var url = request?.Url;
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        return Json(new { success = false, error = "URL is required" });
                    }

                    _apiLogService.AddLog("Lumina CUA", "Step 2: Navigate", 
                        $"📋 Navigating to URL\n" +
                        $"  ComputerId: {sessionId}\n" +
                        $"  URL: {url}");

                    var startTime = DateTime.Now;
                    await cuaService.NavigateToUrlAsync(sessionId, url);
                    var duration = (DateTime.Now - startTime).TotalMilliseconds;

                    _apiLogService.AddLog("Lumina CUA", "Step 2: Navigate", 
                        $"✅ Navigation completed\n" +
                        $"  Response time: {duration:F0}ms");

                    return Json(new { success = true });
                }
                
                // Step 3: Screenshot
                else if (step == "screenshot")
                {
                    var sessionId = request?.SessionId;
                    if (string.IsNullOrWhiteSpace(sessionId))
                    {
                        return Json(new { success = false, error = "Session ID is required" });
                    }

                    _apiLogService.AddLog("Lumina CUA", "Step 3: Screenshot", 
                        $"📋 Capturing screenshot\n" +
                        $"  ComputerId: {sessionId}");

                    var startTime = DateTime.Now;
                    var screenshot = await cuaService.GetComputerScreenshotAsync(sessionId);
                    var duration = (DateTime.Now - startTime).TotalMilliseconds;

                    _apiLogService.AddLog("Lumina CUA", "Step 3: Screenshot", 
                        $"✅ Screenshot captured\n" +
                        $"  Resolution: {screenshot.Content?.Width}x{screenshot.Content?.Height}\n" +
                        $"  Response time: {duration:F0}ms");

                    // Clean up session
                    HttpContext.Session.Remove($"CUA_Computer_{sessionId}");

                    // Release computer in background (don't wait for it)
                    var cuaServiceForRelease = new LuminaCuaService(token, _luminaConfig);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await cuaServiceForRelease.ReleaseComputerAsync(sessionId);
                            _apiLogService.AddLog("Lumina CUA", "Cleanup", 
                                $"✅ Computer released\n" +
                                $"  ComputerId: {sessionId}");
                        }
                        catch (Exception releaseEx)
                        {
                            _logger.LogWarning(releaseEx, "Failed to release computer {ComputerId}", sessionId);
                        }
                    });

                    return Json(new 
                    { 
                        success = true, 
                        screenshot = $"data:image/png;base64,{screenshot.Content?.Screenshot}",
                        width = screenshot.Content?.Width,
                        height = screenshot.Content?.Height
                    });
                }
                
                // Legacy: All-in-one execution (backward compatibility)
                else
                {
                    if (string.IsNullOrWhiteSpace(request?.CompanyName))
                    {
                        return Json(new { success = false, error = "Company name is required" });
                    }

                    var computerId = Guid.NewGuid().ToString("N");
                    var searchUrl = $"https://www.bing.com/search?q={Uri.EscapeDataString(request.CompanyName + " stock price")}";

                    try
                    {
                _apiLogService.AddLog("Lumina CUA", "Initialize", 
                    $"📋 CUA Configuration:\n" +
                    $"  Endpoint: {_luminaConfig.ApiEndpoint}\n" +
                    $"  Method: POST /api/agent/computer/initialize\n" +
                    $"  ComputerId: {computerId}\n" +
                    $"  UserId: From token\n" +
                    $"  TenantId: {_azureAdConfig.TenantId}");

                // Initialize computer
                var startTime = DateTime.Now;
                await cuaService.InitializeComputerAsync(computerId, "user-from-token", _azureAdConfig.TenantId);
                var duration = (DateTime.Now - startTime).TotalMilliseconds;

                _apiLogService.AddLog("Lumina CUA", "Initialize", 
                    $"✅ Virtual computer initialized\n" +
                    $"  ComputerId: {computerId}\n" +
                    $"  Response time: {duration:F0}ms");

                // Navigate to Bing search results
                _apiLogService.AddLog("Lumina CUA", "Navigate", 
                    $"📋 Navigation Request:\n" +
                    $"  URL: {searchUrl}\n" +
                    $"  Query: {request.CompanyName} stock price\n" +
                    $"  Actions: Ctrl+L → Type → Enter → Wait");

                startTime = DateTime.Now;
                await cuaService.NavigateToUrlAsync(computerId, searchUrl);
                duration = (DateTime.Now - startTime).TotalMilliseconds;

                _apiLogService.AddLog("Lumina CUA", "Navigate", 
                    $"✅ Navigation completed\n" +
                    $"  Response time: {duration:F0}ms");

                // Get screenshot
                _apiLogService.AddLog("Lumina CUA", "Screenshot", 
                    $"📋 Screenshot Request:\n" +
                    $"  ComputerId: {computerId}");

                startTime = DateTime.Now;
                var screenshot = await cuaService.GetComputerScreenshotAsync(computerId);
                duration = (DateTime.Now - startTime).TotalMilliseconds;

                _apiLogService.AddLog("Lumina CUA", "Screenshot", 
                    $"✅ Screenshot captured\n" +
                    $"  Resolution: {screenshot.Content?.Width}x{screenshot.Content?.Height}\n" +
                    $"  Status: {screenshot.Status}\n" +
                    $"  Response time: {duration:F0}ms");

                        // Return screenshot as base64 image
                        return Json(new 
                        { 
                            success = true, 
                            screenshot = $"data:image/png;base64,{screenshot.Content?.Screenshot}",
                            width = screenshot.Content?.Width,
                            height = screenshot.Content?.Height
                        });
                    }
                    catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.InsufficientStorage)
                    {
                        _logger.LogWarning(httpEx, "CUA service capacity reached");
                        _apiLogService.AddLog("Lumina CUA", "Error", 
                            $"❌ Service Unavailable\n" +
                            $"  Reason: CUA service is at capacity\n" +
                            $"  Status: 507 Insufficient Storage", false);
                        
                        return Json(new { 
                            success = false, 
                            error = "CUA service is currently at capacity. Please try again later." 
                        });
                    }
                    finally
                    {
                        // Always release computer resources
                        try
                        {
                            await cuaService.ReleaseComputerAsync(computerId);
                            _apiLogService.AddLog("Lumina CUA", "Release", 
                                $"✅ Computer released\n" +
                                $"  ComputerId: {computerId}");
                        }
                        catch (Exception releaseEx)
                        {
                            _logger.LogWarning(releaseEx, "Failed to release computer {ComputerId}", computerId);
                        }
                    }
                }
            }
            catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.InsufficientStorage)
            {
                _logger.LogWarning(httpEx, "CUA service capacity reached");
                _apiLogService.AddLog("Lumina CUA", "Error", 
                    $"❌ Service Unavailable\n" +
                    $"  Reason: CUA service is at capacity\n" +
                    $"  Status: 507 Insufficient Storage", false);
                
                return Json(new { 
                    success = false, 
                    error = "CUA service is currently at capacity. Virtual computers are unavailable. Please try again later." 
                });
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError(httpEx, "CUA HTTP request failed");
                var statusCode = httpEx.StatusCode.HasValue ? $"{(int)httpEx.StatusCode} {httpEx.StatusCode}" : "Unknown";
                _apiLogService.AddLog("Lumina CUA", "Error", 
                    $"❌ HTTP Error\n" +
                    $"  Status: {statusCode}\n" +
                    $"  Message: {httpEx.Message}", false);
                
                return Json(new { success = false, error = httpEx.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CUA test failed");
                _apiLogService.AddLog("Lumina CUA", "Error", $"❌ Error: {ex.Message}", false);
                return Json(new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Test CUA with MSN Money - SSE streaming endpoint for real-time progress
        /// Uses computer pool for resource reuse (3-minute keep-alive)
        /// </summary>
        [HttpGet]
        public async Task TestCuaMsnMoneyStream(string companyName)
        {
            Response.ContentType = "text/event-stream";
            Response.Headers.Add("Cache-Control", "no-cache");
            Response.Headers.Add("Connection", "keep-alive");

            var token = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(token))
            {
                await SendSseMessage("error", "Please log in first");
                return;
            }

            if (string.IsNullOrWhiteSpace(companyName))
            {
                await SendSseMessage("error", "Company name is required");
                return;
            }

            var cuaService = new LuminaCuaService(token, _luminaConfig);
            var userId = "user-from-token"; // Could be extracted from token claims in production
            string? computerId = null;

            try
            {
                // Step 1: Get or create computer from pool
                await SendSseMessage("progress", "🔄 Initialize - Getting virtual computer...");
                await Response.Body.FlushAsync();
                
                var startTime = DateTime.Now;
                computerId = await _cuaComputerPool.GetOrCreateComputerAsync(userId, _azureAdConfig.TenantId, cuaService);
                var duration = (DateTime.Now - startTime).TotalMilliseconds;

                var poolStats = _cuaComputerPool.GetStatistics();
                _apiLogService.AddLog("Lumina CUA - MSN Money", "Initialize", 
                    $"✅ Virtual computer ready\n  ComputerId: {computerId}\n  Response time: {duration:F0}ms\n  Pool: {poolStats.TotalComputers} computers ({poolStats.ActiveComputers} active)");

                await SendSseMessage("progress", $"✅ Initialize completed ({duration:F0}ms) [Reused computer]");
                await Response.Body.FlushAsync();
                await Task.Delay(300);

                // Step 2: Navigate to MSN Money
                await SendSseMessage("progress", "🌐 Navigate - Opening https://www.msn.com/en-us/money/...");
                await Response.Body.FlushAsync();

                startTime = DateTime.Now;
                
                // Navigate to MSN Money
                await cuaService.NavigateToUrlAsync(computerId, "https://www.msn.com/en-us/money/");
                await Task.Delay(2000); // Wait for page load
                
                duration = (DateTime.Now - startTime).TotalMilliseconds;
                await SendSseMessage("progress", $"✅ Navigate completed ({duration:F0}ms)");
                await Response.Body.FlushAsync();
                await Task.Delay(300);

                // Step 3: Click search box
                await SendSseMessage("progress", "🖱️ Click - Clicking search box at (1203, 43)...");
                await Response.Body.FlushAsync();
                await Task.Delay(300);

                // Step 4: Type company name
                await SendSseMessage("progress", $"⌨️ Type - Typing \"{companyName}\"...");
                await Response.Body.FlushAsync();
                await Task.Delay(400);

                // Step 5: Press Enter
                await SendSseMessage("progress", "⏎ Keypress - Pressing Enter...");
                await Response.Body.FlushAsync();
                await Task.Delay(300);

                // Step 6: Wait for page load
                await SendSseMessage("progress", "⏱️ Wait - Waiting for page to load...");
                await Response.Body.FlushAsync();

                // Perform the search actions
                var actions = new List<CuaAction>
                {
                    new CuaAction { Action = "click", X = 1203, Y = 43, Button = 1 },
                    new CuaAction { Action = "type", Text = companyName },
                    new CuaAction { Action = "keypress", Keys = new[] { "enter" } },
                    new CuaAction { Action = "wait" }
                };
                await cuaService.PerformComputerActionsAsync(computerId, actions, actionDelayMs: 800);

                _apiLogService.AddLog("Lumina CUA - MSN Money", "Search", 
                    $"✅ Search completed for '{companyName}'");

                await Task.Delay(500);

                // Step 7: Get screenshot
                await SendSseMessage("progress", "📸 GetScreenshot - Capturing screenshot...");
                await Response.Body.FlushAsync();

                startTime = DateTime.Now;
                var screenshot = await cuaService.GetComputerScreenshotAsync(computerId);
                duration = (DateTime.Now - startTime).TotalMilliseconds;

                _apiLogService.AddLog("Lumina CUA - MSN Money", "Screenshot", 
                    $"✅ Screenshot captured\n  Resolution: {screenshot.Content?.Width}x{screenshot.Content?.Height}\n  Response time: {duration:F0}ms");

                await SendSseMessage("progress", $"✅ GetScreenshot completed ({duration:F0}ms)");
                await Response.Body.FlushAsync();
                await Task.Delay(500);

                // Send screenshot data
                await SendSseMessage("screenshot", System.Text.Json.JsonSerializer.Serialize(new
                {
                    image = $"data:image/png;base64,{screenshot.Content?.Screenshot}",
                    width = screenshot.Content?.Width,
                    height = screenshot.Content?.Height
                }));
                await Response.Body.FlushAsync();

                // Mark computer as used (extends keep-alive time)
                _cuaComputerPool.TouchComputer(userId, _azureAdConfig.TenantId);
                _apiLogService.AddLog("Lumina CUA - MSN Money", "Cleanup", 
                    $"✅ Computer kept alive for reuse (will auto-release after 3 minutes of inactivity)\n  ComputerId: {computerId}");

                await SendSseMessage("complete", "✅ All operations completed successfully!");
            }
            catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.InsufficientStorage)
            {
                _logger.LogWarning(httpEx, "CUA service capacity reached");
                await SendSseMessage("error", "CUA service is currently at capacity. Please try again later.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CUA MSN Money test failed");
                await SendSseMessage("error", ex.Message);
            }
        }

        private async Task SendSseMessage(string eventType, string data)
        {
            var message = $"event: {eventType}\ndata: {data}\n\n";
            await Response.WriteAsync(message);
        }

        /// <summary>
        /// Test CUA with MSN Money - Search for company stock information (Legacy POST endpoint)
        /// Uses computer pool for resource reuse (3-minute keep-alive)
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> TestCuaMsnMoney([FromBody] TestCuaRequest request)
        {
            var token = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(token))
            {
                return Json(new { success = false, error = "Please log in first" });
            }

            if (string.IsNullOrWhiteSpace(request?.CompanyName))
            {
                return Json(new { success = false, error = "Company name is required" });
            }

            var cuaService = new LuminaCuaService(token, _luminaConfig);
            var userId = "user-from-token";
            string? computerId = null;
            var statusLog = new System.Text.StringBuilder();

            try
            {
                // Step 1: Initialize computer
                statusLog.AppendLine($"� Initializing virtual computer...");
                statusLog.AppendLine($"   ComputerId: {computerId}");
                
                var startTime = DateTime.Now;
                if (computerId == null) computerId = Guid.NewGuid().ToString("N");
                await cuaService.InitializeComputerAsync(computerId, "user-from-token", _azureAdConfig.TenantId);
                var duration = (DateTime.Now - startTime).TotalMilliseconds;

                statusLog.AppendLine($"✅ Computer initialized ({duration:F0}ms)");
                statusLog.AppendLine();

                _apiLogService.AddLog("Lumina CUA - MSN Money", "Initialize", 
                    $"✅ Virtual computer initialized\n" +
                    $"  ComputerId: {computerId}\n" +
                    $"  Response time: {duration:F0}ms");

                // Step 2: Navigate to MSN Money
                statusLog.AppendLine($"🌐 Opening MSN Money...");
                statusLog.AppendLine($"   URL: https://www.msn.com/en-us/money/");
                
                startTime = DateTime.Now;
                await cuaService.SearchCompanyOnMsnMoneyAsync(computerId, request.CompanyName);
                duration = (DateTime.Now - startTime).TotalMilliseconds;

                statusLog.AppendLine($"✅ Navigated and searched for '{request.CompanyName}' ({duration:F0}ms)");
                statusLog.AppendLine();

                _apiLogService.AddLog("Lumina CUA - MSN Money", "Search", 
                    $"✅ Search completed for '{request.CompanyName}'\n" +
                    $"  Response time: {duration:F0}ms");

                // Step 3: Get screenshot
                statusLog.AppendLine($"📸 Capturing screenshot...");
                
                startTime = DateTime.Now;
                var screenshot = await cuaService.GetComputerScreenshotAsync(computerId);
                duration = (DateTime.Now - startTime).TotalMilliseconds;

                statusLog.AppendLine($"✅ Screenshot captured: {screenshot.Content?.Width}x{screenshot.Content?.Height} ({duration:F0}ms)");
                statusLog.AppendLine();

                _apiLogService.AddLog("Lumina CUA - MSN Money", "Screenshot", 
                    $"✅ Screenshot captured\n" +
                    $"  Resolution: {screenshot.Content?.Width}x{screenshot.Content?.Height}\n" +
                    $"  Response time: {duration:F0}ms");

                // Keep computer alive for reuse
                statusLog.AppendLine($"♻️ Computer kept alive for reuse (3-minute keep-alive)...");
                _cuaComputerPool.TouchComputer(userId, _azureAdConfig.TenantId);
                _apiLogService.AddLog("Lumina CUA - MSN Money", "Cleanup", 
                    $"✅ Computer kept alive for reuse (will auto-release after 3 minutes of inactivity)\n  ComputerId: {computerId}");

                statusLog.AppendLine($"✅ All operations completed successfully!");

                return Json(new 
                { 
                    success = true, 
                    screenshot = $"data:image/png;base64,{screenshot.Content?.Screenshot}",
                    width = screenshot.Content?.Width,
                    height = screenshot.Content?.Height,
                    statusLog = statusLog.ToString()
                });
            }
            catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.InsufficientStorage)
            {
                _logger.LogWarning(httpEx, "CUA service capacity reached");
                statusLog.AppendLine($"❌ CUA Service Unavailable");
                statusLog.AppendLine($"   Reason: Virtual computers at capacity");
                
                _apiLogService.AddLog("Lumina CUA - MSN Money", "Error", 
                    $"⚠️ CUA Service Unavailable\n" +
                    $"  Reason: Virtual computers at capacity\n" +
                    $"  Message: {httpEx.Message}", false);
                
                return Json(new { success = false, error = "CUA service is currently at capacity. Please try again later.", statusLog = statusLog.ToString() });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CUA MSN Money test failed");
                statusLog.AppendLine($"❌ Error: {ex.Message}");
                
                _apiLogService.AddLog("Lumina CUA - MSN Money", "Error", $"❌ Error: {ex.Message}", false);
                return Json(new { success = false, error = ex.Message, statusLog = statusLog.ToString() });
            }
        }

        #endregion
    }
}