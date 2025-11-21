using Microsoft.Lumina.Client.Models.Sonicberry;
using LuminaSearchConsole.Services;

namespace LuminaSearchConsole.Services.SearchApis
{
    /// <summary>
    /// Demonstrates Lumina Open API: POST /api/sonicberry/open
    /// 
    /// Open API retrieves FULL page content (all text, images, metadata) from a URL.
    /// This is different from Search API which only returns snippets.
    /// 
    /// UI Integration:
    /// - When user clicks a news title in search results → opens content modal with full article
    /// - Content modal shows: full text, extracted links for navigation, breadcrumb trail
    /// - "Related Links" section allows clicking through to referenced pages (Click API)
    /// 
    /// Frontend Code Reference:
    /// - wwwroot/js/navigation.js → openFullContent(resultId, title, url, sourceType)
    /// - wwwroot/js/navigation.js → clickRelatedLink(link, currentBreadcrumbs)
    /// 
    /// How to Try:
    /// 1. Search for "Microsoft" in the main search box
    /// 2. Click any news title in "📰 Latest News" section
    /// 3. A modal opens showing full article content (this uses Open API)
    /// 4. Click any link in "Related Links" section (this uses Click API)
    /// 5. Check API logs at bottom of page - you'll see POST /api/sonicberry/open entries
    /// </summary>
    public class LuminaOpenApiService
    {
        private readonly ApiLogService _apiLogService;
        private readonly ILogger<LuminaOpenApiService> _logger;

        public LuminaOpenApiService(
            ApiLogService apiLogService,
            ILogger<LuminaOpenApiService> logger)
        {
            _apiLogService = apiLogService;
            _logger = logger;
        }

        /// <summary>
        /// Open API - Retrieves full page content from a URL
        /// 
        /// API Endpoint: POST /api/sonicberry/open
        /// Request Body: { "Requests": [{ "RefId": "https://..." }], "ToolState": { "SessionId": "..." } }
        /// Response: Full page text, links, navigation context
        /// 
        /// Use Case: When user clicks a news title to read full article
        /// 
        /// Example:
        /// - Input: url = "https://www.bbc.com/news/technology-12345"
        /// - Output: Full article text + related links for navigation
        /// 
        /// Note: This method requires a LuminaSearchService instance (created with user token)
        /// </summary>
        public async Task<OpenApiResult> OpenContentAsync(LuminaSearchService searchService, string url, string? sessionId = null)
        {
            try
            {
                _logger.LogInformation("Opening content from URL: {Url}", url);

                // Create Open API request and call Lumina
                var startTime = DateTime.UtcNow;
                var openResult = await searchService.OpenContentWithLinksAsync(url, sessionId);
                var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;

                // Log API call for debugging
                _apiLogService.AddLog(
                    apiName: "Lumina Open",
                    operation: "OpenContent",
                    details: $"📋 API Call:\n" +
                             $"  Endpoint: POST /api/sonicberry/open\n" +
                             $"  URL: {url}\n" +
                             $"  SessionId: {sessionId ?? "New session"}\n" +
                             $"  Duration: {duration}ms\n\n" +
                             $"✅ Response:\n" +
                             $"  Content: {openResult.Content.Length} chars\n" +
                             $"  Links: {openResult.Links.Count}\n" +
                             $"  New SessionId: {openResult.SessionId}"
                );

                if (string.IsNullOrEmpty(openResult.Content))
                {
                    _logger.LogWarning("Open API returned no content for URL: {Url}", url);
                    return new OpenApiResult
                    {
                        Success = false,
                        ErrorMessage = "No content returned from Open API"
                    };
                }

                // Convert dynamic links to structured LinkInfo
                var relatedLinks = new List<LinkInfo>();
                for (int i = 0; i < openResult.Links.Count && i < 10; i++)
                {
                    try
                    {
                        var link = openResult.Links[i];
                        string linkId = link.LinkId?.ToString() ?? i.ToString();
                        string name = link.Name?.ToString() ?? $"Link {i}";
                        string linkUrl = link.Url?.ToString() ?? "";
                        
                        // Skip placeholder links
                        if (!string.IsNullOrEmpty(name) && !name.StartsWith("[[["))
                        {
                            relatedLinks.Add(new LinkInfo
                            {
                                LinkId = linkId,
                                Text = name,
                                Url = linkUrl
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error parsing link at index {Index}", i);
                    }
                }

                _logger.LogInformation(
                    "Open API succeeded: {TextLength} chars, {LinkCount} links",
                    openResult.Content.Length,
                    relatedLinks.Count
                );

                return new OpenApiResult
                {
                    Success = true,
                    Content = openResult.Content,
                    RelatedLinks = relatedLinks,
                    Title = openResult.Title,
                    SessionId = openResult.SessionId,
                    PageContext = openResult.PageContext,
                    Url = openResult.Url
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening content from URL: {Url}", url);
                _apiLogService.AddLog("Lumina Open", "OpenContent", $"❌ Error: {ex.Message}", false);
                
                return new OpenApiResult
                {
                    Success = false,
                    ErrorMessage = $"Error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Click API - Navigates to a related link and retrieves its content
        /// 
        /// Technical Note: Click is integrated into the Lumina SDK via ClickAsync method.
        /// It requires session context from a previous Open call.
        /// 
        /// Use Case: When user clicks a link in "Related Links" section of content modal
        /// 
        /// Implementation:
        /// 1. Call Click API with linkId and session context
        /// 2. SDK handles navigation and returns new page content
        /// 3. Update breadcrumb trail for navigation history
        /// 
        /// Example:
        /// - Current page: "Microsoft announces new product"
        /// - User clicks related link: "See full press release" (linkId="5")
        /// - This method navigates to the press release page
        /// - Breadcrumbs update: Home → News Article → Press Release
        /// </summary>
        public async Task<OpenApiResult> ClickLinkAsync(
            LuminaSearchService searchService,
            string sessionId,
            string linkId,
            dynamic pageContext)
        {
            try
            {
                _logger.LogInformation("Clicking link: {LinkId} in session: {SessionId}", linkId, sessionId);

                // Call Click API via LuminaSearchService
                var startTime = DateTime.UtcNow;
                var clickResult = await searchService.ClickLinkAsync(sessionId, linkId, pageContext);
                var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;

                // Log API call for debugging
                _apiLogService.AddLog(
                    apiName: "Lumina Click",
                    operation: "ClickLink",
                    details: $"📋 API Call:\n" +
                             $"  Endpoint: POST /api/sonicberry/click (via Open SDK)\n" +
                             $"  SessionId: {sessionId}\n" +
                             $"  LinkId: {linkId}\n" +
                             $"  Duration: {duration}ms\n\n" +
                             $"✅ Response:\n" +
                             $"  New URL: {clickResult.Url}\n" +
                             $"  Content: {clickResult.Content.Length} chars\n" +
                             $"  Links: {clickResult.Links.Count}"
                );

                // Convert dynamic links to structured LinkInfo
                var relatedLinks = new List<LinkInfo>();
                for (int i = 0; i < clickResult.Links.Count && i < 10; i++)
                {
                    try
                    {
                        var link = clickResult.Links[i];
                        string newLinkId = link.LinkId?.ToString() ?? i.ToString();
                        string name = link.Name?.ToString() ?? $"Link {i}";
                        string linkUrl = link.Url?.ToString() ?? "";
                        
                        if (!string.IsNullOrEmpty(name) && !name.StartsWith("[[["))
                        {
                            relatedLinks.Add(new LinkInfo
                            {
                                LinkId = newLinkId,
                                Text = name,
                                Url = linkUrl
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error parsing link at index {Index}", i);
                    }
                }

                string navigatedUrl = clickResult.Url;
                _logger.LogInformation("Click succeeded, navigated to: {Url}", navigatedUrl);

                return new OpenApiResult
                {
                    Success = true,
                    Content = clickResult.Content,
                    RelatedLinks = relatedLinks,
                    Title = clickResult.Title,
                    SessionId = sessionId,
                    PageContext = clickResult.PageContext,
                    Url = clickResult.Url
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clicking link: {LinkId}", linkId);
                _apiLogService.AddLog("Lumina Click", "ClickLink", $"❌ Error: {ex.Message}", false);
                
                return new OpenApiResult
                {
                    Success = false,
                    ErrorMessage = $"Error: {ex.Message}"
                };
            }
        }
    }

    /// <summary>
    /// Result model for Open API and Click API calls
    /// </summary>
    public class OpenApiResult
    {
        /// <summary>
        /// Whether the API call succeeded
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Full page content (text)
        /// </summary>
        public string Content { get; set; } = "";

        /// <summary>
        /// Page title
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// List of related links that can be clicked for navigation
        /// </summary>
        public List<LinkInfo> RelatedLinks { get; set; } = new List<LinkInfo>();

        /// <summary>
        /// Session ID for maintaining navigation context
        /// </summary>
        public string SessionId { get; set; } = "";

        /// <summary>
        /// Page context (needed for Click API)
        /// </summary>
        public dynamic? PageContext { get; set; }

        /// <summary>
        /// Actual URL of the opened page
        /// </summary>
        public string Url { get; set; } = "";

        /// <summary>
        /// Error message if Success = false
        /// </summary>
        public string ErrorMessage { get; set; } = "";
    }

    /// <summary>
    /// Link information extracted by Open/Click API
    /// </summary>
    public class LinkInfo
    {
        /// <summary>
        /// Link identifier (used for Click API)
        /// </summary>
        public string LinkId { get; set; } = "";
        
        /// <summary>
        /// Display text for the link
        /// </summary>
        public string Text { get; set; } = "";
        
        /// <summary>
        /// Target URL of the link
        /// </summary>
        public string Url { get; set; } = "";
    }
}

