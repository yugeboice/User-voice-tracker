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

        public HomeController(OboTokenService oboTokenService, ILogger<HomeController> logger, Services.ApiLogService apiLogService)
        {
            _oboTokenService = oboTokenService;
            _logger = logger;
            _apiLogService = apiLogService;
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
                _apiLogService.AddLog("MSAL Auth", "GetUserToken", "Requesting OAuth 2.0 access token...");
                
                var token = await _oboTokenService.GetUserTokenAsync();
                HttpContext.Session.SetString("AccessToken", token);
                
                _apiLogService.AddLog("MSAL Auth", "GetUserToken", $"✅ Token acquired successfully. Length: {token.Length} chars");
                
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
                _apiLogService.AddLog("Lumina Search", "BatchSearch", $"Executing batch search for: '{model.Query}', TopResults: {model.TopResults}");
                
                var searchService = new LuminaSearchService(token);
                
                // Execute batch search for company
                var batchResult = await searchService.ExecuteBatchCompanySearchAsync(model.Query, model.TopResults);
                
                _apiLogService.AddLog("Lumina Search", "BatchSearch", 
                    $"✅ Found {batchResult.StockResults.Count} stock results and {batchResult.NewsResults.Count} news results");
                
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
                _apiLogService.AddLog("Lumina Search", "WebSearch", $"Executing web search for: '{model.Query}', TopResults: {model.TopResults}");
                
                var searchService = new LuminaSearchService(token);
                var results = await searchService.ExecuteWebSearchAsync(model.Query, model.TopResults);
                
                _apiLogService.AddLog("Lumina Search", "WebSearch", $"✅ Found {results.Count} results");
                
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
                _apiLogService.AddLog("Lumina Open", "OpenContent", $"Opening content from URL: {request.Url}");
                
                var searchService = new LuminaSearchService(token);
                var content = await searchService.OpenContentAsync(request.Url);
                
                _apiLogService.AddLog("Lumina Open", "OpenContent", $"✅ Content retrieved. Length: {content.Length} chars");
                
                return Json(new { success = true, content = content });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid URL parameter: {Message}", ex.Message);
                _apiLogService.AddLog("Lumina Open", "OpenContent", $"❌ Validation error: {ex.Message}", false);
                return Json(new { success = false, error = ex.Message });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error while opening content from: {Url}", request.Url);
                _apiLogService.AddLog("Lumina Open", "OpenContent", $"❌ Network error: {ex.Message}", false);
                return Json(new { success = false, error = "Network error. Please check your internet connection and try again." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Open content failed for URL: {Url}", request.Url);
                _apiLogService.AddLog("Lumina Open", "OpenContent", $"❌ Error: {ex.Message}", false);
                return Json(new { success = false, error = $"Failed to open content: {ex.Message}" });
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