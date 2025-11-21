using Microsoft.AspNetCore.Mvc;
using LuminaSearchConsole.Models;
using LuminaSearchConsole.Services;
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
        private readonly ApiLogService _apiLogService;
        private readonly AzureAdConfiguration _azureAdConfig;
        private readonly LuminaConfiguration _luminaConfig;

        public HomeController(
            OboTokenService oboTokenService, 
            ILogger<HomeController> logger, 
            ApiLogService apiLogService,
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

        #region Helper Methods

        /// <summary>
        /// Set ViewBag properties before returning a view
        /// </summary>
        private void SetViewBagProperties()
        {
            var token = HttpContext.Session.GetString("AccessToken");
            ViewBag.IsAuthenticated = !string.IsNullOrEmpty(token);
        }

        #endregion

        #region Page Actions

        /// <summary>
        /// Display the home page with authentication status.
        /// Checks session for existing access token.
        /// </summary>
        public IActionResult Index()
        {
            SetViewBagProperties();
            
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
                    "  Method: Silent token acquisition (with fallback to interactive)", true, "Authentication");
                
                var token = await _oboTokenService.GetUserTokenAsync();
                HttpContext.Session.SetString("AccessToken", token);
                
                var tokenPreview = token.Length > 50 ? token.Substring(0, 50) + "..." : token;
                _apiLogService.AddLog("MSAL Auth", "GetUserToken", 
                    $"✅ Token acquired successfully\n" +
                    $"  Token length: {token.Length} chars\n" +
                    $"  Token preview: {tokenPreview}\n" +
                    $"  Storage: Session (30min timeout)", true, "Authentication");
                
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                                _apiLogService.AddLog("MSAL Auth", "GetUserToken", $"❌ Error: {ex.Message}", false, "Authentication");
                
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
                var searchService = new LuminaSearchService(token, _luminaConfig);
                var batchResult = await searchService.ExecuteBatchCompanySearchAsync(model.Query, model.TopResults, _apiLogService, "Stock & market data + recent news");
                
                // Note: Company info will be loaded separately via AJAX call to FindCompanyInfo
                
                model.BatchSearchResult = batchResult;
                model.IsBatchSearch = true;
                model.IsAuthenticated = true;
                model.HasSearched = true;
                model.ApiLogs = _apiLogService.GetLogs();
                
                SetViewBagProperties();
                return View("Index", model);
            }
            catch (ArgumentException ex)
            {
                TempData["Error"] = ex.Message;
                model.IsAuthenticated = true;
                model.ApiLogs = _apiLogService.GetLogs();
                SetViewBagProperties();
                return View("Index", model);
            }
            catch (HttpRequestException)
            {
                TempData["Error"] = "Network error. Please check your internet connection and try again.";
                model.IsAuthenticated = true;
                model.ApiLogs = _apiLogService.GetLogs();
                SetViewBagProperties();
                return View("Index", model);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Search failed: {ex.Message}";
                model.IsAuthenticated = true;
                model.ApiLogs = _apiLogService.GetLogs();
                SetViewBagProperties();
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
                var searchService = new LuminaSearchService(token, _luminaConfig);
                var searchResults = await searchService.ExecuteWebSearchAsync(model.Query, model.TopResults, null, _apiLogService, "Web Search");
                
                model.SearchResults = searchResults;
                model.IsBatchSearch = false;
                model.IsAuthenticated = true;
                model.HasSearched = true;
                model.ApiLogs = _apiLogService.GetLogs();
                
                SetViewBagProperties();
                return View("Index", model);
            }
            catch (ArgumentException ex)
            {
                TempData["Error"] = ex.Message;
                model.IsAuthenticated = true;
                model.ApiLogs = _apiLogService.GetLogs();
                SetViewBagProperties();
                return View("Index", model);
            }
            catch (HttpRequestException)
            {
                TempData["Error"] = "Network error. Please check your internet connection and try again.";
                model.IsAuthenticated = true;
                model.ApiLogs = _apiLogService.GetLogs();
                SetViewBagProperties();
                return View("Index", model);
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Search failed: {ex.Message}";
                model.IsAuthenticated = true;
                model.ApiLogs = _apiLogService.GetLogs();
                SetViewBagProperties();
                return View("Index", model);
            }
        }

        #endregion

        #region Find API Operations

        /// <summary>
        /// Extract company information from Wikipedia using Find API.
        /// This runs independently from batch search for better performance.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> FindCompanyInfo([FromBody] RetryFindRequest request)
        {
            if (string.IsNullOrEmpty(request.CompanyName))
            {
                return Json(new { success = false, error = "Company name is required" });
            }

            var token = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(token))
            {
                return Json(new { success = false, error = "Please log in first" });
            }

            try
            {
                // Step 1: Search Wikipedia for company page
                _apiLogService.AddLog("Lumina Search API", "POST /api/sonicberry/search (Step 1/2)", 
                    $"Parameters\n" +
                    $"{{\n" +
                    $"  \"q\": \"{request.CompanyName} Wikipedia\",\n" +
                    $"  \"topN\": 3,\n" +
                    $"  \"source\": \"WebWithBing\"\n" +
                    $"}}", true, "Company Overview");
                
                var searchService = new Services.LuminaSearchService(token, _luminaConfig);
                var wikiSearchStart = DateTime.Now;
                var wikipediaSearchResults = await searchService.ExecuteWebSearchAsync($"{request.CompanyName} Wikipedia", 3);
                var wikiSearchDuration = (DateTime.Now - wikiSearchStart).TotalMilliseconds;
                
                var wikipediaUrl = wikipediaSearchResults
                    .FirstOrDefault(r => r.Url?.Contains("wikipedia.org/wiki/") == true)?.Url;
                
                if (string.IsNullOrEmpty(wikipediaUrl))
                {
                    _apiLogService.AddLog("Lumina Search API", "POST /api/sonicberry/search (Step 1/2)", 
                        $"Result\n" +
                        $"{{\n" +
                        $"  \"results_count\": {wikipediaSearchResults.Count},\n" +
                        $"  \"wikipedia_url\": null,\n" +
                        $"  \"response_time_ms\": {wikiSearchDuration:F0}\n" +
                        $"}}", false, "Company Overview");
                    return Json(new { success = false, error = "No Wikipedia page found for this company" });
                }
                
                _apiLogService.AddLog("Lumina Search API", "POST /api/sonicberry/search (Step 1/2)", 
                    $"Result\n" +
                    $"{{\n" +
                    $"  \"results_count\": {wikipediaSearchResults.Count},\n" +
                    $"  \"wikipedia_url\": \"{wikipediaUrl}\",\n" +
                    $"  \"response_time_ms\": {wikiSearchDuration:F0}\n" +
                    $"}}", true, "Company Overview");
                
                // Step 2: Open the Wikipedia page
                var openService = new Services.LuminaOpenService(token, _luminaConfig);
                var openResult = await openService.OpenContentWithLinksAsync(wikipediaUrl);
                
                if (string.IsNullOrEmpty(openResult.SessionId))
                {
                    return Json(new { success = false, error = "Failed to open Wikipedia page", logsUpdated = true });
                }
                
                // Step 3: Extract company info using Find API
                _apiLogService.AddLog("Lumina Find API", "POST /api/sonicberry/find (Step 2/2)", 
                    $"Parameters\n" +
                    $"{{\n" +
                    $"  \"url\": \"{wikipediaUrl}\",\n" +
                    $"  \"patterns\": [\"Founded\", \"Headquarters\", \"Revenue\", \"Industry\", \"Type\"]\n" +
                    $"}}", true, "Company Overview");
                
                var findService = new Services.LuminaFindService(token, _luminaConfig);
                var infoStartTime = DateTime.Now;
                var companyInfo = await findService.ExtractCompanyInfoAsync(wikipediaUrl, openResult.SessionId);
                var infoDuration = (DateTime.Now - infoStartTime).TotalMilliseconds;
                
                if (companyInfo != null && companyInfo.Fields.Any())
                {
                    var fieldsJson = string.Join(",\n    ", companyInfo.Fields.Select(f => 
                        $"\"{f.FieldName}\": \"{f.Content.Substring(0, Math.Min(30, f.Content.Length))}{(f.Content.Length > 30 ? "..." : "")}\""));
                    _apiLogService.AddLog("Lumina Find API", "POST /api/sonicberry/find (Step 2/2)", 
                        $"Result\n" +
                        $"{{\n" +
                        $"  \"fields_extracted\": {companyInfo.Fields.Count},\n" +
                        $"  \"data\": {{\n    {fieldsJson}\n  }},\n" +
                        $"  \"response_time_ms\": {infoDuration:F0}\n" +
                        $"}}", true, "Company Overview");
                    
                    return Json(new { 
                        success = true, 
                        companyInfo = new {
                            url = companyInfo.Url,
                            fields = companyInfo.Fields.Select(f => new {
                                fieldName = f.FieldName,
                                content = f.Content
                            }).ToList()
                        },
                        logsUpdated = true 
                    });
                }
                else
                {
                    _apiLogService.AddLog("Lumina Find API", "POST /api/sonicberry/find (Step 2/2)", 
                        $"Result\n" +
                        $"{{\n" +
                        $"  \"fields_extracted\": 0,\n" +
                        $"  \"response_time_ms\": {infoDuration:F0}\n" +
                        $"}}", false, "Company Overview");
                    return Json(new { success = false, error = "No company information found on the page", logsUpdated = true });
                }
            }
            catch (Exception ex)
            {
                _apiLogService.AddLog("Lumina Find", "ExtractCompanyInfo", 
                    $"❌ Error: {ex.Message}", false);
                return Json(new { success = false, error = ex.Message, logsUpdated = true });
            }
        }

        #endregion

        #region Content Operations

        /// <summary>
        /// Open and extract full content from a URL using Lumina Open API.
        /// Called when user clicks a search result to view full content.
        /// Returns JSON response with content, links, and navigation context.
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
                    $"  Method: POST /api/sonicberry/open\n" +
                    $"  Auth: Bearer token (OAuth 2.0)\n\n" +
                    $"📝 Open Request:\n" +
                    $"  RefId (URL): {request.Url}\n" +
                    $"  SessionId: {request.SessionId ?? "New session"}\n" +
                    $"  Purpose: Extract full page content and discover links");
                
                var openService = new Services.LuminaOpenService(token, _luminaConfig);
                var startTime = DateTime.Now;
                var result = await openService.OpenContentWithLinksAsync(request.Url, request.SessionId);
                var duration = (DateTime.Now - startTime).TotalMilliseconds;
                
                // Convert dynamic links to LinkInfo
                var linksList = new List<LinkInfo>();
                for (int i = 0; i < result.Links.Count && i < 10; i++)
                {
                    try
                    {
                        var link = result.Links[i];
                        string linkId = link.LinkId?.ToString() ?? i.ToString();
                        string name = link.Name?.ToString() ?? $"Link {i}";
                        string url = link.Url?.ToString() ?? "";
                        
                        if (!string.IsNullOrEmpty(name) && !name.StartsWith("[[["))
                        {
                            linksList.Add(new LinkInfo
                            {
                                LinkId = linkId,
                                Name = name,
                                Url = url
                            });
                        }
                    }
                    catch
                    {
                        // Skip invalid links
                    }
                }
                
                // Convert PageContext
                PageContextDto? pageContextDto = null;
                if (result.PageContext != null)
                {
                    try
                    {
                        pageContextDto = new PageContextDto
                        {
                            Turn = (int)(result.PageContext.Turn ?? 0),
                            Action = result.PageContext.Action?.ToString() ?? "view",
                            Id = (int)(result.PageContext.Id ?? 0)
                        };
                    }
                    catch { }
                }
                
                _apiLogService.AddLog("Lumina Open", "OpenContent", 
                    $"✅ Content retrieved\n" +
                    $"  Content length: {result.Content.Length} chars\n" +
                    $"  Links found: {linksList.Count}\n" +
                    $"  Session ID: {result.SessionId}\n" +
                    $"  Response time: {duration:F0}ms");
                
                var response = new OpenContentResponse
                {
                    Success = true,
                    Content = result.Content,
                    SessionId = result.SessionId,
                    Links = linksList,
                    PageContext = pageContextDto,
                    Url = result.Url,
                    Title = result.Title,
                    LogsUpdated = true
                };
                
                return Json(response);
            }
            catch (ArgumentException ex)
            {
                                _apiLogService.AddLog("Lumina Open", "OpenContent", $"❌ Validation error: {ex.Message}", false);
                return Json(new OpenContentResponse { Success = false, Error = ex.Message, LogsUpdated = true });
            }
            catch (HttpRequestException ex)
            {
                                _apiLogService.AddLog("Lumina Open", "OpenContent", $"❌ Network error: {ex.Message}", false);
                return Json(new OpenContentResponse { Success = false, Error = "Network error. Please check your internet connection.", LogsUpdated = true });
            }
            catch (Exception ex)
            {
                                _apiLogService.AddLog("Lumina Open", "OpenContent", $"❌ Error: {ex.Message}", false);
                return Json(new OpenContentResponse { Success = false, Error = $"Failed to open content: {ex.Message}", LogsUpdated = true });
            }
        }

        /// <summary>
        /// Click a link within an opened page using Lumina Click API.
        /// Enables navigation through article links for deep content exploration.
        /// </summary>
        /// <param name="request">Request containing session ID, link ID, and page context</param>
        [HttpPost]
        public async Task<IActionResult> ClickLink([FromBody] ClickLinkRequest request)
        {
            if (string.IsNullOrEmpty(request.SessionId))
            {
                return Json(new OpenContentResponse { Success = false, Error = "Session ID is required" });
            }

            if (string.IsNullOrEmpty(request.LinkId))
            {
                return Json(new OpenContentResponse { Success = false, Error = "Link ID is required" });
            }

            var token = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(token))
            {
                return Json(new OpenContentResponse { Success = false, Error = "Please log in first" });
            }

            try
            {
                _apiLogService.AddLog("Lumina Click", "ClickLink", 
                    $"📋 API Configuration:\n" +
                    $"  Endpoint: {_luminaConfig.ApiEndpoint}\n" +
                    $"  Method: POST (via SDK ClickAsync)\n" +
                    $"  Auth: Bearer token (OAuth 2.0)\n\n" +
                    $"📝 Click Request:\n" +
                    $"  SessionId: {request.SessionId}\n" +
                    $"  LinkId: {request.LinkId}\n" +
                    $"  PageContext: Turn={request.PageContext.Turn}, Action={request.PageContext.Action}, Id={request.PageContext.Id}");
                
                var openService = new Services.LuminaOpenService(token, _luminaConfig);
                var startTime = DateTime.Now;
                
                // Convert PageContextDto to dynamic object for API
                dynamic pageContext = new
                {
                    Turn = request.PageContext.Turn,
                    Action = request.PageContext.Action,
                    Id = request.PageContext.Id
                };
                
                var result = await openService.ClickLinkAsync(request.SessionId, request.LinkId, pageContext);
                var duration = (DateTime.Now - startTime).TotalMilliseconds;
                
                // Convert links
                var linksList = new List<LinkInfo>();
                for (int i = 0; i < result.Links.Count && i < 10; i++)
                {
                    try
                    {
                        var link = result.Links[i];
                        string linkId = link.LinkId?.ToString() ?? i.ToString();
                        string name = link.Name?.ToString() ?? $"Link {i}";
                        string url = link.Url?.ToString() ?? "";
                        
                        if (!string.IsNullOrEmpty(name) && !name.StartsWith("[[["))
                        {
                            linksList.Add(new LinkInfo
                            {
                                LinkId = linkId,
                                Name = name,
                                Url = url
                            });
                        }
                    }
                    catch
                    {
                        // Skip invalid links
                    }
                }
                
                // Convert PageContext
                PageContextDto? pageContextDto = null;
                if (result.PageContext != null)
                {
                    try
                    {
                        pageContextDto = new PageContextDto
                        {
                            Turn = (int)(result.PageContext.Turn ?? 0),
                            Action = result.PageContext.Action?.ToString() ?? "view",
                            Id = (int)(result.PageContext.Id ?? 0)
                        };
                    }
                    catch { }
                }
                
                _apiLogService.AddLog("Lumina Click", "ClickLink", 
                    $"✅ Navigated to new page\n" +
                    $"  URL: {result.Url}\n" +
                    $"  Title: {result.Title}\n" +
                    $"  Content length: {result.Content.Length} chars\n" +
                    $"  Links found: {linksList.Count}\n" +
                    $"  Response time: {duration:F0}ms");
                
                var response = new OpenContentResponse
                {
                    Success = true,
                    Content = result.Content,
                    SessionId = result.SessionId,
                    Links = linksList,
                    PageContext = pageContextDto,
                    Url = result.Url,
                    Title = result.Title,
                    LogsUpdated = true
                };
                
                return Json(response);
            }
            catch (Exception ex)
            {
                                _apiLogService.AddLog("Lumina Click", "ClickLink", $"❌ Error: {ex.Message}", false);
                return Json(new OpenContentResponse { Success = false, Error = $"Failed to click link: {ex.Message}", LogsUpdated = true });
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
            // Don't include the outer <div class='api-logs'> wrapper - that stays in the DOM

            foreach (var log in logs)
            {
                var statusClass = log.Success ? "success" : "danger";
                var statusIcon = log.Success ? "✅" : "❌";
                
                // Add feature badge if available
                var featureBadge = !string.IsNullOrEmpty(log.Feature) 
                    ? $"<span class='log-feature badge bg-primary'>{System.Web.HttpUtility.HtmlEncode(log.Feature)}</span>" 
                    : "";
                
                logsHtml.AppendLine($@"
                <div class='log-entry log-{statusClass}'>
                    <div class='log-header'>
                        <span class='log-icon'>{statusIcon}</span>
                        <span class='log-time'>{log.Timestamp:HH:mm:ss.fff}</span>
                        {featureBadge}
                        <span class='log-api badge bg-{statusClass}'>{System.Web.HttpUtility.HtmlEncode(log.ApiName)}</span>
                        <span class='log-operation'>{System.Web.HttpUtility.HtmlEncode(log.Operation)}</span>
                    </div>
                    <div class='log-details'>{System.Web.HttpUtility.HtmlEncode(log.Details)}</div>
                </div>");
            }
            
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
        /// Test CUA with MSN Money - SSE streaming endpoint for real-time progress
        /// Uses computer pool for resource reuse (3-minute keep-alive)
        /// </summary>
        [HttpGet]
        public async Task TestCuaMsnMoneyStream(string companyName)
        {
            Response.ContentType = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["Connection"] = "keep-alive";

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

            var cuaService = new Services.LuminaCuaService(token, _luminaConfig);
            var userId = "user-from-token"; // Could be extracted from token claims in production

            try
            {
                await SendSseMessage("progress", "🔧 Initializing virtual computer...");
                
                var computerId = Guid.NewGuid().ToString("N");
                await cuaService.InitializeComputerAsync(computerId, userId, _azureAdConfig.TenantId);
                
                _apiLogService.AddLog("Lumina CUA", "POST /api/agent/computer/initialize",
                    $"Result\n{{\n  \"computerId\": \"{computerId}\",\n  \"status\": \"initialized\"\n}}", 
                    true, "Stock Price Screenshot");
                
                await SendSseMessage("progress", "✅ Virtual computer initialized");
                
                // Navigate and perform search
                await SendSseMessage("progress", "🌐 Opening MSN Money...");
                await cuaService.SearchCompanyOnMsnMoneyAsync(computerId, companyName);
                
                _apiLogService.AddLog("Lumina CUA", "POST /api/agent/computer/do",
                    $"Result\n{{\n  \"action\": \"search_completed\",\n  \"query\": \"{companyName}\"\n}}", 
                    true, "Stock Price Screenshot");
                
                await SendSseMessage("progress", "✅ Search completed");
                
                // Capture screenshot
                await SendSseMessage("progress", "📸 Capturing screenshot...");
                await Task.Delay(1000); // Wait for page to load
                
                var screenshot = await cuaService.GetComputerScreenshotAsync(computerId);
                
                if (screenshot?.Content?.Success == true)
                {
                    _apiLogService.AddLog("Lumina CUA", "POST /api/agent/computer/get",
                        $"Result\n{{\n  \"width\": {screenshot.Content.Width},\n  \"height\": {screenshot.Content.Height}\n}}", 
                        true, "Stock Price Screenshot");
                    
                    await SendSseMessage("screenshot", System.Text.Json.JsonSerializer.Serialize(new
                    {
                        image = $"data:image/png;base64,{screenshot.Content.Screenshot}",
                        width = screenshot.Content.Width,
                        height = screenshot.Content.Height
                    }));
                    await Response.Body.FlushAsync();

                    await SendSseMessage("complete", "✅ All operations completed successfully!");
                }
                else
                {
                    await SendSseMessage("error", "Failed to capture screenshot");
                }
            }
            catch (Exception ex)
            {
                _apiLogService.AddLog("Lumina CUA", "Error",
                    $"Result\n{{\n  \"error\": \"{ex.Message}\"\n}}", 
                    false, "Stock Price Screenshot");
                await SendSseMessage("error", ex.Message);
            }
        }

        private async Task SendSseMessage(string eventType, string data)
        {
            var message = $"event: {eventType}\ndata: {data}\n\n";
            await Response.WriteAsync(message);
        }

        #endregion
    }
}