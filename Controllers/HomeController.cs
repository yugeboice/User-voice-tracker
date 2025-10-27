using Microsoft.AspNetCore.Mvc;
using LuminaSearchConsole.Models;
using LuminaSearchConsole.Configuration;
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
        private readonly AzureAdConfiguration _azureAdConfig;
        private readonly LuminaConfiguration _luminaConfig;

        public HomeController(
            OboTokenService oboTokenService, 
            ILogger<HomeController> logger, 
            Services.ApiLogService apiLogService,
            AzureAdConfiguration azureAdConfig,
            LuminaConfiguration luminaConfig)
        {
            _oboTokenService = oboTokenService;
            _logger = logger;
            _apiLogService = apiLogService;
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
                
                TempData["Message"] = "Successfully logged in! You can now search.";
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
            
            return Json(new { success = true, logsHtml = logsHtml.ToString() });
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
    }
}