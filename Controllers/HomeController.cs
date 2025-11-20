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
                _apiLogService.AddLog("Lumina Search", "BatchSearch", 
                    $"📋 Batch Search Request:\n" +
                    $"  Company: '{model.Query}'\n" +
                    $"  Queries: Stock price + Latest news\n" +
                    $"  TopN: {model.TopResults}");
                
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
                    _apiLogService.AddLog("Lumina Search", "WikipediaSearch", 
                        $"📋 Searching Wikipedia for '{model.Query}'");
                    
                    var wikiSearchStart = DateTime.Now;
                    var wikipediaSearchResults = await searchService.ExecuteWebSearchAsync($"{model.Query} Wikipedia", 3);
                    var wikiSearchDuration = (DateTime.Now - wikiSearchStart).TotalMilliseconds;
                    
                    // Find Wikipedia URL from search results
                    var wikipediaUrl = wikipediaSearchResults
                        .FirstOrDefault(r => r.Url?.Contains("wikipedia.org/wiki/") == true)?.Url;
                    
                    if (!string.IsNullOrEmpty(wikipediaUrl))
                    {
                        _apiLogService.AddLog("Lumina Search", "WikipediaSearch", 
                            $"✅ Found: {wikipediaUrl} ({wikiSearchDuration:F0}ms)");
                        
                        _apiLogService.AddLog("Lumina Find", "ExtractCompanyInfo", 
                            $"📋 Extracting company info from Wikipedia");
                        
                        var infoStartTime = DateTime.Now;
                        var companyInfo = await searchService.ExtractCompanyInfoAsync(wikipediaUrl);
                        var infoDuration = (DateTime.Now - infoStartTime).TotalMilliseconds;
                        
                        if (companyInfo != null && companyInfo.Fields.Any())
                        {
                            _apiLogService.AddLog("Lumina Find", "ExtractCompanyInfo", 
                                $"✅ Extracted {companyInfo.Fields.Count} fields ({infoDuration:F0}ms)");
                            
                            model.CompanyInfo = companyInfo;
                        }
                        else
                        {
                            _apiLogService.AddLog("Lumina Find", "ExtractCompanyInfo", 
                                $"⚠️ No information found ({infoDuration:F0}ms)");
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
                    $"📋 Query: '{model.Query}', TopN: {model.TopResults}");
                
                var searchService = new LuminaSearchService(token, _luminaConfig);
                var startTime = DateTime.Now;
                var results = await searchService.ExecuteWebSearchAsync(model.Query, model.TopResults);
                var duration = (DateTime.Now - startTime).TotalMilliseconds;
                
                _apiLogService.AddLog("Lumina Search", "WebSearch", 
                    $"✅ Found {results.Count} results ({duration:F0}ms)");
                
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
                
                var searchService = new LuminaSearchService(token, _luminaConfig);
                var startTime = DateTime.Now;
                var result = await searchService.OpenContentWithLinksAsync(request.Url, request.SessionId);
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
                _logger.LogWarning("Invalid URL parameter: {Message}", ex.Message);
                _apiLogService.AddLog("Lumina Open", "OpenContent", $"❌ Validation error: {ex.Message}", false);
                return Json(new OpenContentResponse { Success = false, Error = ex.Message, LogsUpdated = true });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error while opening content from: {Url}", request.Url);
                _apiLogService.AddLog("Lumina Open", "OpenContent", $"❌ Network error: {ex.Message}", false);
                return Json(new OpenContentResponse { Success = false, Error = "Network error. Please check your internet connection.", LogsUpdated = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Open content failed for URL: {Url}", request.Url);
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
                
                var searchService = new LuminaSearchService(token, _luminaConfig);
                var startTime = DateTime.Now;
                
                // Convert PageContextDto to dynamic object for API
                dynamic pageContext = new
                {
                    Turn = request.PageContext.Turn,
                    Action = request.PageContext.Action,
                    Id = request.PageContext.Id
                };
                
                var result = await searchService.ClickLinkAsync(request.SessionId, request.LinkId, pageContext);
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
                _logger.LogError(ex, "Click link failed for LinkId: {LinkId}", request.LinkId);
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

        #endregion
    }
}